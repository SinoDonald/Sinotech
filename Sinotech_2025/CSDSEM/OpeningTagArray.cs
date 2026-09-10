using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Sinotech_2025.CSDSEM
{
    /// <summary>
    /// 開口套管標籤自動正交排列與避讓指令
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class OpeningTagArray : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            ViewPlan activeView = doc.ActiveView as ViewPlan;
            if (activeView == null)
            {
                message = "請在平面視圖或模板視圖中執行此命令。";
                return Result.Failed;
            }

            // 1. 蒐集視圖中開口套管相關標籤
            List<BuiltInCategory> tagCategories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_PipeAccessoryTags,
                BuiltInCategory.OST_DuctAccessoryTags,
                BuiltInCategory.OST_CableTrayFittingTags
            };
            ElementMulticategoryFilter tagFilter = new ElementMulticategoryFilter(tagCategories);

            List<IndependentTag> tags = new FilteredElementCollector(doc, activeView.Id)
                .WherePasses(tagFilter)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .Where(t => !t.IsOrphaned)
                .ToList();

            if (tags.Count == 0)
            {
                TaskDialog.Show("提示", "當前視圖未發現開口套管標籤。");
                return Result.Succeeded;
            }

            // 2. 蒐集視圖內牆與結構樑作為導向參考
            List<BuiltInCategory> hostCategories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_StructuralFraming
            };
            ElementMulticategoryFilter hostFilter = new ElementMulticategoryFilter(hostCategories);
            List<Element> hostElements = new FilteredElementCollector(doc, activeView.Id)
                .WherePasses(hostFilter)
                .WhereElementIsNotElementType()
                .ToList();

            using (Transaction trans = new Transaction(doc, "開口套管標籤排版"))
            {
                trans.Start();

                TagOrthogonalEngine engine = new TagOrthogonalEngine(doc, activeView);
                int processedCount = engine.ArrangeTags(tags, hostElements);

                trans.Commit();
                TaskDialog.Show("成功", $"已成功將 {processedCount} 個標籤排版");
            }

            return Result.Succeeded;
        }
    }

    /// <summary>
    /// 正交標籤自動佈局與避讓核心引擎
    /// </summary>
    public class TagOrthogonalEngine
    {
        private readonly Document _doc;
        private readonly View _view;
        private readonly double _viewScale;

        // 幾何避讓參數 (單位: Feet)
        // 根據圖 4 需求加長引線：將 BaseOffset 從 1.2 提升至 2.5 Feet (約 76 cm)
        private const double BaseOffset = 2.5;       // 標籤離開口/牆面基礎偏移距離
        private const double SlotSpacing = 0.9;      // 同排標籤最小間距
        private const double LayerSpacing = 1.2;     // 多排避讓層疊間距
        private const double TagWidthEst = 2.0;      // 估算標籤寬度
        private const double TagHeightEst = 0.7;     // 估算標籤高度

        public TagOrthogonalEngine(Document doc, View view)
        {
            _doc = doc;
            _view = view;
            _viewScale = view.Scale;
        }

        /// <summary>
        /// 執行標籤自動排版與引線計算
        /// </summary>
        /// <param name="tags">標籤列表</param>
        /// <param name="hostElements">宿主牆樑元件列表</param>
        /// <returns>處理數量</returns>
        public int ArrangeTags(List<IndependentTag> tags, List<Element> hostElements)
        {
            List<TagData> tagDataList = new List<TagData>();

            // 關閉引線以取得準確的標籤邊界框
            foreach (var tag in tags)
            {
                try { tag.HasLeader = false; } catch { }
            }
            _doc.Regenerate();

            // A. 解析標籤與宿主幾何關係
            foreach (var tag in tags)
            {
                Reference refElem = tag.GetTaggedReferences().FirstOrDefault();
                if (refElem == null) continue;

                Element targetElem = _doc.GetElement(refElem.ElementId);
                if (targetElem == null) continue;

                XYZ anchorPt = GetElementCenter(targetElem);
                HostOrientation orientation = DetectHostOrientation(targetElem, anchorPt, hostElements, out XYZ hostDir);

                BoundingBoxXYZ elemBox = targetElem.get_BoundingBox(_view);
                BoundingBoxXYZ tagBox = tag.get_BoundingBox(_view);

                double tWidth = TagWidthEst;
                double tHeight = TagHeightEst;
                if (tagBox != null)
                {
                    double w = tagBox.Max.X - tagBox.Min.X;
                    double h = tagBox.Max.Y - tagBox.Min.Y;
                    if (w > 0.1) tWidth = w + 0.5; // 加入寬度緩衝區
                    if (h > 0.1) tHeight = h + 0.5; // 加入高度緩衝區
                }

                tagDataList.Add(new TagData
                {
                    Tag = tag,
                    TargetReference = refElem,
                    AnchorPoint = anchorPt,
                    ElementBBox = elemBox,
                    TagWidth = tWidth,
                    TagHeight = tHeight,
                    Orientation = orientation,
                    HostDirection = hostDir
                });
            }

            // B. 對所有標籤進行全局優先順序排版
            var sortedTags = tagDataList.OrderBy(t => t.AnchorPoint.X).ThenBy(t => t.AnchorPoint.Y).ToList();
            List<PlacedTagFootprint> placed = new List<PlacedTagFootprint>();

            foreach (var data in sortedTags)
            {
                PlacedTagFootprint bestCandidate = null;

                // 1. 優先嘗試 4 個正交直線方向 (中心點出發)
                foreach (var candidate in GenerateStraightCandidates(data))
                {
                    if (!IsCollision(candidate, placed))
                    {
                        bestCandidate = candidate;
                        break;
                    }
                }

                // 2. 如果 4 個方向都被干涉，才使用滑動錨點與轉折引線，或者外推
                if (bestCandidate == null)
                {
                    foreach (var candidate in GenerateSlidingCandidates(data))
                    {
                        if (!IsCollision(candidate, placed))
                        {
                            bestCandidate = candidate;
                            break;
                        }
                    }
                }

                // 3. Fallback: 真的完全找不到位置，硬上第一個直線
                if (bestCandidate == null)
                {
                    bestCandidate = GenerateStraightCandidates(data).FirstOrDefault();
                }

                if (bestCandidate != null)
                {
                    data.CalculatedHeadPos = bestCandidate.Head;
                    data.CalculatedAnchor = bestCandidate.FinalAnchor;
                    data.CalculatedElbow = bestCandidate.FinalElbow;
                    placed.Add(bestCandidate);
                }
            }

            // C. 寫回 Revit 模型並產生引線
            _doc.Regenerate();

            foreach (var data in tagDataList)
            {
                ApplyTagPositionAndElbow(data);
            }

            return tagDataList.Count;
        }

        private IEnumerable<PlacedTagFootprint> GenerateStraightCandidates(TagData data)
        {
            if (data.ElementBBox == null) yield break;

            double cx = (data.ElementBBox.Min.X + data.ElementBBox.Max.X) / 2.0;
            double cy = (data.ElementBBox.Min.Y + data.ElementBBox.Max.Y) / 2.0;
            double z = data.AnchorPoint.Z;

            var top = new { Anchor = new XYZ(cx, data.ElementBBox.Max.Y, z), Head = new XYZ(cx, data.ElementBBox.Max.Y + BaseOffset, z) };
            var bottom = new { Anchor = new XYZ(cx, data.ElementBBox.Min.Y, z), Head = new XYZ(cx, data.ElementBBox.Min.Y - BaseOffset, z) };
            var left = new { Anchor = new XYZ(data.ElementBBox.Min.X, cy, z), Head = new XYZ(data.ElementBBox.Min.X - BaseOffset, cy, z) };
            var right = new { Anchor = new XYZ(data.ElementBBox.Max.X, cy, z), Head = new XYZ(data.ElementBBox.Max.X + BaseOffset, cy, z) };

            if (data.Orientation == HostOrientation.Horizontal)
            {
                yield return GetStraightFootprint(data, top.Anchor, top.Head);
                yield return GetStraightFootprint(data, bottom.Anchor, bottom.Head);
            }
            else
            {
                yield return GetStraightFootprint(data, left.Anchor, left.Head);
                yield return GetStraightFootprint(data, right.Anchor, right.Head);
            }
        }

        private IEnumerable<PlacedTagFootprint> GenerateSlidingCandidates(TagData data)
        {
            double cx = data.AnchorPoint.X;
            double cy = data.AnchorPoint.Y;
            if (data.ElementBBox != null)
            {
                cx = (data.ElementBBox.Min.X + data.ElementBBox.Max.X) / 2.0;
                cy = (data.ElementBBox.Min.Y + data.ElementBBox.Max.Y) / 2.0;
            }

            for (int layer = 0; layer < 5; layer++)
            {
                double gapW = data.TagWidth + 1.0;
                double gapH = data.TagHeight + 1.0;
                double layerOffsetW = (layer % 2 == 1) ? (gapW / 2.0) : 0.0;
                double layerOffsetH = (layer % 2 == 1) ? (gapH / 2.0) : 0.0;

                for (int shiftIndex = 1; shiftIndex <= 10; shiftIndex++)
                {
                    double shiftDir = (shiftIndex % 2 == 1) ? 1.0 : -1.0;

                    if (data.Orientation == HostOrientation.Horizontal)
                    {
                        double targetX = cx + layerOffsetW + shiftDir * ((shiftIndex + 1) / 2) * gapW;
                        double yDist = BaseOffset + layer * LayerSpacing;
                        yield return GetElbowFootprint(data, targetX, (data.ElementBBox != null ? data.ElementBBox.Max.Y : cy) + yDist, true);
                        yield return GetElbowFootprint(data, targetX, (data.ElementBBox != null ? data.ElementBBox.Min.Y : cy) - yDist, true);
                    }
                    else
                    {
                        double targetY = cy + layerOffsetH + shiftDir * ((shiftIndex + 1) / 2) * gapH;
                        double xDist = BaseOffset + layer * LayerSpacing;
                        yield return GetElbowFootprint(data, (data.ElementBBox != null ? data.ElementBBox.Min.X : cx) - xDist, targetY, false);
                        yield return GetElbowFootprint(data, (data.ElementBBox != null ? data.ElementBBox.Max.X : cx) + xDist, targetY, false);
                    }
                }
            }
        }

        /// <summary>
        /// 將位置套用至標籤，並構建精確的 90 度正交 Elbow
        /// </summary>
        private void ApplyTagPositionAndElbow(TagData data)
        {
            try
            {
                // 1. 先關閉引線以利移動 TagHeadPosition
                data.Tag.HasLeader = false;
                data.Tag.TagHeadPosition = data.CalculatedHeadPos;

                // 2. 距離足夠時開啟引線並設置正確的錨點與轉折
                double dist = data.CalculatedAnchor.DistanceTo(data.CalculatedHeadPos);
                if (dist > 0.4)
                {
                    data.Tag.HasLeader = true;
                    data.Tag.LeaderEndCondition = LeaderEndCondition.Free;
                    data.Tag.SetLeaderEnd(data.TargetReference, data.CalculatedAnchor);
                    data.Tag.SetLeaderElbow(data.TargetReference, data.CalculatedElbow);
                }
            }
            catch
            {
                // 例外保護機制，確保單一標籤失敗不中斷整體流程
            }
        }

        /// <summary>
        /// 宿主方向多重強健判斷（解決圖 3 誤判問題）
        /// </summary>
        private HostOrientation DetectHostOrientation(Element targetElem, XYZ anchorPt, List<Element> hostElements, out XYZ hostDir)
        {
            hostDir = XYZ.BasisX;

            // 第一層判定：直接檢查套管元件的 Host 牆體
            if (targetElem is FamilyInstance fi && fi.Host is Wall hostWall)
            {
                if (hostWall.Location is LocationCurve wallCurve)
                {
                    XYZ dir = (wallCurve.Curve.GetEndPoint(1) - wallCurve.Curve.GetEndPoint(0)).Normalize();
                    hostDir = dir;
                    return Math.Abs(dir.X) > Math.Abs(dir.Y) ? HostOrientation.Horizontal : HostOrientation.Vertical;
                }
            }

            // 第二層判定：檢查套管/開口本身的 BoundingBox 長寬比
            BoundingBoxXYZ bbox = targetElem.get_BoundingBox(_view);
            if (bbox != null)
            {
                double dx = Math.Abs(bbox.Max.X - bbox.Min.X);
                double dy = Math.Abs(bbox.Max.Y - bbox.Min.Y);
                // 若開口本身明顯為縱向延伸（例如縱向管道間開口）
                if (dy > dx * 1.3)
                {
                    hostDir = XYZ.BasisY;
                    return HostOrientation.Vertical;
                }
                else if (dx > dy * 1.3)
                {
                    hostDir = XYZ.BasisX;
                    return HostOrientation.Horizontal;
                }
            }

            // 第三層判定：擴大周圍牆與樑幾何投影檢索距離至 10 呎
            double minDist = double.MaxValue;
            HostOrientation orientation = HostOrientation.Horizontal;

            foreach (var host in hostElements)
            {
                if (host.Location is LocationCurve locCurve)
                {
                    Curve curve = locCurve.Curve;
                    XYZ proj = curve.Project(anchorPt).XYZPoint;
                    double d = anchorPt.DistanceTo(proj);

                    if (d < minDist && d < 10.0) // 擴大至 10 呎檢索範圍
                    {
                        minDist = d;
                        XYZ dir = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
                        hostDir = dir;

                        orientation = (Math.Abs(dir.X) > Math.Abs(dir.Y))
                            ? HostOrientation.Horizontal
                            : HostOrientation.Vertical;
                    }
                }
            }

            return orientation;
        }

        private PlacedTagFootprint GetStraightFootprint(TagData data, XYZ anchor, XYZ head)
        {
            PlacedTagFootprint fp = new PlacedTagFootprint();
            fp.SetTextBox(head.X, head.Y, data.TagWidth, data.TagHeight);
            fp.AddLine(anchor.X, anchor.Y, head.X, head.Y, 0.4);
            fp.FinalAnchor = anchor;
            fp.FinalElbow = anchor;
            fp.Head = head;
            return fp;
        }

        private PlacedTagFootprint GetElbowFootprint(TagData data, double targetX, double targetY, bool isHorizontal)
        {
            PlacedTagFootprint fp = new PlacedTagFootprint();
            fp.SetTextBox(targetX, targetY, data.TagWidth, data.TagHeight);

            XYZ anchor = data.AnchorPoint;
            if (data.ElementBBox != null)
            {
                double cx = (data.ElementBBox.Min.X + data.ElementBBox.Max.X) / 2.0;
                double cy = (data.ElementBBox.Min.Y + data.ElementBBox.Max.Y) / 2.0;

                if (isHorizontal)
                {
                    double ay = targetY > data.AnchorPoint.Y ? data.ElementBBox.Max.Y : data.ElementBBox.Min.Y;
                    anchor = new XYZ(cx, ay, data.AnchorPoint.Z);
                }
                else
                {
                    double ax = targetX > data.AnchorPoint.X ? data.ElementBBox.Max.X : data.ElementBBox.Min.X;
                    anchor = new XYZ(ax, cy, data.AnchorPoint.Z);
                }
            }

            XYZ elbow = isHorizontal ? new XYZ(anchor.X, targetY, anchor.Z) : new XYZ(targetX, anchor.Y, anchor.Z);

            fp.AddLine(anchor.X, anchor.Y, elbow.X, elbow.Y, 0.4);
            fp.AddLine(elbow.X, elbow.Y, targetX, targetY, 0.4);

            fp.FinalAnchor = anchor;
            fp.FinalElbow = elbow;
            fp.Head = new XYZ(targetX, targetY, anchor.Z);
            return fp;
        }

        private bool IsCollision(PlacedTagFootprint candidate, List<PlacedTagFootprint> placed)
        {
            foreach (var p in placed)
                if (p.Intersects(candidate))
                    return true;
            return false;
        }

        private XYZ GetElementCenter(Element elem)
        {
            if (elem.Location is LocationPoint lp) return lp.Point;
            BoundingBoxXYZ bbox = elem.get_BoundingBox(_view);
            if (bbox != null) return (bbox.Min + bbox.Max) * 0.5;
            return XYZ.Zero;
        }

        private enum HostOrientation { Horizontal, Vertical }

        private class TagData
        {
            public IndependentTag Tag { get; set; }
            public Reference TargetReference { get; set; }
            public XYZ AnchorPoint { get; set; }
            public BoundingBoxXYZ ElementBBox { get; set; }
            public double TagWidth { get; set; }
            public double TagHeight { get; set; }
            public XYZ CalculatedHeadPos { get; set; }
            public XYZ CalculatedAnchor { get; set; }
            public XYZ CalculatedElbow { get; set; }
            public HostOrientation Orientation { get; set; }
            public XYZ HostDirection { get; set; }
        }

        private class PlacedTagFootprint
        {
            public BoundingBox2D TextBox { get; set; }
            public List<BoundingBox2D> LineBoxes { get; } = new List<BoundingBox2D>();
            public XYZ FinalAnchor { get; set; }
            public XYZ FinalElbow { get; set; }
            public XYZ Head { get; set; }

            public void SetTextBox(double x, double y, double w, double h)
            {
                TextBox = new BoundingBox2D(x, y, w, h);
            }

            public void AddLine(double x1, double y1, double x2, double y2, double thickness)
            {
                double minX = Math.Min(x1, x2) - thickness / 2.0;
                double maxX = Math.Max(x1, x2) + thickness / 2.0;
                double minY = Math.Min(y1, y2) - thickness / 2.0;
                double maxY = Math.Max(y1, y2) + thickness / 2.0;
                LineBoxes.Add(new BoundingBox2D((minX + maxX) / 2.0, (minY + maxY) / 2.0, maxX - minX, maxY - minY));
            }

            public bool Intersects(PlacedTagFootprint other)
            {
                if (TextBox != null && other.TextBox != null && TextBox.Intersects(other.TextBox))
                    return true;

                if (TextBox != null)
                {
                    foreach (var line in other.LineBoxes)
                        if (TextBox.Intersects(line))
                            return true;
                }

                if (other.TextBox != null)
                {
                    foreach (var line in LineBoxes)
                        if (line.Intersects(other.TextBox))
                            return true;
                }

                // Allow line vs line overlaps to avoid completely boxing out paths
                return false;
            }
        }

        private class BoundingBox2D
        {
            public double MinX { get; }
            public double MaxX { get; }
            public double MinY { get; }
            public double MaxY { get; }

            public BoundingBox2D(double cx, double cy, double w, double h)
            {
                MinX = cx - w / 2.0;
                MaxX = cx + w / 2.0;
                MinY = cy - h / 2.0;
                MaxY = cy + h / 2.0;
            }

            public bool Intersects(BoundingBox2D other)
            {
                return !(MaxX < other.MinX || MinX > other.MaxX || MaxY < other.MinY || MinY > other.MaxY);
            }
        }
    }
}