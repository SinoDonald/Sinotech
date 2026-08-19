using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace Sinotech_2025.CSDSEM
{
    /// <summary>
    /// 自動開口與套管標籤建立外部命令
    /// 包含品類專屬標籤自動匹配、防重複標註與全域多視圖累加統計
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AutoOpeningTag : IExternalCommand
    {
        #region IExternalCommand Entry Point

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData?.Application;
            if (uiapp == null)
            {
                message = "無法取得 UIApplication 物件。";
                return Result.Failed;
            }

            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null)
            {
                message = "未開啟任何專案文件。";
                return Result.Failed;
            }

            try
            {
                using (AutoNumberForm autoNumberForm = new AutoNumberForm(doc))
                {
                    if (autoNumberForm.ShowDialog() != DialogResult.OK)
                    {
                        return Result.Cancelled;
                    }

                    List<ViewPlan> availableViewPlans = GetAutoNumberViewPlans(doc, autoNumberForm.viewFamilyTypeName);
                    if (availableViewPlans == null || availableViewPlans.Count == 0)
                    {
                        TaskDialog.Show("警告", "找不到符合出圖條件的平面視圖，請確認視圖設定或「圖面分類」參數！");
                        return Result.Failed;
                    }

                    using (ChooseMultiViewPlansForm chooseForm = new ChooseMultiViewPlansForm(doc, availableViewPlans, ChooseMultiViewPlansForm.FormMode.TagArray))
                    {
                        if (chooseForm.ShowDialog() != DialogResult.OK)
                        {
                            return Result.Cancelled;
                        }

                        List<ViewPlan> selectedViews = chooseForm.checkViewPlans;
                        if (selectedViews == null || selectedViews.Count == 0)
                        {
                            TaskDialog.Show("提示", "未勾選任何視圖。");
                            return Result.Cancelled;
                        }

                        // 執行全視圖批次標籤作業
                        TaggingSessionResult sessionResult = ExecuteBatchOpeningTagging(uidoc, doc, selectedViews);

                        // 呈現報告與匯出
                        DisplayCompletionDialog(sessionResult);
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"執行過程發生未預期錯誤：\n{ex.Message}\n{ex.StackTrace}";
                return Result.Failed;
            }
        }

        #endregion

        #region Core Batch Processing Logic

        /// <summary>
        /// 批次處理所有選定視圖的開口標籤放置作業
        /// </summary>
        private TaggingSessionResult ExecuteBatchOpeningTagging(UIDocument uidoc, Document doc, List<ViewPlan> targetViews)
        {
            TaggingSessionResult sessionResult = new TaggingSessionResult
            {
                StartTime = DateTime.Now
            };

            // 1. 預先建立各宿主品類專屬的標籤族群型別字典 (Category -> FamilySymbol)
            Dictionary<BuiltInCategory, FamilySymbol> tagSymbolMap = BuildCategoryTagSymbolMap(doc);

            using (ProgressForm progressForm = new ProgressForm("自動開口標籤作業中", targetViews.Count))
            {
                progressForm.Show();

                // 依據 PrimaryViewId 分組處理，降低視圖切換之重繪負擔
                var groupedByPrimaryView = targetViews
                    .GroupBy(v => v.GetPrimaryViewId() != ElementId.InvalidElementId ? v.GetPrimaryViewId() : v.Id)
                    .ToList();

                foreach (var viewGroup in groupedByPrimaryView)
                {
                    ElementId parentId = viewGroup.Key;
                    if (doc.GetElement(parentId) is ViewPlan parentView)
                    {
                        try
                        {
                            uidoc.RequestViewChange(parentView);
                            System.Windows.Forms.Application.DoEvents();
                        }
                        catch { /* 忽略視圖切換限制 */ }
                    }

                    foreach (ViewPlan currentPlan in viewGroup)
                    {
                        progressForm.UpdateProgress(currentPlan.Name);
                        System.Windows.Forms.Application.DoEvents();

                        // 處理單一視圖
                        List<ElementId> tagsInThisView = TagOpeningsInSingleView(doc, currentPlan, tagSymbolMap);

                        // 累計各視圖成果
                        if (tagsInThisView.Count > 0)
                        {
                            sessionResult.CreatedTagIds.AddRange(tagsInThisView);
                            sessionResult.ViewTagSummary[currentPlan.Name] = tagsInThisView.Count;
                        }
                    }
                }

                progressForm.Close();
            }

            sessionResult.EndTime = DateTime.Now;
            return sessionResult;
        }

        /// <summary>
        /// 針對單一視圖執行開口與套管標籤建立
        /// </summary>
        private List<ElementId> TagOpeningsInSingleView(Document doc, ViewPlan view, Dictionary<BuiltInCategory, FamilySymbol> tagSymbolMap)
        {
            List<ElementId> createdInCurrentView = new List<ElementId>();

            // 1. 計算該視圖的 Z 軸與 CropBox 平面範圍
            if (!CalculateViewBoundingLimits(view, out double minX, out double maxX, out double minY, out double maxY, out double validZMin, out double validZMax))
            {
                return createdInCurrentView;
            }

            // 2. 取得該視圖中目前已經標記過的 Host ElementId 集合
            HashSet<ElementId> existingTaggedIds = GetExistingTaggedHostIds(doc, view);

            // 3. 收集主模型中屬於此視圖高度範圍內的開口/套管/配件元件
            List<Element> hostOpenings = GetCandidateOpeningElements(doc, validZMin, validZMax);
            if (hostOpenings == null || hostOpenings.Count == 0)
            {
                return createdInCurrentView;
            }

            // 4. 開啟 Transaction 進行標籤建立
            using (Transaction trans = new Transaction(doc, $"自動開口標籤 - {view.Name}"))
            {
                trans.Start();

                foreach (Element openingElem in hostOpenings)
                {
                    if (existingTaggedIds.Contains(openingElem.Id))
                    {
                        continue;
                    }

                    XYZ centerPoint = CalculateElementCenter(openingElem);
                    if (centerPoint == null)
                    {
                        continue;
                    }

                    if (!IsPointInsideViewCrop(centerPoint, view))
                    {
                        continue;
                    }

                    // 判斷該元件所屬品類
                    BuiltInCategory hostCategory = GetElementBuiltInCategory(openingElem);

                    // 取得對應品類的專屬標籤族群型別
                    FamilySymbol targetTagSymbol = null;
                    if (tagSymbolMap.TryGetValue(hostCategory, out FamilySymbol mappedSymbol))
                    {
                        targetTagSymbol = mappedSymbol;
                    }

                    // 確保 Symbol 已被 Activate
                    if (targetTagSymbol != null && !targetTagSymbol.IsActive)
                    {
                        targetTagSymbol.Activate();
                        doc.Regenerate();
                    }

                    try
                    {
                        Reference elemRef = new Reference(openingElem);
                        IndependentTag tag = IndependentTag.Create(
                            doc,
                            view.Id,
                            elemRef,
                            false,
                            TagMode.TM_ADDBY_CATEGORY,
                            TagOrientation.Horizontal,
                            centerPoint
                        );

                        if (tag != null)
                        {
                            // 【關鍵修復】：確保傳入的 TypeId 與新建立標籤的 Category 完全相容
                            if (targetTagSymbol != null && tag.GetTypeId() != targetTagSymbol.Id)
                            {
                                if (tag.IsSchemaCompatible(targetTagSymbol))
                                {
                                    tag.ChangeTypeId(targetTagSymbol.Id);
                                }
                            }

                            createdInCurrentView.Add(tag.Id);
                            existingTaggedIds.Add(openingElem.Id);
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        // 略過不可貼附或幾何無效之元件
                    }
                }

                trans.Commit();
            }

            return createdInCurrentView;
        }

        #endregion

        #region Tag Symbol Category Mapping

        /// <summary>
        /// 預先構建宿主品類與標籤族群型別的映射字典
        /// </summary>
        private Dictionary<BuiltInCategory, FamilySymbol> BuildCategoryTagSymbolMap(Document doc)
        {
            Dictionary<BuiltInCategory, FamilySymbol> map = new Dictionary<BuiltInCategory, FamilySymbol>();

            // 1. PipeAccessory -> OST_PipeAccessoryTags
            FamilySymbol pipeAccTag = GetTagSymbol(doc, BuiltInCategory.OST_PipeAccessoryTags, "開口標籤")
                                   ?? GetTagSymbol(doc, BuiltInCategory.OST_PipeAccessoryTags, null);
            if (pipeAccTag != null) map[BuiltInCategory.OST_PipeAccessory] = pipeAccTag;

            // 2. DuctAccessory -> OST_DuctAccessoryTags
            FamilySymbol ductAccTag = GetTagSymbol(doc, BuiltInCategory.OST_DuctAccessoryTags, "開口標籤")
                                   ?? GetTagSymbol(doc, BuiltInCategory.OST_DuctAccessoryTags, null);
            if (ductAccTag != null) map[BuiltInCategory.OST_DuctAccessory] = ductAccTag;

            // 3. CableTrayFitting -> OST_CableTrayFittingTags
            FamilySymbol trayFitTag = GetTagSymbol(doc, BuiltInCategory.OST_CableTrayFittingTags, "開口標籤")
                                   ?? GetTagSymbol(doc, BuiltInCategory.OST_CableTrayFittingTags, null);
            if (trayFitTag != null) map[BuiltInCategory.OST_CableTrayFitting] = trayFitTag;

            // 4. GenericModel -> OST_GenericModelTags
            FamilySymbol genericTag = GetTagSymbol(doc, BuiltInCategory.OST_GenericModelTags, "開口標籤")
                                   ?? GetTagSymbol(doc, BuiltInCategory.OST_GenericModelTags, null);
            if (genericTag != null) map[BuiltInCategory.OST_GenericModel] = genericTag;

            return map;
        }

        /// <summary>
        /// 安全取得 Element 的 BuiltInCategory
        /// </summary>
        private BuiltInCategory GetElementBuiltInCategory(Element elem)
        {
            if (elem.Category != null)
            {
                return (BuiltInCategory)elem.Category.Id.Value;
            }
            return BuiltInCategory.INVALID;
        }

        #endregion

        #region Geometry & Bounds Calculation

        private List<Element> GetCandidateOpeningElements(Document doc, double validZMin, double validZMax)
        {
            List<ElementFilter> categoryFilters = new List<ElementFilter>
            {
                new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory),
                new ElementCategoryFilter(BuiltInCategory.OST_DuctAccessory),
                new ElementCategoryFilter(BuiltInCategory.OST_CableTrayFitting),
                new ElementCategoryFilter(BuiltInCategory.OST_GenericModel)
            };

            LogicalOrFilter orFilter = new LogicalOrFilter(categoryFilters);

            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .WherePasses(orFilter)
                .WhereElementIsNotElementType();

            List<Element> result = new List<Element>();
            foreach (Element elem in collector)
            {
                XYZ center = CalculateElementCenter(elem);
                if (center == null) continue;

                if (center.Z >= validZMin && center.Z <= validZMax)
                {
                    result.Add(elem);
                }
            }

            return result;
        }

        private HashSet<ElementId> GetExistingTaggedHostIds(Document doc, ViewPlan view)
        {
            HashSet<ElementId> taggedIds = new HashSet<ElementId>();

            FilteredElementCollector tagCollector = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(IndependentTag));

            foreach (IndependentTag tag in tagCollector.Cast<IndependentTag>())
            {
                try
                {
                    foreach (Reference r in tag.GetTaggedReferences())
                    {
                        if (r.ElementId != ElementId.InvalidElementId && r.LinkedElementId == ElementId.InvalidElementId)
                        {
                            taggedIds.Add(r.ElementId);
                        }
                    }
                }
                catch { }
            }

            return taggedIds;
        }

        private XYZ CalculateElementCenter(Element elem)
        {
            if (elem.Location is LocationPoint locPt)
            {
                return locPt.Point;
            }
            if (elem.Location is LocationCurve locCurve && locCurve.Curve != null)
            {
                return locCurve.Curve.Evaluate(0.5, true);
            }

            BoundingBoxXYZ bbox = elem.get_BoundingBox(null);
            if (bbox != null)
            {
                return (bbox.Min + bbox.Max) * 0.5;
            }

            return null;
        }

        private bool IsPointInsideViewCrop(XYZ point, ViewPlan view)
        {
            if (!view.CropBoxActive) return true;

            BoundingBoxXYZ cb = view.CropBox;
            if (cb == null) return true;

            const double tolerance = 1e-4;
            Transform invTransform = cb.Transform.Inverse;
            XYZ localPt = invTransform.OfPoint(point);

            return (localPt.X >= cb.Min.X - tolerance && localPt.X <= cb.Max.X + tolerance &&
                    localPt.Y >= cb.Min.Y - tolerance && localPt.Y <= cb.Max.Y + tolerance);
        }

        private bool CalculateViewBoundingLimits(ViewPlan view, out double minX, out double maxX, out double minY, out double maxY, out double validZMin, out double validZMax)
        {
            minX = minY = double.MinValue / 2;
            maxX = maxY = double.MaxValue / 2;

            double exactZMax = GetPlaneElevation(view, PlanViewPlane.TopClipPlane, 1000.0, -1000.0);
            double exactZMin = GetPlaneElevation(view, PlanViewPlane.ViewDepthPlane, 1000.0, -1000.0);

            validZMin = exactZMin - 0.5;
            validZMax = exactZMax + 0.5;

            BoundingBoxXYZ cb = view.CropBox;
            if (cb == null) return false;

            Transform ct = cb.Transform;
            if (view.CropBoxActive)
            {
                minX = minY = double.MaxValue;
                maxX = maxY = double.MinValue;

                foreach (double lx in new[] { cb.Min.X, cb.Max.X })
                {
                    foreach (double ly in new[] { cb.Min.Y, cb.Max.Y })
                    {
                        XYZ wp = ct.OfPoint(new XYZ(lx, ly, 0));
                        if (wp.X < minX) minX = wp.X;
                        if (wp.Y < minY) minY = wp.Y;
                        if (wp.X > maxX) maxX = wp.X;
                        if (wp.Y > maxY) maxY = wp.Y;
                    }
                }
            }

            return true;
        }

        private double GetPlaneElevation(ViewPlan view, PlanViewPlane plane, double defaultHigh, double defaultLow)
        {
            PlanViewRange viewRange = view.GetViewRange();
            ElementId levelId = viewRange.GetLevelId(plane);
            double offset = viewRange.GetOffset(plane);

            if (levelId == ElementId.InvalidElementId)
                return plane == PlanViewPlane.TopClipPlane ? defaultHigh : defaultLow;

            long idVal = levelId.Value;
            if (idVal < 0)
            {
                if (idVal == -5) return plane == PlanViewPlane.TopClipPlane ? defaultHigh : defaultLow;
                if (idVal == -2) return (view.GenLevel != null ? view.GenLevel.Elevation : 0) + offset;
                if (idVal == -4) return defaultHigh;
                if (idVal == -3) return defaultLow;
            }

            Element elem = view.Document.GetElement(levelId);
            if (elem is Level lvl) return lvl.Elevation + offset;

            return (view.GenLevel != null ? view.GenLevel.Elevation : 0) + offset;
        }

        private FamilySymbol GetTagSymbol(Document doc, BuiltInCategory tagCategory, string familyOrTypeName)
        {
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(tagCategory);

            if (string.IsNullOrEmpty(familyOrTypeName))
            {
                return collector.Cast<FamilySymbol>().FirstOrDefault();
            }

            return collector.Cast<FamilySymbol>()
                .FirstOrDefault(x => (x.FamilyName != null && x.FamilyName.Contains(familyOrTypeName)) ||
                                     (x.Name != null && x.Name.Contains(familyOrTypeName)));
        }

        public static List<ViewPlan> GetAutoNumberViewPlans(Document doc, string viewFamilyTypeName)
        {
            if (string.IsNullOrWhiteSpace(viewFamilyTypeName))
            {
                return new List<ViewPlan>();
            }

            int firstSpaceIndex = viewFamilyTypeName.IndexOf(' ');
            if (firstSpaceIndex < 0)
            {
                return new List<ViewPlan>();
            }

            string familyName = viewFamilyTypeName.Substring(0, firstSpaceIndex).Trim();
            string rawTypeName = viewFamilyTypeName.Substring(firstSpaceIndex + 1).Trim();

            string typeName = rawTypeName;
            if (typeName.StartsWith("(") && typeName.EndsWith(")") && typeName.Length >= 2)
            {
                typeName = typeName.Substring(1, typeName.Length - 2);
            }

            ViewFamilyType viewFamilyType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .Where(x => x.ViewFamily == ViewFamily.FloorPlan || x.ViewFamily == ViewFamily.CeilingPlan)
                .Where(x => x.Name.Contains("1/100"))
                .FirstOrDefault(x => string.Equals(x.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)
                                  && string.Equals(x.Name, typeName, StringComparison.OrdinalIgnoreCase));

            if (viewFamilyType == null) return new List<ViewPlan>();

            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .WhereElementIsNotElementType()
                .Where(x => x.GetTypeId().Equals(viewFamilyType.Id))
                .Cast<ViewPlan>()
                .Where(x => x.GenLevel != null)
                .Where(v => v.LookupParameter("圖面分類") != null && v.LookupParameter("圖面分類").AsString() == "出圖")
                .Where(x => x.GetDependentViewIds().Count == 0)
                .OrderBy(x => x.GenLevel.Elevation)
                .ToList();
        }

        #endregion

        #region Dialog & Reporting

        private void DisplayCompletionDialog(TaggingSessionResult result)
        {
            int totalCount = result.TotalCount;
            TimeSpan totalTime = result.Duration;

            if (totalCount > 0)
            {
                StringBuilder detailSummary = new StringBuilder();
                detailSummary.AppendLine($"作業完成！共在 {result.ViewTagSummary.Count} 個視圖中累計產生 {totalCount} 個開口標籤。");
                detailSummary.AppendLine($"總耗時：{totalTime.Minutes} 分 {totalTime.Seconds} 秒。\n");
                detailSummary.AppendLine("各視圖標籤產出明細：");
                foreach (var item in result.ViewTagSummary)
                {
                    detailSummary.AppendLine($" • {item.Key}：{item.Value} 個");
                }

                TaskDialog td = new TaskDialog("自動開口標籤完成")
                {
                    MainInstruction = $"所有視圖累計成功產生 {totalCount} 個開口標籤！",
                    MainContent = detailSummary.ToString(),
                    CommonButtons = TaskDialogCommonButtons.Close,
                    DefaultButton = TaskDialogResult.Close
                };

                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "匯出全部新增標籤 ID 清單 (.txt)");

                if (td.Show() == TaskDialogResult.CommandLink1)
                {
                    using (SaveFileDialog sfd = new SaveFileDialog())
                    {
                        sfd.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                        sfd.Title = "儲存全部標籤 ID 清單";
                        sfd.FileName = $"全部視圖開口標籤ID_{DateTime.Now:yyyyMMdd_HHmmss}.txt";

                        if (sfd.ShowDialog() == DialogResult.OK)
                        {
                            StringBuilder sb = new StringBuilder();
                            sb.AppendLine("=========================================");
                            sb.AppendLine("      自動產生開口標籤 ID 總清單 (QC專用)  ");
                            sb.AppendLine("=========================================");
                            sb.AppendLine($"產生時間: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                            sb.AppendLine($"涵蓋視圖數: {result.ViewTagSummary.Count}");
                            sb.AppendLine($"標籤總數量: {totalCount}");
                            sb.AppendLine("-----------------------------------------");
                            sb.AppendLine("[視圖統計明細]");
                            foreach (var kvp in result.ViewTagSummary)
                            {
                                sb.AppendLine($"{kvp.Key}: {kvp.Value} 個");
                            }
                            sb.AppendLine("-----------------------------------------");
                            sb.AppendLine("[標籤 Element IDs]");
                            foreach (ElementId id in result.CreatedTagIds)
                            {
                                sb.AppendLine(id.Value.ToString());
                            }

                            try
                            {
                                File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                                TaskDialog.Show("匯出成功", $"已成功匯出 {totalCount} 筆標籤 ID 至：\n{sfd.FileName}");
                            }
                            catch (Exception ex)
                            {
                                TaskDialog.Show("匯出失敗", $"檔案寫入失敗：\n{ex.Message}");
                            }
                        }
                    }
                }
            }
            else
            {
                TaskDialog.Show("Revit", "所選視圖中未發現需標註的新開口或套管（可能已全數標註或超出裁切範圍）。");
            }
        }

        #endregion
    }

    #region Supporting Extension & Models

    public static class TagExtensions
    {
        /// <summary>
        /// 判斷標籤與目標族群符號是否屬於相同 Category 架構 (避免 ChangeTypeId 拋出 ArgumentException)
        /// </summary>
        public static bool IsSchemaCompatible(this IndependentTag tag, FamilySymbol targetSymbol)
        {
            if (tag == null || targetSymbol == null) return false;
            if (tag.Category == null || targetSymbol.Category == null) return false;

            return tag.Category.Id.Value == targetSymbol.Category.Id.Value;
        }
    }

    public class TaggingSessionResult
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public List<ElementId> CreatedTagIds { get; } = new List<ElementId>();
        public Dictionary<string, int> ViewTagSummary { get; } = new Dictionary<string, int>();
        public int TotalCount => CreatedTagIds.Count;
    }

    #endregion
}