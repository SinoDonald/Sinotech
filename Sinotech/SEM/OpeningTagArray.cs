using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Sinotech.SEM
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

            List<IndependentTag> allTags = new FilteredElementCollector(doc, activeView.Id)
                .WherePasses(tagFilter)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .Where(t => !t.IsOrphaned)
                .ToList();

            if (allTags.Count == 0)
            {
                TaskDialog.Show("提示", "當前視圖未發現開口套管標籤。");
                return Result.Succeeded;
            }

            // 已開啟引線代表使用者已完成放置，只作為避碰障礙，不再重新演算或移動。
            List<IndependentTag> tags = allTags.Where(t => !t.HasLeader).ToList();
            List<IndependentTag> fixedTags = allTags.Where(t => t.HasLeader).ToList();
            if (tags.Count == 0)
            {
                TaskDialog.Show("提示", $"目前 {fixedTags.Count} 個開口套管標籤皆已開啟引線，未重新排列。");
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
                int processedCount = engine.ArrangeTags(tags, fixedTags, hostElements);

                trans.Commit();
                TaskDialog.Show("成功",
                    $"已完成 {processedCount} 個未開啟引線標籤的排版；保留 {fixedTags.Count} 個已完成標籤不變。");
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
        /// <param name="fixedTags">已開啟引線、不得移動的標籤</param>
        /// <param name="hostElements">宿主牆樑元件列表</param>
        /// <returns>處理數量</returns>
        public int ArrangeTags(List<IndependentTag> tags, List<IndependentTag> fixedTags,
            List<Element> hostElements)
        {
            List<TagData> tagDataList = new List<TagData>();
            var fixedTagBoxes = new Dictionary<ElementId, BoundingBoxXYZ>();

            using (SubTransaction measurement = new SubTransaction(_doc))
            {
                measurement.Start();
                // 關閉引線以取得準確的標籤邊界框
                foreach (var tag in tags.Concat(fixedTags))
                {
                    try { tag.HasLeader = false; } catch { }
                }
                _doc.Regenerate();

                foreach (var fixedTag in fixedTags)
                    fixedTagBoxes[fixedTag.Id] = fixedTag.get_BoundingBox(_view);

                // A. 解析標籤與宿主幾何關係
                foreach (var tag in tags)
                {
                    var references = tag.GetTaggedReferences();
                    if (references.Count == 0) continue;
                    Reference refElem = references.FirstOrDefault();
                    if (refElem != null && refElem.LinkedElementId != ElementId.InvalidElementId) continue;
                    if (refElem == null) continue;

                    Element targetElem = _doc.GetElement(refElem.ElementId);
                    if (targetElem == null) continue;

                    XYZ anchorPt = GetElementCenter(targetElem);
                    TargetOrientation orientation = DetectTargetOrientation(targetElem, anchorPt, hostElements, out XYZ hostDir);

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
                        TextOffset = tagBox == null ? XYZ.Zero
                            : (tagBox.Min + tagBox.Max) * 0.5 - tag.TagHeadPosition,
                        TagWidth = tWidth,
                        TagHeight = tHeight,
                        Orientation = orientation,
                        HostDirection = hostDir
                    });
                }

                measurement.RollBack();
            }

            var obstacles = new FilteredElementCollector(_doc, _view.Id)
                .WherePasses(new ElementMulticategoryFilter(new[] {
                    BuiltInCategory.OST_PipeAccessory, BuiltInCategory.OST_DuctAccessory,
                    BuiltInCategory.OST_CableTrayFitting }))
                .WhereElementIsNotElementType()
                .Select(e => new { e.Id, Box = e.get_BoundingBox(_view) })
                .Where(e => e.Box != null).ToDictionary(e => e.Id, e => ToBox(e.Box));

            // 已完成標籤及無法參與排版的標籤都是固定障礙；可處理標籤的舊位置不保留。
            var processableIds = new HashSet<ElementId>(tagDataList.Select(t => t.Tag.Id));
            var reserved = new Dictionary<ElementId, BoundingBoxXYZ>(fixedTagBoxes);
            foreach (var tag in tags.Where(t => !processableIds.Contains(t.Id)))
                reserved[tag.Id] = tag.get_BoundingBox(_view);
            int processedCount = 0;
            var arrangedTagIds = new HashSet<ElementId>();
            // B. 對所有標籤進行全局優先順序排版
            var sortedTags = tagDataList.OrderBy(t => t.AnchorPoint.X).ThenBy(t => t.AnchorPoint.Y).ToList();
            List<PlacedTagFootprint> placed = new List<PlacedTagFootprint>();

            foreach (var data in sortedTags)
            {
                PlacedTagFootprint bestCandidate = null;

                // 1. 第一優先：垂直於牆 LocationCurve 直出；水平牆先上後下，垂直牆先左後右。
                foreach (var candidate in GenerateStraightCandidates(data, 0, 0))
                {
                    if (!IsCollision(candidate, placed, obstacles, reserved, data))
                    {
                        bestCandidate = candidate;
                        break;
                    }
                }

                // 2. 第二優先：兩側直出皆碰撞時，先在近距離嘗試 90° 轉折。
                if (bestCandidate == null)
                {
                    foreach (var candidate in GenerateFallbackSlidingCandidates(data, 0, 4))
                    {
                        if (!IsCollision(candidate, placed, obstacles, reserved, data))
                        {
                            bestCandidate = candidate;
                            break;
                        }
                    }
                }

                // 3. 第三優先：近距離轉折仍無法避讓，才逐層延長兩側直線。
                if (bestCandidate == null)
                {
                    foreach (var candidate in GenerateStraightCandidates(data, 1, 6))
                    {
                        if (!IsCollision(candidate, placed, obstacles, reserved, data))
                        {
                            bestCandidate = candidate;
                            break;
                        }
                    }
                }

                // 4. 密集區擴大搜尋轉折位置，再搜尋較外層直線；所有保底候選仍須通過避碰。
                if (bestCandidate == null)
                {
                    foreach (var candidate in GenerateFallbackSlidingCandidates(data, 5, 16)
                        .Concat(GenerateStraightCandidates(data, 7, 30)))
                    {
                        if (!IsCollision(candidate, placed, obstacles, reserved, data))
                        {
                            bestCandidate = candidate;
                            break;
                        }
                    }
                }

                if (bestCandidate != null)
                {
                    data.CalculatedHeadPos = bestCandidate.Head;
                    data.CalculatedAnchor = bestCandidate.FinalAnchor;
                    data.CalculatedElbow = bestCandidate.FinalElbow;
                    data.HasElbow = bestCandidate.HasElbow;
                    bool applied = ApplyTagPositionAndElbow(data);
                    if (!applied && data.HasElbow)
                    {
                        // 自由端點／折線不被此標籤型式接受時，改找無碰撞的貼附端點直線。
                        bestCandidate = GenerateStraightCandidates(data, 1, 30)
                            .FirstOrDefault(candidate => !IsCollision(candidate, placed, obstacles, reserved, data));
                        if (bestCandidate != null)
                        {
                            data.CalculatedHeadPos = bestCandidate.Head;
                            data.CalculatedAnchor = bestCandidate.FinalAnchor;
                            data.CalculatedElbow = bestCandidate.FinalElbow;
                            data.HasElbow = false;
                            applied = ApplyTagPositionAndElbow(data);
                        }
                    }
                    if (applied)
                    {
                        placed.Add(bestCandidate);
                        reserved.Remove(data.Tag.Id);
                        arrangedTagIds.Add(data.Tag.Id);
                        processedCount++;
                    }
                }
            }

            // 無法解析或寫回失敗的標籤也必須離開原開口，並開啟貼附端點引線。
            int fallbackIndex = 0;
            foreach (var tag in tags.Where(t => !arrangedTagIds.Contains(t.Id)))
            {
                using (SubTransaction fallback = new SubTransaction(_doc))
                {
                    fallback.Start();
                    try
                    {
                        double direction = fallbackIndex % 2 == 0 ? -1.0 : 1.0;
                        tag.HasLeader = false;
                        tag.TagHeadPosition += XYZ.BasisX * (direction * BaseOffset);
                        tag.HasLeader = true;
                        tag.LeaderEndCondition = LeaderEndCondition.Attached;
                        fallback.Commit();
                        processedCount++;
                        fallbackIndex++;
                    }
                    catch
                    {
                        fallback.RollBack();
                        try { tag.HasLeader = true; } catch { }
                    }
                }
            }

            return processedCount;
        }

        /// <summary>
        /// 以牆 LocationCurve／開口長軸為基準，標籤沿其垂直方向放置。
        /// 基準軸水平：上、下；基準軸垂直：左、右。延長與轉折均共用此順序。
        /// </summary>
        private static IEnumerable<XYZ> GetPerpendicularSides(TargetOrientation orientation)
        {
            if (orientation == TargetOrientation.Horizontal)
            {
                yield return XYZ.BasisY;          // 上
                yield return -XYZ.BasisY;         // 下
            }
            else
            {
                yield return -XYZ.BasisX;         // 左
                yield return XYZ.BasisX;          // 右
            }
        }

        private static XYZ GetSideAnchor(TagData data, XYZ side)
        {
            if (data.ElementBBox == null) return data.AnchorPoint;
            var box = data.ElementBBox;
            double cx = (box.Min.X + box.Max.X) / 2.0;
            double cy = (box.Min.Y + box.Max.Y) / 2.0;
            return new XYZ(side.X < 0 ? box.Min.X : side.X > 0 ? box.Max.X : cx,
                side.Y < 0 ? box.Min.Y : side.Y > 0 ? box.Max.Y : cy, data.AnchorPoint.Z);
        }

        /// <summary>
        /// 每一距離層只嘗試與元件垂直的兩側，再延長到下一層。
        /// </summary>
        private IEnumerable<PlacedTagFootprint> GenerateStraightCandidates(TagData data, int firstLayer, int lastLayer)
        {
            for (int layer = firstLayer; layer <= lastLayer; layer++)
            {
                double offset = BaseOffset + layer * LayerSpacing;
                foreach (XYZ side in GetPerpendicularSides(data.Orientation))
                {
                    XYZ anchor = GetSideAnchor(data, side);
                    yield return GetStraightFootprint(data, anchor, anchor + side * offset);
                }
            }
        }

        /// <summary>
        /// 第三優先（降級）：當正交直線完全碰撞時，降級啟用滑動與 90° 正交轉折 Elbow
        /// </summary>
        private IEnumerable<PlacedTagFootprint> GenerateFallbackSlidingCandidates(
            TagData data, int firstLayer, int lastLayer)
        {
            double cx = data.AnchorPoint.X;
            double cy = data.AnchorPoint.Y;
            if (data.ElementBBox != null)
            {
                cx = (data.ElementBBox.Min.X + data.ElementBBox.Max.X) / 2.0;
                cy = (data.ElementBBox.Min.Y + data.ElementBBox.Max.Y) / 2.0;
            }

            for (int layer = firstLayer; layer <= lastLayer; layer++)
            {
                double gapW = data.TagWidth + 0.8;
                double gapH = data.TagHeight + 0.8;
                double layerOffsetW = (layer % 2 == 1) ? (gapW / 2.0) : 0.0;
                double layerOffsetH = (layer % 2 == 1) ? (gapH / 2.0) : 0.0;

                for (int shiftIndex = 1; shiftIndex <= 12; shiftIndex++)
                {
                    double shiftDir = (shiftIndex % 2 == 1) ? 1.0 : -1.0;

                    foreach (XYZ side in GetPerpendicularSides(data.Orientation))
                    {
                        XYZ anchor = GetSideAnchor(data, side);
                        double offset = BaseOffset + layer * LayerSpacing;
                        if (side.Y != 0)
                        {
                            double targetX = cx + layerOffsetW + shiftDir * ((shiftIndex + 1) / 2) * gapW;
                            yield return GetElbowFootprint(data, targetX, anchor.Y + side.Y * offset, true);
                        }
                        else
                        {
                            double targetY = cy + layerOffsetH + shiftDir * ((shiftIndex + 1) / 2) * gapH;
                            yield return GetElbowFootprint(data, anchor.X + side.X * offset, targetY, false);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 將位置套用至標籤，並構建精確的 90 度正交 Elbow 或直出貼附端點
        /// </summary>
        private bool ApplyTagPositionAndElbow(TagData data)
        {
            using (SubTransaction change = new SubTransaction(_doc))
            {
                change.Start();
                try
                {
                    data.Tag.HasLeader = false;
                    data.Tag.TagHeadPosition = data.CalculatedHeadPos;
                    data.Tag.HasLeader = true;
                    data.Tag.LeaderEndCondition = data.HasElbow
                        ? LeaderEndCondition.Free : LeaderEndCondition.Attached;
                    if (data.HasElbow)
                    {
                        data.Tag.SetLeaderEnd(data.TargetReference, data.CalculatedAnchor);
                        data.Tag.SetLeaderElbow(data.TargetReference, data.CalculatedElbow);
                    }
                    _doc.Regenerate();
                    return change.Commit() == TransactionStatus.Committed;
                }
                catch
                {
                    change.RollBack();
                    return false;
                }
            }
        }

        /// <summary>
        /// 判斷牆 LocationCurve／開口長軸方向；標籤放置方向另取其垂直軸。
        /// </summary>
        private TargetOrientation DetectTargetOrientation(Element targetElem, XYZ anchorPt, List<Element> hostElements, out XYZ hostDir)
        {
            hostDir = XYZ.BasisX;

            var family = targetElem as FamilyInstance;
            // 有直接宿主牆時，以牆的 LocationCurve 為最高優先基準。
            if (family?.Host is Wall wall && wall.Location is LocationCurve wallLocation)
            {
                XYZ tangent = wallLocation.Curve.ComputeDerivatives(0.5, true).BasisX;
                hostDir = tangent;
                return Math.Abs(hostDir.X) > Math.Abs(hostDir.Y)
                    ? TargetOrientation.Horizontal : TargetOrientation.Vertical;
            }

            // 沒有直接宿主牆時，以 MEP Connector 的穿牆／管道方向推回牆軸。
            // GetPerpendicularSides 會再取牆軸的垂直方向，因此最後標籤會與管道同向。
            var connectors = family?.MEPModel?.ConnectorManager?.Connectors;
            if (connectors != null)
            {
                foreach (Connector connector in connectors)
                {
                    if (connector.ConnectorType != ConnectorType.End) continue;
                    XYZ pipeDirection = connector.CoordinateSystem.BasisZ;
                    if (pipeDirection.X * pipeDirection.X + pipeDirection.Y * pipeDirection.Y < 0.25) continue;
                    hostDir = new XYZ(-pipeDirection.Y, pipeDirection.X, 0);
                    return Math.Abs(hostDir.X) > Math.Abs(hostDir.Y)
                        ? TargetOrientation.Horizontal : TargetOrientation.Vertical;
                }
            }

            // 無 MEP Connector 時，FacingOrientation 作為穿牆方向備援；不受族尺寸影響。
            if (family != null)
            {
                XYZ facing = family.FacingOrientation;
                if (facing.X * facing.X + facing.Y * facing.Y > 0.25)
                {
                    hostDir = new XYZ(-facing.Y, facing.X, 0);
                    return Math.Abs(hostDir.X) > Math.Abs(hostDir.Y)
                        ? TargetOrientation.Horizontal : TargetOrientation.Vertical;
                }
            }

            // 無直接宿主時，優先尋找最近牆的 LocationCurve；結構樑僅作次要備援。
            TargetOrientation orientation;
            if (TryGetClosestCurveOrientation(hostElements.Where(h => h is Wall), anchorPt,
                out orientation, out hostDir)) return orientation;
            if (TryGetClosestCurveOrientation(hostElements.Where(h => !(h is Wall)), anchorPt,
                out orientation, out hostDir)) return orientation;

            // 找不到牆／樑時，才以開口或套管的長軸作為備援基準。
            BoundingBoxXYZ bbox = targetElem.get_BoundingBox(_view);
            if (bbox != null)
            {
                double dx = Math.Abs(bbox.Max.X - bbox.Min.X);
                double dy = Math.Abs(bbox.Max.Y - bbox.Min.Y);
                if (dy > dx * 1.3)
                {
                    hostDir = XYZ.BasisY;
                    return TargetOrientation.Vertical;
                }
                if (dx > dy * 1.3)
                {
                    hostDir = XYZ.BasisX;
                    return TargetOrientation.Horizontal;
                }
            }

            return orientation;
        }

        private static bool TryGetClosestCurveOrientation(IEnumerable<Element> elements, XYZ point,
            out TargetOrientation orientation, out XYZ direction)
        {
            double minDist = double.MaxValue;
            direction = XYZ.BasisX;
            orientation = TargetOrientation.Horizontal;
            foreach (var element in elements)
            {
                if (!(element.Location is LocationCurve location)) continue;
                Curve curve = location.Curve;
                var projection = curve.Project(point);
                if (projection == null) continue;
                double distance = point.DistanceTo(projection.XYZPoint);
                if (distance >= minDist || distance >= 10.0) continue;
                minDist = distance;
                direction = (curve.GetEndPoint(1) - curve.GetEndPoint(0)).Normalize();
                orientation = Math.Abs(direction.X) > Math.Abs(direction.Y)
                    ? TargetOrientation.Horizontal : TargetOrientation.Vertical;
            }
            return minDist < double.MaxValue;
        }

        private PlacedTagFootprint GetStraightFootprint(TagData data, XYZ anchor, XYZ head)
        {
            PlacedTagFootprint fp = new PlacedTagFootprint();
            fp.SetTextBox(head.X + data.TextOffset.X, head.Y + data.TextOffset.Y, data.TagWidth, data.TagHeight);
            fp.AddLine(anchor.X, anchor.Y, head.X, head.Y, 0.4);
            fp.FinalAnchor = anchor;
            fp.FinalElbow = anchor;
            fp.Head = head;
            fp.HasElbow = false;
            return fp;
        }

        private PlacedTagFootprint GetElbowFootprint(TagData data, double targetX, double targetY, bool isHorizontal)
        {
            PlacedTagFootprint fp = new PlacedTagFootprint();
            fp.SetTextBox(targetX + data.TextOffset.X, targetY + data.TextOffset.Y, data.TagWidth, data.TagHeight);

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
            fp.HasElbow = true;
            return fp;
        }

        private static BoundingBox2D ToBox(BoundingBoxXYZ box)
        {
            return new BoundingBox2D((box.Min.X + box.Max.X) / 2,
                (box.Min.Y + box.Max.Y) / 2, box.Max.X - box.Min.X, box.Max.Y - box.Min.Y);
        }

        private bool IsCollision(PlacedTagFootprint candidate, List<PlacedTagFootprint> placed,
            Dictionary<ElementId, BoundingBox2D> obstacles,
            Dictionary<ElementId, BoundingBoxXYZ> reserved, TagData data)
        {
            if (placed.Any(p => p.Intersects(candidate))) return true;
            foreach (var obstacle in obstacles)
            {
                if (candidate.TextBox.Intersects(obstacle.Value)) return true;
                // 引線只允許接觸自己的開口／套管。
                if (obstacle.Key != data.TargetReference.ElementId &&
                    candidate.LineBoxes.Any(line => line.Intersects(obstacle.Value))) return true;
            }
            foreach (var entry in reserved)
            {
                if (entry.Key == data.Tag.Id || entry.Value == null) continue;
                var box = ToBox(entry.Value);
                if (candidate.TextBox.Intersects(box) ||
                    candidate.LineBoxes.Any(line => line.Intersects(box))) return true;
            }
            return false;
        }

        private XYZ GetElementCenter(Element elem)
        {
            if (elem.Location is LocationPoint lp) return lp.Point;
            BoundingBoxXYZ bbox = elem.get_BoundingBox(_view);
            if (bbox != null) return (bbox.Min + bbox.Max) * 0.5;
            return XYZ.Zero;
        }

        private enum TargetOrientation { Horizontal, Vertical }

        private class TagData
        {
            public IndependentTag Tag { get; set; }
            public Reference TargetReference { get; set; }
            public XYZ AnchorPoint { get; set; }
            public BoundingBoxXYZ ElementBBox { get; set; }
            public XYZ TextOffset { get; set; }
            public double TagWidth { get; set; }
            public double TagHeight { get; set; }
            public XYZ CalculatedHeadPos { get; set; }
            public XYZ CalculatedAnchor { get; set; }
            public XYZ CalculatedElbow { get; set; }
            public bool HasElbow { get; set; }
            public TargetOrientation Orientation { get; set; }
            public XYZ HostDirection { get; set; }
        }

        private class PlacedTagFootprint
        {
            public BoundingBox2D TextBox { get; set; }
            public List<BoundingBox2D> LineBoxes { get; } = new List<BoundingBox2D>();
            public XYZ FinalAnchor { get; set; }
            public XYZ FinalElbow { get; set; }
            public XYZ Head { get; set; }
            public bool HasElbow { get; set; }

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

                foreach (var line in LineBoxes)
                    if (other.LineBoxes.Any(otherLine => line.Intersects(otherLine))) return true;
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
