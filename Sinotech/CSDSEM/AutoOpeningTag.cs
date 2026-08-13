using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Sinotech.CSDSEM
{
    [Transaction(TransactionMode.Manual)]
    public class AutoOpeningTag : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            List<ProjectItem> availableProjects = new List<ProjectItem>();
            availableProjects.Add(new ProjectItem(doc));

            FilteredElementCollector linkCollector = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance));

            foreach (RevitLinkInstance linkInst in linkCollector.Cast<RevitLinkInstance>())
            {
                Document linkedDoc = linkInst.GetLinkDocument();
                if (linkedDoc != null)
                {
                    availableProjects.Add(new ProjectItem(linkedDoc, linkInst));
                }
            }

            using (AutoNumberForm autoNumberForm = new AutoNumberForm(doc))
            {
                if (autoNumberForm.ShowDialog() == DialogResult.OK)
                {
                    IList<ElementFilter> openingFilters = new List<ElementFilter>() {
                        new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory) ,
                        new ElementCategoryFilter(BuiltInCategory.OST_DuctAccessory),
                        new ElementCategoryFilter(BuiltInCategory.OST_CableTrayFitting)};
                    LogicalOrFilter logicOrFilter = new LogicalOrFilter(openingFilters);
                    List<ElementId> docOpenings = new FilteredElementCollector(doc).WherePasses(logicOrFilter).WhereElementIsNotElementType().ToElementIds().ToList();

                    try
                    {
                        List<ViewPlan> viewPlans = GetAutoNumberViewPlans(doc, autoNumberForm.viewFamilyTypeName);
                        using (ChooseMultiViewPlansForm chooseMultiViewPlansForm = new ChooseMultiViewPlansForm(doc, viewPlans, ChooseMultiViewPlansForm.FormMode.AutoPipeTag))
                        {
                            if (chooseMultiViewPlansForm.ShowDialog() == DialogResult.OK)
                            {
                                DateTime timeStart = DateTime.Now;
                                int newTagCounts = 0;
                                List<ElementId> createdTagIds = new List<ElementId>();
                                List<ViewPlan> checkViewPlans = chooseMultiViewPlansForm.checkViewPlans;

                                if (checkViewPlans.Count > 0)
                                {
                                    // 進度條視窗
                                    ProgressForm progressForm = new ProgressForm("自動開口標籤", checkViewPlans.Count);
                                    progressForm.Show();

                                    HashSet<ElementId> openedParentViewIds = new HashSet<ElementId>();
                                    foreach (ViewPlan checkViewPlan in checkViewPlans)
                                    {
                                        ElementId primaryViewId = checkViewPlan.GetPrimaryViewId();
                                        if (primaryViewId != null
                                            && primaryViewId != ElementId.InvalidElementId
                                            && !openedParentViewIds.Contains(primaryViewId))
                                        {
                                            ViewPlan parentView = doc.GetElement(primaryViewId) as ViewPlan;
                                            if (parentView != null)
                                            {
                                                try
                                                {
                                                    uidoc.RequestViewChange(parentView);
                                                    openedParentViewIds.Add(primaryViewId);
                                                }
                                                catch { }
                                            }
                                        }
                                    }

                                    foreach (ElementId primaryViewId in openedParentViewIds)
                                    {
                                        ViewPlan parentView = doc.GetElement(primaryViewId) as ViewPlan;
                                        if (parentView != null)
                                        {
                                            try
                                            {
                                                uidoc.RequestViewChange(parentView);
                                                Application.DoEvents();
                                                using (Transaction t = new Transaction(doc, "開口標籤"))
                                                {
                                                    t.Start();

                                                    List<ViewPlan> sameParentViewId = checkViewPlans.Where(x => x.GetPrimaryViewId().Equals(primaryViewId)).ToList();

                                                    foreach (ViewPlan checkViewPlan in sameParentViewId)
                                                    {
                                                        progressForm.UpdateProgress(checkViewPlan.Name);
                                                        Application.DoEvents();
                                                        double exactZMax = GetPlaneElevation(checkViewPlan, PlanViewPlane.TopClipPlane, 1000.0, -1000.0);
                                                        double exactZMin = GetPlaneElevation(checkViewPlan, PlanViewPlane.ViewDepthPlane, 1000.0, -1000.0);
                                                        double defaultCutZ = (exactZMax + exactZMin) / 2.0;
                                                        double exactCutZ = GetPlaneElevation(checkViewPlan, PlanViewPlane.CutPlane, defaultCutZ, defaultCutZ);

                                                        double validZ_Min = exactZMin - 0.5;
                                                        double validZ_Max = exactZMax + 0.5;

                                                        double viewMinX, viewMinY, viewMaxX, viewMaxY;
                                                        {
                                                            BoundingBoxXYZ cb = checkViewPlan.CropBox;
                                                            Transform ct = cb.Transform;
                                                            if (checkViewPlan.CropBoxActive)
                                                            {
                                                                viewMinX = double.MaxValue; viewMinY = double.MaxValue;
                                                                viewMaxX = double.MinValue; viewMaxY = double.MinValue;
                                                                foreach (double lx in new[] { cb.Min.X, cb.Max.X })
                                                                    foreach (double ly in new[] { cb.Min.Y, cb.Max.Y })
                                                                    {
                                                                        XYZ wp = ct.OfPoint(new XYZ(lx, ly, 0));
                                                                        if (wp.X < viewMinX) viewMinX = wp.X;
                                                                        if (wp.Y < viewMinY) viewMinY = wp.Y;
                                                                        if (wp.X > viewMaxX) viewMaxX = wp.X;
                                                                        if (wp.Y > viewMaxY) viewMaxY = wp.Y;
                                                                    }
                                                            }
                                                            else
                                                            {
                                                                viewMinX = double.MinValue / 2; viewMinY = double.MinValue / 2;
                                                                viewMaxX = double.MaxValue / 2; viewMaxY = double.MaxValue / 2;
                                                            }
                                                        }

                                                        BoundingBoxXYZ viewBBox = checkViewPlan.CropBox;
                                                    }
                                                    t.Commit();
                                                }
                                            }
                                            catch { }
                                        }
                                    }

                                    progressForm.Close();
                                    progressForm.Dispose();

                                    DateTime timeEnd = DateTime.Now;
                                    TimeSpan totalTime = timeEnd - timeStart;

                                    if (newTagCounts > 0)
                                    {
                                        // 【修改】使用自訂的 TaskDialog 來提供匯出選項
                                        TaskDialog td = new TaskDialog("自動開口標籤完成");
                                        td.MainInstruction = $"已產生 {newTagCounts} 個開口標籤！\n耗時：{totalTime.Minutes} 分 {totalTime.Seconds} 秒。";
                                        td.MainContent = "視圖裁切線外的標籤可能會因為無法顯示而導致未來重複建立。\n您可以將本次建立的標籤 ID 匯出成文字檔，以便後續透過「依 ID 選取」來檢查它們的位置。";

                                        // 加入匯出按鈕
                                        td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "匯出標籤 ID 文字檔 (.txt)");
                                        td.CommonButtons = TaskDialogCommonButtons.Close;
                                        td.DefaultButton = TaskDialogResult.Close;

                                        TaskDialogResult tdResult = td.Show();

                                        // 若使用者點擊匯出按鈕
                                        if (tdResult == TaskDialogResult.CommandLink1)
                                        {
                                            using (SaveFileDialog sfd = new SaveFileDialog())
                                            {
                                                sfd.Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
                                                sfd.Title = "儲存標籤 ID 清單";
                                                // 預設檔名加上當下時間避免覆蓋
                                                sfd.FileName = $"新增標籤ID_{DateTime.Now:yyyyMMdd_HHmmss}.txt";

                                                if (sfd.ShowDialog() == DialogResult.OK)
                                                {
                                                    StringBuilder sb = new StringBuilder();
                                                    sb.AppendLine("=== 自動產生的管線標籤 ID 清單 ===");
                                                    sb.AppendLine($"產生時間: {DateTime.Now}");
                                                    sb.AppendLine($"總數量: {newTagCounts}");
                                                    sb.AppendLine("-----------------------------------");

                                                    foreach (ElementId id in createdTagIds)
                                                    {
                                                        // 根據 Revit 版本，通常使用 id.Value.ToString() (Revit 2024+) 或 id.IntegerValue.ToString() (舊版)
                                                        // 若您的環境是新版 API，建議使用 id.Value.ToString()；若是舊版則改為 id.IntegerValue
                                                        sb.AppendLine(id.Value.ToString());
                                                    }

                                                    try
                                                    {
                                                        File.WriteAllText(sfd.FileName, sb.ToString());
                                                        //TaskDialog.Show("匯出成功", $"已成功匯出至：\n{sfd.FileName}");
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        TaskDialog.Show("匯出失敗", $"儲存檔案時發生錯誤：\n{ex.Message}");
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        TaskDialog.Show("Revit", "沒有產生新管線標籤！");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }

                    return Result.Succeeded;
                }
            }

            return Result.Cancelled;
        }

        public static List<ViewPlan> GetAutoNumberViewPlans(Document doc, string viewFamilyTypeName)
        {
            string familyName = viewFamilyTypeName.Split(' ')[0];
            string name = viewFamilyTypeName.Split(' ')[1].Substring(1, viewFamilyTypeName.Split(' ')[1].Length - 2);
            List<ViewFamilyType> viewFamilyTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .Where(x => x.ViewFamily == ViewFamily.FloorPlan || x.ViewFamily == ViewFamily.CeilingPlan)
                .Where(x => x.Name.Contains("1/100"))
                .OrderBy(x => x.ViewFamily).ThenBy(x => x.Name).ToList();
            ViewFamilyType viewFamilyType = viewFamilyTypes.Where(x => x.FamilyName.Equals(familyName) && x.Name.Equals(name)).FirstOrDefault();
            List<ViewPlan> viewPlans = new List<ViewPlan>();
            viewPlans = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .WhereElementIsNotElementType()
                .Where(x => x.GetTypeId().Equals(viewFamilyType.Id))
                .Cast<ViewPlan>()
                .Where(x => x.GenLevel != null)
                .Where(v => v.LookupParameter("圖面分類") != null && v.LookupParameter("圖面分類").AsString() == "出圖")
                .Where(x => x.GetDependentViewIds().Count.Equals(0))
                .OrderBy(x => x.GenLevel.Elevation).ToList();
            return viewPlans;
        }

        private double GetPlaneElevation(ViewPlan view, PlanViewPlane plane, double defaultHigh, double defaultLow)
        {
            PlanViewRange viewRange = view.GetViewRange();
            ElementId levelId = viewRange.GetLevelId(plane);
            double offset = viewRange.GetOffset(plane);

            if (levelId == ElementId.InvalidElementId)
                return plane == PlanViewPlane.TopClipPlane ? defaultHigh : defaultLow;

            if (levelId.Value < 0)
            {
                long specialId = levelId.Value;
                if (specialId == -5) return plane == PlanViewPlane.TopClipPlane ? defaultHigh : defaultLow;
                if (specialId == -2) return (view.GenLevel != null ? view.GenLevel.Elevation : 0) + offset;
                if (specialId == -4) return defaultHigh;
                if (specialId == -3) return defaultLow;
            }

            Element elem = view.Document.GetElement(levelId);
            if (elem is Level lvl) return lvl.Elevation + offset;

            return (view.GenLevel != null ? view.GenLevel.Elevation : 0) + offset;
        }
    }
}
