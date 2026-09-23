using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using Sinotech.Common;

namespace Sinotech.CSD
{
    [Transaction(TransactionMode.Manual)]
    public class TagArray : IExternalCommand
    {
        private const string RegionLineStyleName = "CSD_標籤自動空白區";
        private const string TagArrayVersion = "TagArray-20260922-R13";
        private const int MaximumRowsPerRegion = 15;
        private const double AnnotationBoundsInsetPaperMm = 0.5;
        private const double RowSpacingPaperMm = 0.2;
        private const double PlacementTolerancePaperMm = 0.05;
        private const double HorizontalLeaderTolerancePaperMm = 0.2;
        private const double Epsilon = 1e-7;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            AutoNumberForm typeForm = new AutoNumberForm(doc);
            typeForm.ShowDialog();
            if (typeForm.trueOrFalse != true) return Result.Cancelled;

            List<ViewPlan> availableViews = AutoPipeTag.GetAutoNumberViewPlans(doc, typeForm.viewFamilyTypeName);
            ChooseMultiViewPlansForm chooseForm = new ChooseMultiViewPlansForm(doc, availableViews,
                ChooseMultiViewPlansForm.FormMode.TagArray);
            if (chooseForm.ShowDialog() != DialogResult.OK) return Result.Cancelled;
            List<ViewPlan> selectedViews = chooseForm.checkViewPlans;
            if (selectedViews == null || selectedViews.Count == 0) return Result.Failed;

            List<IndependentTag> targetTags = selectedViews.SelectMany(view => CollectTargetTags(doc, view))
                .GroupBy(tag => tag.Id.Value).Select(group => group.First()).ToList();
            List<string> familyWarnings = DisableRotateWithComponent(doc, targetTags);
            Dictionary<ElementId, List<PickedBox>> manualBoxes = chooseForm.IsAutoResult
                ? new Dictionary<ElementId, List<PickedBox>>() : PickManualRegions(uiDoc, selectedViews);

            int totalTags = 0;
            int totalMoved = 0;
            List<string> unmovedTags = new List<string>();
            List<string> processingMessages = new List<string>();
            List<string> viewResults = new List<string>();
            foreach (IGrouping<ElementId, ViewPlan> viewGroup in selectedViews.GroupBy(view =>
            {
                ElementId primaryId = view.GetPrimaryViewId();
                return primaryId != null && primaryId != ElementId.InvalidElementId ? primaryId : view.Id;
            }))
            {
                ViewPlan operationView = doc.GetElement(viewGroup.Key) as ViewPlan;
                if (operationView != null)
                {
                    try
                    {
                        uiDoc.RequestViewChange(operationView);
                        System.Windows.Forms.Application.DoEvents();
                    }
                    catch { }
                }

                using (Transaction transaction = new Transaction(doc, "標籤空白區域排序"))
                {
                    transaction.Start();
                    foreach (ViewPlan view in viewGroup)
                    {
                    ClearOldRegionLines(doc, view);
                    List<IndependentTag> tags = CollectTargetTags(doc, view);
                    totalTags += tags.Count;
                    foreach (IndependentTag tag in tags)
                    {
                        try
                        {
                            tag.TagOrientation = TagOrientation.Horizontal;
                            if (tag.TagOrientation == TagOrientation.AnyModelDirection) tag.RotationAngle = 0;
                        }
                        catch { }
                    }
                    doc.Regenerate();

                    ViewFrame frame = new ViewFrame(view);
                    List<TagSize> sizes = tags.Select(tag => MeasureTag(tag, view, frame))
                        .Where(size => size != null).ToList();
                    if (sizes.Count == 0)
                    {
                        viewResults.Add($"{view.Name}：無引線標籤 {tags.Count} 個，成功移動 0 個");
                        if (tags.Count > 0)
                            processingMessages.Add($"視圖：{view.Name} | 無法量測無引線管道／電纜架標籤範圍");
                        continue;
                    }
                    double cellWidth = sizes.Max(size => size.Width);
                    double cellHeight = sizes.Max(size => size.Height) + RowSpacingPaperMm * view.Scale / 304.8;
                    int verticalMergeCount = 0;
                    List<Rect2D> regions;
                    List<Rect2D> displayRegions;
                    List<Rect2D> placementSlots;
                    if (chooseForm.IsAutoResult)
                    {
                        Rect2D cropBounds = frame.Project(view.CropBox);
                        List<List<UV2>> cropLoops = GetCropLoops(view, frame);
                        double annotationInset = AnnotationBoundsInsetPaperMm * view.Scale / 304.8;
                        ObstacleSet obstacles = CollectVisibleObstacles(doc, view, frame, annotationInset);
                        regions = FindAutomaticRegions(cropBounds, cropLoops, obstacles, cellWidth, cellHeight,
                            out verticalMergeCount, out placementSlots);
                        // 顯示框直接由實際可放置格位組成，避免標籤使用到畫面上沒有框出的格位。
                        displayRegions = MergeVerticalDisplayRegions(placementSlots, cellWidth, cellHeight,
                            out int displayMergeCount);
                        verticalMergeCount += displayMergeCount;
                    }
                    else
                    {
                        manualBoxes.TryGetValue(view.Id, out List<PickedBox> boxes);
                        regions = CreateManualRegions(frame, boxes, cellWidth, cellHeight);
                        placementSlots = CreateManualSlots(frame, boxes, cellWidth, cellHeight);
                        displayRegions = regions;
                    }

                    int moved = ArrangeTagsInSlots(doc, view, frame, tags, displayRegions, placementSlots,
                        cellWidth, cellHeight, !chooseForm.IsAutoResult,
                        out List<string> viewUnmoved);
                    totalMoved += moved;
                    unmovedTags.AddRange(viewUnmoved);
                    viewResults.Add($"{view.Name}：無引線標籤 {tags.Count} 個，成功移動 {moved} 個");
                    }
                    transaction.Commit();
                }
            }

            processingMessages.AddRange(familyWarnings.Distinct()
                .Select(warning => $"無法關閉「隨元件旋轉」：{warning}"));
            WriteDiagnosticReport(doc, unmovedTags, processingMessages);
            TaskDialog.Show("標籤空白區域排序", $"處理完成。\n" +
                $"無引線標籤：{totalTags} 個\n成功移動：{totalMoved} 個\n\n" +
                string.Join("\n", viewResults));
            return Result.Succeeded;
        }

        private static List<IndependentTag> CollectTargetTags(Document doc, ViewPlan view)
        {
            return new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>().Where(tag => tag.Category != null && !tag.HasLeader && !tag.IsOrphaned &&
                    (tag.Category.BuiltInCategory == BuiltInCategory.OST_PipeTags ||
                     tag.Category.BuiltInCategory == BuiltInCategory.OST_CableTrayTags)).ToList();
        }

        private static List<string> DisableRotateWithComponent(Document projectDoc, IEnumerable<IndependentTag> tags)
        {
            List<string> warnings = new List<string>();
            List<Family> families = tags.Select(tag => projectDoc.GetElement(tag.GetTypeId()) as FamilySymbol)
                .Where(symbol => symbol != null).Select(symbol => symbol.Family)
                .GroupBy(family => family.Id.Value).Select(group => group.First()).ToList();
            foreach (Family family in families)
            {
                Document familyDoc = null;
                try
                {
                    if (!family.IsEditable) { warnings.Add($"{family.Name}（族不可編輯）"); continue; }
                    familyDoc = projectDoc.EditFamily(family);
                    Parameter parameter = familyDoc.OwnerFamily
                        .get_Parameter(BuiltInParameter.FAMILY_ROTATE_WITH_COMPONENT);
                    if (parameter == null) { warnings.Add($"{family.Name}（找不到族參數）"); continue; }
                    using (Transaction transaction = new Transaction(familyDoc, "關閉隨元件旋轉"))
                    {
                        transaction.Start();
                        if (parameter.IsReadOnly || !parameter.Set(0))
                            throw new InvalidOperationException("族參數無法寫入");
                        transaction.Commit();
                    }
                    familyDoc.LoadFamily(projectDoc, new OverwriteFamilyLoadOptions());
                }
                catch (Exception ex) { warnings.Add($"{family.Name}（{ex.Message}）"); }
                finally
                {
                    if (familyDoc != null && familyDoc.IsValidObject)
                        try { familyDoc.Close(false); } catch { }
                }
            }
            return warnings;
        }

        private static Dictionary<ElementId, List<PickedBox>> PickManualRegions(UIDocument uiDoc,
            IEnumerable<ViewPlan> views)
        {
            Dictionary<ElementId, List<PickedBox>> result = new Dictionary<ElementId, List<PickedBox>>();
            foreach (ViewPlan view in views)
            {
                if (uiDoc.ActiveView.Id != view.Id) uiDoc.ActiveView = view;
                List<PickedBox> boxes = new List<PickedBox>();
                TaskDialog.Show("手動空白區域", $"目前視圖：【{view.Name}】\n連續框選可放置標籤的區域，完成後按 ESC。");
                while (true)
                {
                    try { boxes.Add(uiDoc.Selection.PickBox(PickBoxStyle.Directional, "框選空白區域；完成請按 ESC")); }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { break; }
                }
                result[view.Id] = boxes;
            }
            return result;
        }

        private static TagSize MeasureTag(IndependentTag tag, Autodesk.Revit.DB.View view, ViewFrame frame)
        {
            try
            {
                BoundingBoxXYZ box = tag.get_BoundingBox(view);
                if (box == null) return null;
                Rect2D projected = frame.Project(box);
                return projected.Width > Epsilon && projected.Height > Epsilon
                    ? new TagSize(projected.Width, projected.Height) : null;
            }
            catch { return null; }
        }

        private static int ArrangeTagsInSlots(Document doc, ViewPlan view, ViewFrame frame,
            List<IndependentTag> tags, List<Rect2D> displayRegions, List<Rect2D> slots,
            double cellWidth, double cellHeight, bool useManualSlotOrder,
            out List<string> unmoved)
        {
            unmoved = new List<string>();
            List<TagPlacementData> data = new List<TagPlacementData>();
            foreach (IndependentTag tag in tags)
            {
                TagPlacementData item = CreateTagPlacementData(doc, view, frame, tag);
                if (item == null)
                    unmoved.Add(DescribeUnmoved(view, tag, "無法取得被標註元件或標籤範圍"));
                else
                    data.Add(item);
            }

            List<PlacementGroup> groups = BuildPlacementGroups(displayRegions, slots);
            if (!useManualSlotOrder) NormalizePlacementSlots(groups, cellWidth, cellHeight);
            AssignTagsToGroups(data.Where(item => item.IsPipe), groups);
            AssignTagsToGroups(data.Where(item => !item.IsPipe), groups);

            HashSet<ElementId> assignedIds = new HashSet<ElementId>(groups
                .SelectMany(group => group.AssignedTags).Select(item => item.Tag.Id));
            foreach (TagPlacementData item in data.Where(item => !assignedIds.Contains(item.Tag.Id)))
                unmoved.Add(DescribeUnmoved(view, item.Tag, "空白格位不足"));

            int moved = 0;
            double placementTolerance = PlacementTolerancePaperMm * view.Scale / 304.8;
            foreach (PlacementGroup group in groups)
            {
                List<Rect2D> availableSlots = useManualSlotOrder
                    ? group.Slots.OrderBy(slot => slot.MinU)
                        .ThenByDescending(slot => (slot.MinV + slot.MaxV) / 2.0).ToList()
                    : group.Slots.OrderByDescending(slot => (slot.MinV + slot.MaxV) / 2.0)
                        .ThenBy(slot => slot.MinU).ToList();
                List<TagPlacementData> orderedTags = group.AssignedTags.Where(item => item.IsPipe)
                    .OrderByDescending(item => item.AnchorV)
                    .Concat(group.AssignedTags.Where(item => !item.IsPipe)
                        .OrderByDescending(item => item.AnchorV)).ToList();

                foreach (TagPlacementData item in orderedTags)
                {
                    bool applied = false;
                    string failureReason = availableSlots.Count == 0 ? "沒有剩餘空白格位" : "無法套用任何空白格位";
                    for (int slotIndex = 0; slotIndex < availableSlots.Count && !applied; slotIndex++)
                    {
                        Rect2D slot = availableSlots[slotIndex];
                        applied = ApplyTagPlacement(doc, view, frame, item, slot, placementTolerance,
                            out failureReason);
                        if (applied) availableSlots.RemoveAt(slotIndex);
                    }
                    if (applied) moved++;
                    else unmoved.Add(DescribeUnmoved(view, item.Tag,
                        $"無法設定標籤位置或引線（{failureReason}）"));
                }
            }
            return moved;
        }

        private static TagPlacementData CreateTagPlacementData(Document doc, ViewPlan view,
            ViewFrame frame, IndependentTag tag)
        {
            try
            {
                Reference targetReference = tag.GetTaggedReferences().FirstOrDefault();
                if (targetReference == null) return null;
                // CSD 管道／電纜架標籤建立時，無引線標籤的 Head 就位於目前視圖內
                // 可見管段的長度中心；保留這個原始點作為最近區域與引線端點基準。
                XYZ anchor = tag.TagHeadPosition;
                BoundingBoxXYZ box = tag.get_BoundingBox(view);
                if (anchor == null || box == null) return null;

                Rect2D body = frame.Project(box);
                UV2 head = frame.Project(tag.TagHeadPosition);
                UV2 anchorUv = frame.Project(anchor);
                return new TagPlacementData
                {
                    Tag = tag,
                    TargetReference = targetReference,
                    AnchorPoint = anchor,
                    AnchorU = anchorUv.U,
                    AnchorV = anchorUv.V,
                    LeftOffsetFromHead = body.MinU - head.U,
                    CenterVOffsetFromHead = (body.MinV + body.MaxV) / 2.0 - head.V,
                    BodyWidth = body.Width,
                    BodyHeight = body.Height,
                    IsPipe = tag.Category.BuiltInCategory == BuiltInCategory.OST_PipeTags
                };
            }
            catch { return null; }
        }

        private static List<PlacementGroup> BuildPlacementGroups(List<Rect2D> displayRegions,
            List<Rect2D> slots)
        {
            List<PlacementGroup> groups = displayRegions.Select(region => new PlacementGroup(region)).ToList();
            foreach (Rect2D slot in slots)
            {
                UV2 center = new UV2((slot.MinU + slot.MaxU) / 2.0, (slot.MinV + slot.MaxV) / 2.0);
                PlacementGroup group = groups.Where(item => item.DisplayBounds.Contains(center))
                    .OrderBy(item => item.DisplayBounds.Width * item.DisplayBounds.Height).FirstOrDefault();
                if (group == null && groups.Count > 0)
                    group = groups.OrderBy(item => DistanceToRect(center, item.DisplayBounds)).First();
                group?.Slots.Add(slot);
            }
            return groups.Where(group => group.Slots.Count > 0).ToList();
        }

        private static void NormalizePlacementSlots(List<PlacementGroup> groups,
            double cellWidth, double cellHeight)
        {
            foreach (PlacementGroup group in groups)
            {
                int detectedCapacity = Math.Min(MaximumRowsPerRegion, group.Slots.Count);
                int geometricCapacity = Math.Max(0,
                    (int)Math.Floor((group.DisplayBounds.Height + Epsilon) / cellHeight));
                int capacity = Math.Min(detectedCapacity, geometricCapacity);
                group.Slots.Clear();

                // 偵測格位只決定這個外框可容納多少標籤；實際排列則從外框頂端
                // 連續向下配置，避免障礙物切割時留下視覺上的空列。
                for (int row = 0; row < capacity; row++)
                {
                    double maxV = group.DisplayBounds.MaxV - row * cellHeight;
                    double minV = maxV - cellHeight;
                    group.Slots.Add(new Rect2D(group.DisplayBounds.MinU,
                        group.DisplayBounds.MinU + cellWidth, minV, maxV));
                }
            }
        }

        private static void AssignTagsToGroups(IEnumerable<TagPlacementData> source,
            List<PlacementGroup> groups)
        {
            List<TagPlacementData> remaining = source.ToList();
            while (remaining.Count > 0)
            {
                TagPlacementData bestTag = null;
                PlacementGroup bestGroup = null;
                double bestDistance = double.MaxValue;
                foreach (TagPlacementData item in remaining)
                    foreach (PlacementGroup group in groups.Where(candidate =>
                        candidate.AssignedTags.Count < candidate.Slots.Count))
                    {
                        double distance = group.Slots.Min(slot => DistanceToRect(
                            new UV2(item.AnchorU, item.AnchorV), slot));
                        if (distance >= bestDistance) continue;
                        bestDistance = distance;
                        bestTag = item;
                        bestGroup = group;
                    }
                if (bestTag == null || bestGroup == null) break;
                bestGroup.AssignedTags.Add(bestTag);
                remaining.Remove(bestTag);
            }
        }

        private static double DistanceToRect(UV2 point, Rect2D rect)
        {
            double du = point.U < rect.MinU ? rect.MinU - point.U :
                point.U > rect.MaxU ? point.U - rect.MaxU : 0.0;
            double dv = point.V < rect.MinV ? rect.MinV - point.V :
                point.V > rect.MaxV ? point.V - rect.MaxV : 0.0;
            return Math.Sqrt(du * du + dv * dv);
        }

        private static bool ApplyTagPlacement(Document doc, ViewPlan view, ViewFrame frame,
            TagPlacementData data, Rect2D slot, double placementTolerance, out string failureReason)
        {
            failureReason = string.Empty;
            using (SubTransaction change = new SubTransaction(doc))
            {
                change.Start();
                try
                {
                    double desiredLeft = slot.MinU;
                    double desiredCenterV = (slot.MinV + slot.MaxV) / 2.0;
                    double desiredHeadU = desiredLeft - data.LeftOffsetFromHead;
                    double desiredHeadV = desiredCenterV - data.CenterVOffsetFromHead;

                    data.Tag.HasLeader = false;
                    data.Tag.TagHeadPosition = frame.PointAt(data.Tag.TagHeadPosition,
                        desiredHeadU, desiredHeadV);
                    doc.Regenerate();

                    // 移動至母視圖作業時，Revit 對子視圖中的標籤可能回傳 null 或包含
                    // 非文字內容的 BoundingBox。此處使用移動前已量得的文字框偏移量
                    // 定位；不要再以移動後 BoundingBox 否決並復原已完成的位置。

                    XYZ finalHeadPosition = data.Tag.TagHeadPosition;
                    UV2 expectedHead = frame.Project(finalHeadPosition);
                    data.Tag.HasLeader = true;
                    double horizontalTolerance = HorizontalLeaderTolerancePaperMm * view.Scale / 304.8;
                    bool isHorizontal = Math.Abs(data.AnchorV - desiredCenterV) <= horizontalTolerance;
                    if (isHorizontal)
                    {
                        data.Tag.LeaderEndCondition = LeaderEndCondition.Attached;
                        StabilizeTagHead(doc, data.Tag, finalHeadPosition);
                    }
                    else
                    {
                        try
                        {
                            data.Tag.LeaderEndCondition = LeaderEndCondition.Free;
                            data.Tag.TagHeadPosition = finalHeadPosition;
                            data.Tag.SetLeaderEnd(data.TargetReference, data.AnchorPoint);

                            // 標籤端至 Elbow 維持水平，Elbow 至被標註點維持垂直，
                            // 因此只會在標籤左側或右側形成一個 90 度轉折。
                            XYZ elbow = frame.PointAt(data.AnchorPoint, data.AnchorU, desiredCenterV);
                            data.Tag.SetLeaderElbow(data.TargetReference, elbow);
                            StabilizeTagHead(doc, data.Tag, finalHeadPosition);
                            data.Tag.SetLeaderEnd(data.TargetReference, data.AnchorPoint);
                            data.Tag.SetLeaderElbow(data.TargetReference, elbow);
                            doc.Regenerate();
                        }
                        catch
                        {
                            // 個別標籤族若不支援自由端點，保留 R10 的貼附引線，
                            // 不讓引線整形失敗連帶復原已正確完成的標籤位置。
                            data.Tag.LeaderEndCondition = LeaderEndCondition.Attached;
                            StabilizeTagHead(doc, data.Tag, finalHeadPosition);
                        }
                    }

                    UV2 actualHead = frame.Project(data.Tag.TagHeadPosition);
                    if (Distance(actualHead, expectedHead) > placementTolerance)
                        throw new InvalidOperationException("開啟引線後標籤位置被 Revit 改動");
                    TransactionStatus status = change.Commit();
                    if (status == TransactionStatus.Committed) return true;
                    failureReason = $"子交易未提交（{status}）";
                    return false;
                }
                catch (Exception ex)
                {
                    failureReason = ex.Message;
                    change.RollBack();
                    return false;
                }
            }
        }

        private static void StabilizeTagHead(Document doc, IndependentTag tag, XYZ headPosition)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                tag.TagHeadPosition = headPosition;
                doc.Regenerate();
            }
        }

        private static string DescribeUnmoved(ViewPlan view, IndependentTag tag, string reason)
            => $"視圖：{view.Name} | 標籤 ID：{tag.Id.Value} | 類別：{tag.Category?.Name} | 原因：{reason}";

        private static string WriteDiagnosticReport(Document doc, List<string> unmoved,
            List<string> processingMessages)
        {
            if (unmoved.Count == 0 && processingMessages.Count == 0) return string.Empty;
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string projectName = string.IsNullOrWhiteSpace(doc.PathName)
                ? doc.Title : Path.GetFileNameWithoutExtension(doc.PathName);
            foreach (char invalid in Path.GetInvalidFileNameChars()) projectName = projectName.Replace(invalid, '_');
            string path = Path.Combine(desktop,
                $"{projectName}_標籤排序處理訊息_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            StringBuilder content = new StringBuilder();
            content.AppendLine($"版本：{TagArrayVersion}");
            if (unmoved.Count > 0)
            {
                content.AppendLine($"未移動標籤：{unmoved.Count} 個");
                content.AppendLine();
                foreach (string line in unmoved) content.AppendLine(line);
            }
            if (processingMessages.Count > 0)
            {
                if (unmoved.Count > 0) content.AppendLine();
                content.AppendLine("其他處理訊息：");
                foreach (string line in processingMessages) content.AppendLine(line);
            }
            File.WriteAllText(path, content.ToString(), Encoding.UTF8);
            return path;
        }

        private static ObstacleSet CollectVisibleObstacles(Document doc, ViewPlan view, ViewFrame frame,
            double annotationInset)
        {
            ObstacleSet result = new ObstacleSet();
            foreach (Element element in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                if (element is RevitLinkInstance || IsRegionLine(element)) continue;
                AddElementObstacle(result, element, view, frame, null, annotationInset);
            }
            foreach (RevitLinkInstance link in new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                Document linkDoc = link.GetLinkDocument();
                if (linkDoc == null) continue;
                Transform transform = link.GetTotalTransform();
                Autodesk.Revit.DB.View linkedView = null;
                try
                {
                    RevitLinkGraphicsSettings settings = view.GetLinkOverrides(link.Id)
                        ?? view.GetLinkOverrides(link.GetTypeId());
                    if (settings != null && settings.LinkedViewId != ElementId.InvalidElementId)
                        linkedView = linkDoc.GetElement(settings.LinkedViewId) as Autodesk.Revit.DB.View;
                }
                catch { }
                try
                {
                    foreach (Element element in new FilteredElementCollector(doc, view.Id, link.Id)
                        .WhereElementIsNotElementType())
                        AddElementObstacle(result, element, linkedView, frame, transform, annotationInset);
                }
                catch
                {
                    foreach (Element element in new FilteredElementCollector(linkDoc).WhereElementIsNotElementType())
                        AddElementObstacle(result, element, null, frame, transform, annotationInset);
                }
            }
            return result;
        }

        private static void AddElementObstacle(ObstacleSet result, Element element, Autodesk.Revit.DB.View view,
            ViewFrame frame, Transform transform, double annotationInset)
        {
            try
            {
                // 容器的 BoundingBox 會包住所有成員，不能當成實心障礙物。
                if (element is Group || element is AssemblyInstance) return;
                if (element is CurveElement curveElement)
                {
                    AddCurveObstacle(result, curveElement.GeometryCurve, frame, transform);
                    return;
                }
                if (element is ImportInstance)
                {
                    Options options = new Options { IncludeNonVisibleObjects = false };
                    if (view != null && view.Document.Equals(element.Document)) options.View = view;
                    AddGeometryObstacles(result, element.get_Geometry(options), frame, transform);
                    return;
                }

                bool isAnnotation = element.Category != null &&
                    element.Category.CategoryType == CategoryType.Annotation;
                if (isAnnotation || element is IndependentTag || element is TextNote ||
                    element is Dimension || element is FilledRegion || element is ImageInstance)
                {
                    BoundingBoxXYZ box = element.get_BoundingBox(view);
                    if (box == null) return;
                    Rect2D rect = frame.Project(box, transform);
                    // Revit 的註解外框通常帶有少量文字留白或控制範圍；以紙面固定值
                    // 向內收縮，但保留填滿區域與影像的完整可見範圍。
                    if (!(element is FilledRegion) && !(element is ImageInstance))
                        rect = InsetAnnotationBounds(rect, annotationInset);
                    if (rect.Width > Epsilon || rect.Height > Epsilon) result.Rectangles.Add(rect);
                    return;
                }

                // 模型元件只使用目前視圖實際畫出的幾何邊線。若以整個元件 BoundingBox
                // 判斷，樓板、地形或大型族的外框會把其內部及周圍空白全部誤判為障礙。
                Options geometryOptions = new Options
                {
                    IncludeNonVisibleObjects = false,
                    ComputeReferences = false
                };
                if (view != null && view.Document.Equals(element.Document)) geometryOptions.View = view;
                else geometryOptions.DetailLevel = ViewDetailLevel.Fine;
                AddGeometryObstacles(result, element.get_Geometry(geometryOptions), frame, transform);
            }
            catch { }
        }

        private static void AddGeometryObstacles(ObstacleSet result, GeometryElement geometry,
            ViewFrame frame, Transform outerTransform)
        {
            if (geometry == null) return;
            foreach (GeometryObject item in geometry)
            {
                if (item is Curve curve) AddCurveObstacle(result, curve, frame, outerTransform);
                else if (item is PolyLine polyLine)
                {
                    IList<XYZ> points = polyLine.GetCoordinates();
                    for (int i = 1; i < points.Count; i++)
                        AddSegmentObstacle(result, points[i - 1], points[i], frame, outerTransform);
                }
                else if (item is GeometryInstance instance)
                    AddGeometryObstacles(result, instance.GetInstanceGeometry(), frame, outerTransform);
                else if (item is Solid solid && solid.Edges.Size > 0)
                    foreach (Edge edge in solid.Edges)
                        AddCurveObstacle(result, edge.AsCurve(), frame, outerTransform);
                else if (item is Mesh mesh)
                {
                    for (int i = 0; i < mesh.NumTriangles; i++)
                    {
                        MeshTriangle triangle = mesh.get_Triangle(i);
                        XYZ a = triangle.get_Vertex(0); XYZ b = triangle.get_Vertex(1); XYZ c = triangle.get_Vertex(2);
                        AddSegmentObstacle(result, a, b, frame, outerTransform);
                        AddSegmentObstacle(result, b, c, frame, outerTransform);
                        AddSegmentObstacle(result, c, a, frame, outerTransform);
                    }
                }
            }
        }

        private static void AddCurveObstacle(ObstacleSet result, Curve curve, ViewFrame frame, Transform transform)
        {
            if (curve == null) return;
            IList<XYZ> points = curve.Tessellate();
            for (int i = 1; i < points.Count; i++)
                AddSegmentObstacle(result, points[i - 1], points[i], frame, transform);
        }

        private static void AddSegmentObstacle(ObstacleSet result, XYZ a, XYZ b,
            ViewFrame frame, Transform transform)
        {
            if (transform != null) { a = transform.OfPoint(a); b = transform.OfPoint(b); }
            UV2 start = frame.Project(a); UV2 end = frame.Project(b);
            if (Distance(start, end) > Epsilon) result.Segments.Add(new Segment2D(start, end));
        }

        private static bool IsRegionLine(Element element)
        {
            CurveElement curve = element as CurveElement;
            return curve?.LineStyle != null && curve.LineStyle.Name == RegionLineStyleName;
        }

        private static Rect2D InsetAnnotationBounds(Rect2D rect, double requestedInset)
        {
            double insetU = Math.Min(requestedInset, rect.Width * 0.20);
            double insetV = Math.Min(requestedInset, rect.Height * 0.20);
            return new Rect2D(rect.MinU + insetU, rect.MaxU - insetU,
                rect.MinV + insetV, rect.MaxV - insetV);
        }

        private static List<Rect2D> FindAutomaticRegions(Rect2D crop, List<List<UV2>> cropLoops,
            ObstacleSet obstacles, double cellWidth, double cellHeight, out int verticalMergeCount,
            out List<Rect2D> placementSlots)
        {
            verticalMergeCount = 0;
            placementSlots = new List<Rect2D>();
            List<Rect2D> candidates = new List<Rect2D>();
            if (cellWidth <= Epsilon || cellHeight <= Epsilon) return candidates;
            double cropTolerance = Math.Max(Epsilon * 10.0, Math.Min(cellWidth, cellHeight) * 0.001);

            // 固定單一格網起點會漏掉未與裁切框對齊的空白帶。
            // 以四分之一格為位移掃描 16 組格網，再挑選互不重疊的最大候選區。
            const int phaseCount = 4;
            for (int horizontalPhase = 0; horizontalPhase < phaseCount; horizontalPhase++)
            {
                double startU = crop.MinU + horizontalPhase * cellWidth / phaseCount;
                int columns = (int)Math.Floor((crop.MaxU - startU) / cellWidth);
                if (columns < 1) continue;
                for (int verticalPhase = 0; verticalPhase < phaseCount; verticalPhase++)
                {
                    double startV = crop.MinV + verticalPhase * cellHeight / phaseCount;
                    int rows = (int)Math.Floor((crop.MaxV - startV) / cellHeight);
                    if (rows < 1) continue;
                    for (int column = 0; column < columns; column++)
                    {
                        bool[] free = new bool[rows];
                        double minU = startU + column * cellWidth;
                        for (int row = 0; row < rows; row++)
                        {
                            Rect2D cell = new Rect2D(minU, minU + cellWidth,
                                startV + row * cellHeight, startV + (row + 1) * cellHeight);
                            free[row] = IsInsideCrop(cell, cropLoops, cropTolerance) && !obstacles.IsBlocked(cell);
                        }
                        int rowIndex = 0;
                        while (rowIndex < rows)
                        {
                            while (rowIndex < rows && !free[rowIndex]) rowIndex++;
                            int runStart = rowIndex;
                            while (rowIndex < rows && free[rowIndex]) rowIndex++;
                            int remaining = rowIndex - runStart;
                            int partStart = runStart;
                            while (remaining > 0)
                            {
                                int count = Math.Min(MaximumRowsPerRegion, remaining);
                                candidates.Add(new Rect2D(minU, minU + cellWidth,
                                    startV + partStart * cellHeight,
                                    startV + (partStart + count) * cellHeight));
                                partStart += count;
                                remaining -= count;
                            }
                        }
                    }
                }
            }

            List<Rect2D> result = new List<Rect2D>();
            foreach (Rect2D candidate in candidates
                .OrderByDescending(region => region.Height)
                .ThenBy(region => region.MinU).ThenBy(region => region.MinV))
            {
                if (!result.Any(existing => existing.IntersectsInterior(candidate))) result.Add(candidate);
            }
            placementSlots = ExpandRegionsToSlots(result, cellHeight);
            return MergeVerticalRegions(result, cropLoops, obstacles, cellWidth, cellHeight, cropTolerance,
                out verticalMergeCount);
        }

        private static List<Rect2D> ExpandRegionsToSlots(IEnumerable<Rect2D> regions, double cellHeight)
        {
            List<Rect2D> slots = new List<Rect2D>();
            foreach (Rect2D region in regions)
            {
                int rows = (int)Math.Floor((region.Height + Epsilon) / cellHeight);
                for (int row = 0; row < rows; row++)
                {
                    double minV = region.MinV + row * cellHeight;
                    slots.Add(new Rect2D(region.MinU, region.MaxU, minV, minV + cellHeight));
                }
            }
            return slots;
        }

        private static List<Rect2D> MergeVerticalRegions(List<Rect2D> regions,
            List<List<UV2>> cropLoops, ObstacleSet obstacles, double cellWidth, double cellHeight,
            double cropTolerance, out int mergeCount)
        {
            mergeCount = 0;
            List<Rect2D> result = regions.OrderBy(region => region.MinU)
                .ThenBy(region => region.MinV).ToList();
            double exactAlignmentTolerance = Math.Max(Epsilon * 10.0, cellWidth * 0.001);
            double maximumHorizontalShift = cellWidth * 0.26;
            double maximumVerticalGap = cellHeight * 1.26;
            double sharedBoundaryTolerance = Math.Max(Epsilon * 10.0, cellHeight * 0.001);
            double maximumHeight = MaximumRowsPerRegion * cellHeight + sharedBoundaryTolerance;

            bool mergedAny;
            do
            {
                mergedAny = false;
                for (int i = 0; i < result.Count && !mergedAny; i++)
                {
                    for (int j = i + 1; j < result.Count; j++)
                    {
                        Rect2D first = result[i]; Rect2D second = result[j];
                        double horizontalShift = Math.Max(Math.Abs(first.MinU - second.MinU),
                            Math.Abs(first.MaxU - second.MaxU));
                        if (horizontalShift > maximumHorizontalShift) continue;

                        Rect2D lower = first.MinV <= second.MinV ? first : second;
                        Rect2D upper = ReferenceEquals(lower, first) ? second : first;
                        double gap = upper.MinV - lower.MaxV;
                        if (gap < -sharedBoundaryTolerance || gap > maximumVerticalGap) continue;

                        Rect2D merged = TryCreateVerticalMerge(lower, upper, horizontalShift,
                            exactAlignmentTolerance, cropLoops, obstacles, cellHeight, cropTolerance,
                            maximumHeight);
                        if (merged == null) continue;
                        if (result.Where((region, index) => index != i && index != j)
                            .Any(region => region.IntersectsInterior(merged))) continue;

                        result[i] = merged;
                        result.RemoveAt(j);
                        mergeCount++;
                        result = result.OrderBy(region => region.MinU)
                            .ThenBy(region => region.MinV).ToList();
                        mergedAny = true;
                        break;
                    }
                }
            }
            while (mergedAny);

            return result;
        }

        private static Rect2D TryCreateVerticalMerge(Rect2D lower, Rect2D upper,
            double horizontalShift, double exactAlignmentTolerance, List<List<UV2>> cropLoops,
            ObstacleSet obstacles, double cellHeight, double cropTolerance, double maximumHeight)
        {
            List<double> candidateMinUs = new List<double> { lower.MinU };
            if (Math.Abs(upper.MinU - lower.MinU) > exactAlignmentTolerance)
            {
                candidateMinUs.Add(upper.MinU);
                candidateMinUs.Add((lower.MinU + upper.MinU) / 2.0);
            }

            foreach (double minU in candidateMinUs)
            {
                double maxU = minU + Math.Min(lower.Width, upper.Width);
                Rect2D merged = new Rect2D(minU, maxU, lower.MinV, upper.MaxV);
                if (merged.Height > maximumHeight ||
                    !IsInsideCrop(merged, cropLoops, cropTolerance)) continue;

                // 完全對齊時，兩個框本身已逐格通過障礙檢查；中間被細線占用的一格
                // 只作為合併外框的間隔。偏移框則用原有格高重新驗證所選欄位。
                if (horizontalShift <= exactAlignmentTolerance ||
                    (IsColumnRangeClear(minU, maxU, lower.MinV, lower.MaxV, cellHeight, obstacles) &&
                     IsColumnRangeClear(minU, maxU, upper.MinV, upper.MaxV, cellHeight, obstacles)))
                    return merged;
            }
            return null;
        }

        private static bool IsColumnRangeClear(double minU, double maxU, double minV, double maxV,
            double cellHeight, ObstacleSet obstacles)
        {
            for (double rowMin = minV; rowMin < maxV - Epsilon; rowMin += cellHeight)
            {
                double rowMax = Math.Min(maxV, rowMin + cellHeight);
                if (obstacles.IsBlocked(new Rect2D(minU, maxU, rowMin, rowMax))) return false;
            }
            return true;
        }

        private static List<Rect2D> MergeVerticalDisplayRegions(List<Rect2D> regions,
            double cellWidth, double cellHeight, out int mergeCount)
        {
            mergeCount = 0;
            List<Rect2D> result = regions.OrderBy(region => region.MinU)
                .ThenBy(region => region.MinV).ToList();
            double maximumCenterShift = cellWidth * 0.26;
            double maximumVerticalGap = cellHeight * 1.26;
            double maximumHeight = MaximumRowsPerRegion * cellHeight + Epsilon;

            bool mergedAny;
            do
            {
                mergedAny = false;
                for (int i = 0; i < result.Count && !mergedAny; i++)
                {
                    for (int j = i + 1; j < result.Count; j++)
                    {
                        Rect2D first = result[i]; Rect2D second = result[j];
                        double firstCenter = (first.MinU + first.MaxU) / 2.0;
                        double secondCenter = (second.MinU + second.MaxU) / 2.0;
                        if (Math.Abs(firstCenter - secondCenter) > maximumCenterShift) continue;

                        Rect2D lower = first.MinV <= second.MinV ? first : second;
                        Rect2D upper = ReferenceEquals(lower, first) ? second : first;
                        double gap = upper.MinV - lower.MaxV;
                        if (gap < -Epsilon || gap > maximumVerticalGap) continue;

                        Rect2D merged = new Rect2D(Math.Min(first.MinU, second.MinU),
                            Math.Max(first.MaxU, second.MaxU), lower.MinV, upper.MaxV);
                        if (merged.Height > maximumHeight) continue;

                        // 顯示外框可以跨過已知的細線隔離區，但不可蓋到另一個已保留的
                        // 空白框；實際可放置格位仍保留在原始 regions 內。
                        if (result.Where((region, index) => index != i && index != j)
                            .Any(region => region.IntersectsInterior(merged))) continue;

                        result[i] = merged;
                        result.RemoveAt(j);
                        result = result.OrderBy(region => region.MinU)
                            .ThenBy(region => region.MinV).ToList();
                        mergeCount++;
                        mergedAny = true;
                        break;
                    }
                }
            }
            while (mergedAny);

            return result;
        }

        private static List<Rect2D> CreateManualRegions(ViewFrame frame, List<PickedBox> boxes,
            double cellWidth, double cellHeight)
        {
            List<Rect2D> result = new List<Rect2D>();
            if (boxes == null) return result;
            foreach (PickedBox box in boxes)
            {
                UV2 a = frame.Project(box.Min); UV2 b = frame.Project(box.Max);
                double minU = Math.Min(a.U, b.U); double maxU = Math.Max(a.U, b.U);
                double minV = Math.Min(a.V, b.V); double maxV = Math.Max(a.V, b.V);
                if (maxU - minU + Epsilon < cellWidth || maxV - minV + Epsilon < cellHeight) continue;
                result.Add(new Rect2D(minU, maxU, minV, maxV));
            }
            return result;
        }

        private static List<Rect2D> CreateManualSlots(ViewFrame frame, List<PickedBox> boxes,
            double cellWidth, double cellHeight)
        {
            List<Rect2D> slots = new List<Rect2D>();
            if (boxes == null || cellWidth <= Epsilon || cellHeight <= Epsilon) return slots;
            foreach (PickedBox box in boxes)
            {
                UV2 a = frame.Project(box.Min); UV2 b = frame.Project(box.Max);
                double minU = Math.Min(a.U, b.U); double maxU = Math.Max(a.U, b.U);
                double minV = Math.Min(a.V, b.V); double maxV = Math.Max(a.V, b.V);
                int columns = (int)Math.Floor((maxU - minU + Epsilon) / cellWidth);
                int rows = (int)Math.Floor((maxV - minV + Epsilon) / cellHeight);

                // 手動框選不檢查障礙物，也不限制 15 列。每一欄由上往下排滿後，
                // 再以視圖中最長標籤的寬度向右換到下一欄。
                for (int column = 0; column < columns; column++)
                {
                    double slotMinU = minU + column * cellWidth;
                    for (int row = 0; row < rows; row++)
                    {
                        double slotMaxV = maxV - row * cellHeight;
                        slots.Add(new Rect2D(slotMinU, slotMinU + cellWidth,
                            slotMaxV - cellHeight, slotMaxV));
                    }
                }
            }
            return slots;
        }

        private static List<List<UV2>> GetCropLoops(ViewPlan view, ViewFrame frame)
        {
            List<List<UV2>> result = new List<List<UV2>>();
            try
            {
                foreach (CurveLoop loop in view.GetCropRegionShapeManager().GetCropShape())
                {
                    List<UV2> points = new List<UV2>();
                    foreach (Curve curve in loop)
                        foreach (XYZ point in curve.Tessellate())
                        {
                            UV2 projected = frame.Project(point);
                            if (points.Count == 0 || Distance(points.Last(), projected) > Epsilon) points.Add(projected);
                        }
                    if (points.Count >= 3) result.Add(points);
                }
            }
            catch { }
            return result;
        }

        private static bool IsInsideCrop(Rect2D cell, List<List<UV2>> loops, double tolerance)
        {
            if (loops.Count == 0) return true;
            return new[] { new UV2(cell.MinU, cell.MinV), new UV2(cell.MaxU, cell.MinV),
                new UV2(cell.MaxU, cell.MaxV), new UV2(cell.MinU, cell.MaxV) }
                .All(point => IsPointInsideOrOnBoundary(point, loops, tolerance));
        }

        private static bool IsPointInsideOrOnBoundary(UV2 point, List<List<UV2>> loops, double tolerance)
        {
            if (loops.Any(polygon => IsPointOnBoundary(point, polygon, tolerance))) return true;
            bool inside = false;
            foreach (List<UV2> polygon in loops)
            {
                bool inThisLoop = false;
                for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                {
                    UV2 a = polygon[i]; UV2 b = polygon[j];
                    if (((a.V > point.V) != (b.V > point.V)) &&
                        point.U < (b.U - a.U) * (point.V - a.V) / (b.V - a.V) + a.U)
                        inThisLoop = !inThisLoop;
                }
                if (inThisLoop) inside = !inside;
            }
            return inside;
        }

        private static bool IsPointOnBoundary(UV2 point, List<UV2> polygon, double tolerance)
        {
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                UV2 a = polygon[j]; UV2 b = polygon[i];
                double du = b.U - a.U; double dv = b.V - a.V;
                double lengthSquared = du * du + dv * dv;
                if (lengthSquared <= Epsilon) continue;
                double parameter = ((point.U - a.U) * du + (point.V - a.V) * dv) / lengthSquared;
                if (parameter < 0.0 || parameter > 1.0) continue;
                double nearestU = a.U + parameter * du; double nearestV = a.V + parameter * dv;
                double distance = Math.Sqrt((point.U - nearestU) * (point.U - nearestU) +
                    (point.V - nearestV) * (point.V - nearestV));
                if (distance <= tolerance) return true;
            }
            return false;
        }

        private static void ClearOldRegionLines(Document doc, ViewPlan view)
        {
            List<ElementId> ids = new FilteredElementCollector(doc, view.Id).OfClass(typeof(CurveElement))
                .Cast<CurveElement>().Where(IsRegionLine).Where(curve => curve.OwnerViewId == view.Id)
                .Select(curve => curve.Id).ToList();
            if (ids.Count > 0) doc.Delete(ids);
        }

        private static GraphicsStyle GetOrCreateRegionLineStyle(Document doc)
        {
            Category lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            Category style = lines.SubCategories.Cast<Category>()
                .FirstOrDefault(category => category.Name == RegionLineStyleName);
            if (style == null) style = doc.Settings.Categories.NewSubcategory(lines, RegionLineStyleName);
            style.LineColor = new Autodesk.Revit.DB.Color(0, 255, 255);
            try { style.SetLineWeight(3, GraphicsStyleType.Projection); } catch { }
            return style.GetGraphicsStyle(GraphicsStyleType.Projection);
        }

        private static void DrawRegions(Document doc, ViewPlan view, ViewFrame frame,
            IEnumerable<Rect2D> regions, GraphicsStyle style)
        {
            foreach (Rect2D box in regions)
            {
                XYZ a = frame.PointAt(view.Origin, box.MinU, box.MinV);
                XYZ b = frame.PointAt(view.Origin, box.MaxU, box.MinV);
                XYZ c = frame.PointAt(view.Origin, box.MaxU, box.MaxV);
                XYZ d = frame.PointAt(view.Origin, box.MinU, box.MaxV);
                foreach (Line line in new[] { Line.CreateBound(a, b), Line.CreateBound(b, c),
                    Line.CreateBound(c, d), Line.CreateBound(d, a) })
                {
                    DetailCurve curve = doc.Create.NewDetailCurve(view, line);
                    curve.LineStyle = style;
                }
            }
        }

        private static double Distance(UV2 a, UV2 b)
            => Math.Sqrt((a.U - b.U) * (a.U - b.U) + (a.V - b.V) * (a.V - b.V));

        private class OverwriteFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            { overwriteParameterValues = true; return true; }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            { source = FamilySource.Family; overwriteParameterValues = true; return true; }
        }

        private class TagSize
        {
            public TagSize(double width, double height) { Width = width; Height = height; }
            public double Width { get; }
            public double Height { get; }
        }

        private class TagPlacementData
        {
            public IndependentTag Tag { get; set; }
            public Reference TargetReference { get; set; }
            public XYZ AnchorPoint { get; set; }
            public double AnchorU { get; set; }
            public double AnchorV { get; set; }
            public double LeftOffsetFromHead { get; set; }
            public double CenterVOffsetFromHead { get; set; }
            public double BodyWidth { get; set; }
            public double BodyHeight { get; set; }
            public bool IsPipe { get; set; }
        }

        private class PlacementGroup
        {
            public PlacementGroup(Rect2D bounds) { DisplayBounds = bounds; }
            public Rect2D DisplayBounds { get; }
            public List<Rect2D> Slots { get; } = new List<Rect2D>();
            public List<TagPlacementData> AssignedTags { get; } = new List<TagPlacementData>();
        }

        private class UV2
        {
            public UV2(double u, double v) { U = u; V = v; }
            public double U { get; }
            public double V { get; }
        }

        private class Rect2D
        {
            public Rect2D(double minU, double maxU, double minV, double maxV)
            { MinU = minU; MaxU = maxU; MinV = minV; MaxV = maxV; }
            public double MinU { get; }
            public double MaxU { get; }
            public double MinV { get; }
            public double MaxV { get; }
            public double Width => MaxU - MinU;
            public double Height => MaxV - MinV;
            public bool Contains(UV2 point) => point.U >= MinU - Epsilon && point.U <= MaxU + Epsilon &&
                point.V >= MinV - Epsilon && point.V <= MaxV + Epsilon;
            public bool IntersectsInterior(Rect2D other) => MaxU > other.MinU + Epsilon &&
                MinU < other.MaxU - Epsilon && MaxV > other.MinV + Epsilon && MinV < other.MaxV - Epsilon;
        }

        private class Segment2D
        {
            public Segment2D(UV2 start, UV2 end) { Start = start; End = end; }
            public UV2 Start { get; }
            public UV2 End { get; }
            public bool Intersects(Rect2D box)
            {
                Rect2D inner = new Rect2D(box.MinU + Epsilon, box.MaxU - Epsilon,
                    box.MinV + Epsilon, box.MaxV - Epsilon);
                if (inner.Width <= Epsilon || inner.Height <= Epsilon) return false;
                if ((Start.U < inner.MinU && End.U < inner.MinU) || (Start.U > inner.MaxU && End.U > inner.MaxU) ||
                    (Start.V < inner.MinV && End.V < inner.MinV) || (Start.V > inner.MaxV && End.V > inner.MaxV)) return false;
                if (inner.Contains(Start) || inner.Contains(End)) return true;
                UV2 a = new UV2(inner.MinU, inner.MinV); UV2 b = new UV2(inner.MaxU, inner.MinV);
                UV2 c = new UV2(inner.MaxU, inner.MaxV); UV2 d = new UV2(inner.MinU, inner.MaxV);
                return Crosses(Start, End, a, b) || Crosses(Start, End, b, c) ||
                       Crosses(Start, End, c, d) || Crosses(Start, End, d, a);
            }
            private static bool Crosses(UV2 a, UV2 b, UV2 c, UV2 d)
            {
                double o1 = Orientation(a, b, c); double o2 = Orientation(a, b, d);
                double o3 = Orientation(c, d, a); double o4 = Orientation(c, d, b);
                return o1 * o2 <= Epsilon && o3 * o4 <= Epsilon;
            }
            private static double Orientation(UV2 a, UV2 b, UV2 c)
                => (b.U - a.U) * (c.V - a.V) - (b.V - a.V) * (c.U - a.U);
        }

        private class ObstacleSet
        {
            public List<Rect2D> Rectangles { get; } = new List<Rect2D>();
            public List<Segment2D> Segments { get; } = new List<Segment2D>();
            public bool IsBlocked(Rect2D cell) => Rectangles.Any(rect => rect.IntersectsInterior(cell)) ||
                Segments.Any(segment => segment.Intersects(cell));
        }

        private class ViewFrame
        {
            private readonly XYZ _right;
            private readonly XYZ _up;
            public ViewFrame(Autodesk.Revit.DB.View view) { _right = view.RightDirection.Normalize(); _up = view.UpDirection.Normalize(); }
            public UV2 Project(XYZ point) => new UV2(point.DotProduct(_right), point.DotProduct(_up));
            public Rect2D Project(BoundingBoxXYZ box, Transform outerTransform = null)
            {
                List<UV2> points = new List<UV2>();
                foreach (double x in new[] { box.Min.X, box.Max.X })
                    foreach (double y in new[] { box.Min.Y, box.Max.Y })
                        foreach (double z in new[] { box.Min.Z, box.Max.Z })
                        {
                            XYZ point = box.Transform.OfPoint(new XYZ(x, y, z));
                            if (outerTransform != null) point = outerTransform.OfPoint(point);
                            points.Add(Project(point));
                        }
                return new Rect2D(points.Min(p => p.U), points.Max(p => p.U),
                    points.Min(p => p.V), points.Max(p => p.V));
            }
            public XYZ PointAt(XYZ planePoint, double u, double v)
            {
                UV2 current = Project(planePoint);
                return planePoint + _right * (u - current.U) + _up * (v - current.V);
            }
        }
    }
}
