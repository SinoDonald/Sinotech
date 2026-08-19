using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Sinotech.CSDSEM
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

            // A. 解析標籤與宿主幾何關係
            foreach (var tag in tags)
            {
                Reference refElem = tag.GetTaggedReferences().FirstOrDefault();
                if (refElem == null) continue;

                Element targetElem = _doc.GetElement(refElem.ElementId);
                if (targetElem == null) continue;

                XYZ anchorPt = GetElementCenter(targetElem);
                HostOrientation orientation = DetectHostOrientation(targetElem, anchorPt, hostElements, out XYZ hostDir);

                tagDataList.Add(new TagData
                {
                    Tag = tag,
                    TargetReference = refElem,
                    AnchorPoint = anchorPt,
                    Orientation = orientation,
                    HostDirection = hostDir
                });
            }

            // B. 依據結構走向分組排版 (橫向群組 vs 縱向群組)
            var groups = tagDataList.GroupBy(d => d.Orientation);

            foreach (var group in groups)
            {
                if (group.Key == HostOrientation.Horizontal)
                {
                    ArrangeHorizontalGroup(group.ToList());
                }
                else
                {
                    ArrangeVerticalGroup(group.ToList());
                }
            }

            // C. 寫回 Revit 模型並產生 90 度正交引線
            _doc.Regenerate();

            foreach (var data in tagDataList)
            {
                ApplyTagPositionAndElbow(data);
            }

            return tagDataList.Count;
        }

        /// <summary>
        /// 處理橫向牆/樑（標籤向上或向下排隊，引線先垂直再水平）
        /// </summary>
        private void ArrangeHorizontalGroup(List<TagData> list)
        {
            var sorted = list.OrderBy(t => t.AnchorPoint.X).ToList();
            List<BoundingBox2D> placedBoxes = new List<BoundingBox2D>();

            foreach (var data in sorted)
            {
                double targetX = data.AnchorPoint.X;
                double targetY = data.AnchorPoint.Y - BaseOffset; // 預設下方偏移
                int layer = 0;

                while (IsCollision(targetX, targetY, TagWidthEst, TagHeightEst, placedBoxes))
                {
                    layer++;
                    double direction = (layer % 2 == 1) ? -1.0 : 1.0;
                    targetY = data.AnchorPoint.Y + direction * (BaseOffset + (layer / 2) * LayerSpacing);
                }

                data.CalculatedHeadPos = new XYZ(targetX, targetY, data.AnchorPoint.Z);
                placedBoxes.Add(new BoundingBox2D(targetX, targetY, TagWidthEst, TagHeightEst));
            }
        }

        /// <summary>
        /// 處理縱向牆/管道間（標籤向左側垂直排隊，引線先水平再垂直，如圖 4 所示）
        /// </summary>
        private void ArrangeVerticalGroup(List<TagData> list)
        {
            // 按 Y 座標排序，同一豎向管道間/牆面的套管標籤統一由下至上排隊
            var sorted = list.OrderBy(t => t.AnchorPoint.Y).ToList();
            List<BoundingBox2D> placedBoxes = new List<BoundingBox2D>();

            foreach (var data in sorted)
            {
                // 圖 4 需求：縱向牆體開口標籤統一放置於左側 (X 軸減去 BaseOffset，拉長距離)
                double targetX = data.AnchorPoint.X - BaseOffset;
                double targetY = data.AnchorPoint.Y;
                int layer = 0;

                // 若同一垂直方向有多個標籤，沿 Y 軸避讓推移，確保不重疊
                while (IsCollision(targetX, targetY, TagWidthEst, TagHeightEst, placedBoxes))
                {
                    layer++;
                    targetY = data.AnchorPoint.Y + layer * (TagHeightEst + 0.3);
                }

                data.CalculatedHeadPos = new XYZ(targetX, targetY, data.AnchorPoint.Z);
                placedBoxes.Add(new BoundingBox2D(targetX, targetY, TagWidthEst, TagHeightEst));
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

                // 2. 距離足夠時開啟引線並計算 90 度轉折
                double dist = data.AnchorPoint.DistanceTo(data.CalculatedHeadPos);
                if (dist > 0.4)
                {
                    data.Tag.HasLeader = true;
                    data.Tag.LeaderEndCondition = LeaderEndCondition.Free;

                    XYZ anchor = data.AnchorPoint;
                    XYZ head = data.CalculatedHeadPos;
                    XYZ elbow;

                    if (data.Orientation == HostOrientation.Horizontal)
                    {
                        // 橫向牆：先垂直出牆至 Head.Y，再水平拉至標籤 (Anchor.X, Head.Y)
                        elbow = new XYZ(anchor.X, head.Y, anchor.Z);
                    }
                    else
                    {
                        // 縱向牆（圖 4 紅框需求）：先水平向左出牆至 Head.X，再垂直拉至標籤 (Head.X, Anchor.Y)
                        elbow = new XYZ(head.X, anchor.Y, anchor.Z);
                    }

                    data.Tag.SetLeaderEnd(data.TargetReference, anchor);
                    data.Tag.SetLeaderElbow(data.TargetReference, elbow);
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

        private bool IsCollision(double x, double y, double w, double h, List<BoundingBox2D> boxes)
        {
            BoundingBox2D target = new BoundingBox2D(x, y, w, h);
            return boxes.Any(b => b.Intersects(target));
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
            public XYZ CalculatedHeadPos { get; set; }
            public HostOrientation Orientation { get; set; }
            public XYZ HostDirection { get; set; }
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