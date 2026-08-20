using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace Sinotech.CSDSEM
{
    /// <summary>
    /// 自動開口與套管標籤建立外部命令 (極速與高強健性商用版)
    /// 特性：全背景零視圖切換、全域快取、VG 可見性預檢、防幽靈標籤與全域累加統計
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

                        TransactionGroup tranGrp1 = new TransactionGroup(doc, "自動開口標籤");
                        tranGrp1.Start();
                        // 執行全視圖極速批次標籤作業 (含健壯防護)
                        TaggingSessionResult sessionResult = ExecuteFastBatchOpeningTagging(doc, selectedViews);
                        tranGrp1.Assimilate();

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

        #region Core Fast Batch Processing Logic

        /// <summary>
        /// 批次處理開口標籤放置 (全背景處理，零視圖切換)
        /// </summary>
        private TaggingSessionResult ExecuteFastBatchOpeningTagging(Document doc, List<ViewPlan> targetViews)
        {
            TaggingSessionResult sessionResult = new TaggingSessionResult
            {
                StartTime = DateTime.Now
            };

            // 1. 預先建立並激活各品類的標籤族群型別
            Dictionary<BuiltInCategory, FamilySymbol> tagSymbolMap = BuildAndActivateCategoryTagSymbolMap(doc);

            // 2. 預先快取主模型中所有開口幾何與中心資訊 (O(N) 降低至常數級運算)
            List<OpeningCacheItem> globalOpeningCache = PreCacheHostOpenings(doc);

            using (ProgressForm progressForm = new ProgressForm("自動開口標籤作業中", targetViews.Count))
            {
                progressForm.Show();

                // 循序在背景處理各視圖 (不切換 ActiveView)
                foreach (ViewPlan currentPlan in targetViews)
                {
                    progressForm.UpdateProgress(currentPlan.Name);
                    System.Windows.Forms.Application.DoEvents();

                    List<ElementId> tagsInThisView = TagOpeningsInSingleViewFast(doc, currentPlan, tagSymbolMap, globalOpeningCache);

                    if (tagsInThisView.Count > 0)
                    {
                        sessionResult.CreatedTagIds.AddRange(tagsInThisView);
                        sessionResult.ViewTagSummary[currentPlan.Name] = tagsInThisView.Count;
                    }
                }

                progressForm.Close();
            }

            sessionResult.EndTime = DateTime.Now;
            return sessionResult;
        }

        /// <summary>
        /// 單一視圖極速標註實作 (加入可見性防護)
        /// </summary>
        private List<ElementId> TagOpeningsInSingleViewFast(
            Document doc,
            ViewPlan view,
            Dictionary<BuiltInCategory, FamilySymbol> tagSymbolMap,
            List<OpeningCacheItem> openingCache)
        {
            List<ElementId> createdInCurrentView = new List<ElementId>();

            // 1. 快速提取視圖範圍與 CropBox
            if (!CalculateViewBoundingLimits(view, out double validZMin, out double validZMax, out BoundingBoxXYZ cropBox, out Transform invCropTransform))
            {
                return createdInCurrentView;
            }

            // 2. 取得該視圖既有標籤 Host ID (O(1) 防重複標註)
            HashSet<ElementId> existingTaggedIds = GetExistingTaggedHostIds(doc, view);

            // 3. 預檢各 Category 在該視圖中的 Visibility 狀態，避免產生幽靈標籤
            Dictionary<BuiltInCategory, bool> categoryVisibilityMap = GetCategoryVisibilityMap(view, tagSymbolMap.Keys);

            // 4. 開啟視圖級 Transaction 並掛載靜默警告處理
            using (Transaction trans = new Transaction(doc, $"自動開口標籤 - {view.Name}"))
            {
                FailureHandlingOptions failureOptions = trans.GetFailureHandlingOptions();
                failureOptions.SetFailuresPreprocessor(new SilentFailurePreprocessor());
                trans.SetFailureHandlingOptions(failureOptions);

                trans.Start();

                foreach (OpeningCacheItem item in openingCache)
                {
                    // 高度範圍快篩 (Z 軸數值檢核)
                    if (item.CenterPoint.Z < validZMin || item.CenterPoint.Z > validZMax)
                    {
                        continue;
                    }

                    // 防重複標註
                    if (existingTaggedIds.Contains(item.Id))
                    {
                        continue;
                    }

                    // 可見性防護：若該視圖關閉了該品類的可見性，則不標註
                    if (categoryVisibilityMap.TryGetValue(item.Category, out bool isVisible) && !isVisible)
                    {
                        continue;
                    }

                    // 裁切框檢核 (透過反轉矩陣計算 View 局部座標)
                    if (view.CropBoxActive && cropBox != null)
                    {
                        XYZ localPt = invCropTransform.OfPoint(item.CenterPoint);
                        const double tol = 1e-4;
                        if (localPt.X < cropBox.Min.X - tol || localPt.X > cropBox.Max.X + tol ||
                            localPt.Y < cropBox.Min.Y - tol || localPt.Y > cropBox.Max.Y + tol)
                        {
                            continue;
                        }
                    }

                    // 取得品類匹配的標籤型別
                    tagSymbolMap.TryGetValue(item.Category, out FamilySymbol targetTagSymbol);

                    try
                    {
                        Reference elemRef = new Reference(item.ElementInstance);

                        // 開口標籤放置於中心點
                        IndependentTag tag = IndependentTag.Create(
                            doc,
                            view.Id,
                            elemRef,
                            false, // 開口套管預設無引線，精準落於幾何中心
                            TagMode.TM_ADDBY_CATEGORY,
                            TagOrientation.Horizontal,
                            item.CenterPoint
                        );

                        if (tag != null)
                        {
                            // 安全型別替換
                            if (targetTagSymbol != null && tag.GetTypeId() != targetTagSymbol.Id)
                            {
                                if (tag.IsSchemaCompatible(targetTagSymbol))
                                {
                                    tag.ChangeTypeId(targetTagSymbol.Id);
                                }
                            }

                            // 確保標籤頭部位置鎖定在開口中心
                            tag.TagHeadPosition = item.CenterPoint;

                            createdInCurrentView.Add(tag.Id);
                            existingTaggedIds.Add(item.Id);
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {
                        // 略過少數不可標註之退化圖元
                    }
                }

                trans.Commit();
            }

            return createdInCurrentView;
        }

        #endregion

        #region Pre-Caching & Visibility Guards

        /// <summary>
        /// 開口元件快取結構 (值型別最佳化記憶體與快取命中率)
        /// </summary>
        private struct OpeningCacheItem
        {
            public ElementId Id;
            public Element ElementInstance;
            public BuiltInCategory Category;
            public XYZ CenterPoint;
        }

        /// <summary>
        /// 一次性全域快取主模型開口，杜絕多視圖重複掃描
        /// </summary>
        private List<OpeningCacheItem> PreCacheHostOpenings(Document doc)
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

            List<OpeningCacheItem> cacheList = new List<OpeningCacheItem>(2048);

            foreach (Element elem in collector)
            {
                XYZ center = CalculateElementCenter(elem);
                if (center == null) continue;

                BuiltInCategory bic = elem.Category != null ? (BuiltInCategory)elem.Category.Id.Value : BuiltInCategory.INVALID;

                cacheList.Add(new OpeningCacheItem
                {
                    Id = elem.Id,
                    ElementInstance = elem,
                    Category = bic,
                    CenterPoint = center
                });
            }

            return cacheList;
        }

        /// <summary>
        /// 預先建立並激活標籤型別
        /// </summary>
        private Dictionary<BuiltInCategory, FamilySymbol> BuildAndActivateCategoryTagSymbolMap(Document doc)
        {
            Dictionary<BuiltInCategory, FamilySymbol> map = new Dictionary<BuiltInCategory, FamilySymbol>();

            FamilySymbol pipeAccTag = GetTagSymbol(doc, BuiltInCategory.OST_PipeAccessoryTags, "開口標籤")
                                   ?? GetTagSymbol(doc, BuiltInCategory.OST_PipeAccessoryTags, null);
            FamilySymbol ductAccTag = GetTagSymbol(doc, BuiltInCategory.OST_DuctAccessoryTags, "開口標籤")
                                   ?? GetTagSymbol(doc, BuiltInCategory.OST_DuctAccessoryTags, null);
            FamilySymbol trayFitTag = GetTagSymbol(doc, BuiltInCategory.OST_CableTrayFittingTags, "開口標籤")
                                   ?? GetTagSymbol(doc, BuiltInCategory.OST_CableTrayFittingTags, null);
            FamilySymbol genericTag = GetTagSymbol(doc, BuiltInCategory.OST_GenericModelTags, "開口標籤")
                                   ?? GetTagSymbol(doc, BuiltInCategory.OST_GenericModelTags, null);

            List<FamilySymbol> symbolsToActivate = new List<FamilySymbol> { pipeAccTag, ductAccTag, trayFitTag, genericTag };

            using (Transaction t = new Transaction(doc, "預先激活標籤族群"))
            {
                t.Start();
                bool needRegen = false;
                foreach (var sym in symbolsToActivate)
                {
                    if (sym != null && !sym.IsActive)
                    {
                        sym.Activate();
                        needRegen = true;
                    }
                }
                if (needRegen) doc.Regenerate();
                t.Commit();
            }

            if (pipeAccTag != null) map[BuiltInCategory.OST_PipeAccessory] = pipeAccTag;
            if (ductAccTag != null) map[BuiltInCategory.OST_DuctAccessory] = ductAccTag;
            if (trayFitTag != null) map[BuiltInCategory.OST_CableTrayFitting] = trayFitTag;
            if (genericTag != null) map[BuiltInCategory.OST_GenericModel] = genericTag;

            return map;
        }

        /// <summary>
        /// 檢查指定品類在視圖中的 Visibility 狀態 (防止產生幽靈標籤)
        /// </summary>
        private Dictionary<BuiltInCategory, bool> GetCategoryVisibilityMap(ViewPlan view, IEnumerable<BuiltInCategory> categories)
        {
            Dictionary<BuiltInCategory, bool> visibilityMap = new Dictionary<BuiltInCategory, bool>();
            Document doc = view.Document;

            foreach (var bic in categories)
            {
                try
                {
                    Category cat = Category.GetCategory(doc, bic);
                    if (cat != null)
                    {
                        bool isHidden = view.GetCategoryHidden(cat.Id);
                        visibilityMap[bic] = !isHidden;
                    }
                    else
                    {
                        visibilityMap[bic] = true;
                    }
                }
                catch
                {
                    visibilityMap[bic] = true;
                }
            }

            return visibilityMap;
        }

        private bool CalculateViewBoundingLimits(
            ViewPlan view,
            out double validZMin,
            out double validZMax,
            out BoundingBoxXYZ cropBox,
            out Transform invCropTransform)
        {
            cropBox = null;
            invCropTransform = null;

            double exactZMax = GetPlaneElevation(view, PlanViewPlane.TopClipPlane, 1000.0, -1000.0);
            double exactZMin = GetPlaneElevation(view, PlanViewPlane.ViewDepthPlane, 1000.0, -1000.0);

            validZMin = exactZMin - 0.5;
            validZMax = exactZMax + 0.5;

            if (view.CropBoxActive)
            {
                cropBox = view.CropBox;
                if (cropBox != null)
                {
                    invCropTransform = cropBox.Transform.Inverse;
                }
            }

            return true;
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

    #region Failure Processor & Extensions

    public class SilentFailurePreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var failureMessages = failuresAccessor.GetFailureMessages();
            foreach (var msg in failureMessages)
            {
                if (msg.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(msg);
                }
            }
            return FailureProcessingResult.Continue;
        }
    }

    public static class TagExtensions
    {
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