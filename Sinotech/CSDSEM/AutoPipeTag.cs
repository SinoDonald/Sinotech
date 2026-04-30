using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using View = Autodesk.Revit.DB.View;

namespace Sinotech.CSDSEM
{
    [Transaction(TransactionMode.Manual)]
    public class AutoPipeTag : IExternalCommand
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

            using (LinkSelectionForm form = new LinkSelectionForm(availableProjects))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    // 【效能優化】刪除了原本在這裡全域撈取 allMepElements 的動作
                    // 定義我們想要抓取的管道類型 (供後續使用)
                    List<Type> mepTypes = new List<Type> { typeof(Pipe), typeof(Duct), typeof(CableTray) };
                    ElementMulticlassFilter multiFilter = new ElementMulticlassFilter(mepTypes);

                    // ==========================================
                    // 步驟 A：收集主模型中的 2D 視圖，並依據專案瀏覽器架構分類與高程排序
                    // ==========================================
                    FilteredElementCollector viewCollector = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewPlan))
                        .WhereElementIsNotElementType();

                    var browserTree = new Dictionary<string, Dictionary<string, List<View>>>();

                    foreach (ViewPlan view in viewCollector.Cast<ViewPlan>())
                    {
                        if (view.IsTemplate) continue;

                        Parameter folderParam = view.LookupParameter("視圖分類");
                        string topFolder = (folderParam != null && folderParam.HasValue) ? folderParam.AsString() : "???";

                        string subFolder = "未分類平面圖";
                        ElementId typeId = view.GetTypeId();
                        if (typeId != ElementId.InvalidElementId)
                        {
                            Element viewType = doc.GetElement(typeId);
                            if (viewType != null) subFolder = viewType.Name;
                        }

                        if (!browserTree.ContainsKey(topFolder))
                            browserTree[topFolder] = new Dictionary<string, List<View>>();

                        if (!browserTree[topFolder].ContainsKey(subFolder))
                            browserTree[topFolder][subFolder] = new List<View>();

                        browserTree[topFolder][subFolder].Add(view);
                    }

                    var sortedGroupedViews = new Dictionary<string, Dictionary<string, List<View>>>();

                    foreach (var topLevel in browserTree)
                    {
                        sortedGroupedViews[topLevel.Key] = new Dictionary<string, List<View>>();
                        foreach (var subLevel in topLevel.Value)
                        {
                            var sortedViews = subLevel.Value
                                .OrderBy(v => v.GenLevel != null ? v.GenLevel.Elevation : 0.0)
                                .ThenBy(v => v.Name)
                                .ToList();

                            sortedGroupedViews[topLevel.Key][subLevel.Key] = sortedViews;
                        }
                    }

                    // ==========================================
                    // 步驟 B & C：呼叫第二個視窗讓使用者選擇視圖，並執行標註
                    // ==========================================
                    using (ViewSelectionForm viewForm = new ViewSelectionForm(sortedGroupedViews))
                    {
                        if (viewForm.ShowDialog() == DialogResult.OK)
                        {
                            DateTime timeStart = DateTime.Now;
                            int newTagCounts = 0;
                            List<View> targetViews = viewForm.SelectedViews;

                            using (Transaction t = new Transaction(doc, "批次建立管線標籤"))
                            {
                                t.Start();

                                FamilySymbol pipeTagSym = GetTagSymbol(doc, BuiltInCategory.OST_PipeTags, "管底_尺寸+系統");
                                FamilySymbol ductTagSym = GetTagSymbol(doc, BuiltInCategory.OST_DuctTags, "管道標籤_寬高_高程");
                                FamilySymbol trayTagSym = GetTagSymbol(doc, BuiltInCategory.OST_CableTrayTags, "MRT_電纜托盤編號標籤");

                                if (pipeTagSym != null && !pipeTagSym.IsActive) pipeTagSym.Activate();
                                if (ductTagSym != null && !ductTagSym.IsActive) ductTagSym.Activate();
                                if (trayTagSym != null && !trayTagSym.IsActive) trayTagSym.Activate();

                                if (pipeTagSym == null && ductTagSym == null && trayTagSym == null)
                                {
                                    TaskDialog.Show("警告", "找不到指定的標籤族群，請確認是否已載入專案！");
                                    t.RollBack();
                                    return Result.Failed;
                                }

                                // 開始針對每個勾選的視圖進行處理
                                foreach (View targetView in targetViews)
                                {
                                    // 1. 防重複機制：收集這個視圖中「已經存在的標籤」
                                    HashSet<string> alreadyTaggedSignatures = new HashSet<string>();
                                    FilteredElementCollector existingTags = new FilteredElementCollector(doc, targetView.Id)
                                        .OfClass(typeof(IndependentTag));

                                    foreach (IndependentTag tag in existingTags.Cast<IndependentTag>())
                                    {
                                        try
                                        {
                                            foreach (Reference tagRef in tag.GetTaggedReferences())
                                            {
                                                if (tagRef.LinkedElementId != ElementId.InvalidElementId)
                                                    alreadyTaggedSignatures.Add($"Linked_{tagRef.ElementId}_{tagRef.LinkedElementId}");
                                                else
                                                    alreadyTaggedSignatures.Add($"Local_{tagRef.ElementId}");
                                            }
                                        }
                                        catch { /* 忽略孤立標籤 */ }
                                    }

                                    // =========================================================
                                    // 【核心優化：視圖專屬的極速管線收集器】
                                    // =========================================================
                                    List<TargetMepElement> validMepInThisView = new List<TargetMepElement>();

                                    // A. 收集主模型中【確實可見】的管線 (C++ 層級過濾，極快)
                                    ProjectItem mainProj = form.SelectedProjects.FirstOrDefault(p => p.IsMainModel);
                                    if (mainProj != null)
                                    {
                                        FilteredElementCollector mainCollector = new FilteredElementCollector(doc, targetView.Id)
                                            .WherePasses(multiFilter)
                                            .WhereElementIsNotElementType();

                                        foreach (Element elem in mainCollector)
                                        {
                                            validMepInThisView.Add(new TargetMepElement { MepElement = elem, SourceProject = mainProj });
                                        }
                                    }

                                    // B. 收集連結模型中的管線 (使用空間過濾器粗篩，再精細檢查)
                                    BoundingBoxXYZ viewBBox = targetView.CropBox;
                                    foreach (ProjectItem linkedProj in form.SelectedProjects.Where(p => !p.IsMainModel))
                                    {
                                        // 將視圖的 BoundingBox 轉換為連結模型的座標系，並取得外框 (Outline)
                                        Transform invTransform = linkedProj.LinkInstance.GetTotalTransform().Inverse;
                                        Outline linkOutline = GetTransformedOutline(viewBBox, invTransform);

                                        // 建立空間過濾器 (粗篩)
                                        BoundingBoxIntersectsFilter bboxFilter = new BoundingBoxIntersectsFilter(linkOutline);

                                        FilteredElementCollector linkedMepCollector = new FilteredElementCollector(linkedProj.Doc)
                                            .WherePasses(multiFilter)
                                            .WherePasses(bboxFilter) // <--- 這行是加速千萬倍的關鍵
                                            .WhereElementIsNotElementType();

                                        foreach (Element elem in linkedMepCollector)
                                        {
                                            // 精細檢查：確保該元件在該視圖中可見 (現在只檢查幾十個，而不是幾萬個)
                                            BoundingBoxXYZ bboxInView = elem.get_BoundingBox(targetView);
                                            if (bboxInView != null)
                                            {
                                                validMepInThisView.Add(new TargetMepElement { MepElement = elem, SourceProject = linkedProj });
                                            }
                                        }
                                    }

                                    if (validMepInThisView.Count == 0) continue;

                                    // =========================================================
                                    // 執行標註
                                    // =========================================================
                                    foreach (TargetMepElement mepItem in validMepInThisView)
                                    {
                                        string currentSig = mepItem.SourceProject.IsMainModel
                                            ? $"Local_{mepItem.MepElement.Id}"
                                            : $"Linked_{mepItem.SourceProject.LinkInstance.Id}_{mepItem.MepElement.Id}";

                                        if (alreadyTaggedSignatures.Contains(currentSig)) continue;

                                        Element elem = mepItem.MepElement;
                                        FamilySymbol targetSymbol = null;

                                        if (elem is Pipe && pipeTagSym != null) targetSymbol = pipeTagSym;
                                        else if (elem is Duct && ductTagSym != null) targetSymbol = ductTagSym;
                                        else if (elem is CableTray && trayTagSym != null) targetSymbol = trayTagSym;

                                        if (targetSymbol == null) continue;

                                        Reference pipeRef = null;
                                        XYZ midPoint = null;

                                        if (mepItem.SourceProject.IsMainModel)
                                        {
                                            pipeRef = new Reference(elem);
                                            midPoint = GetCurveMidPoint(elem);
                                        }
                                        else
                                        {
                                            pipeRef = new Reference(elem).CreateLinkReference(mepItem.SourceProject.LinkInstance);
                                            Transform linkTransform = mepItem.SourceProject.LinkInstance.GetTotalTransform();
                                            XYZ localMidPoint = GetCurveMidPoint(elem);
                                            if (localMidPoint != null) midPoint = linkTransform.OfPoint(localMidPoint);
                                        }

                                        if (midPoint == null) continue;

                                        IndependentTag newTag = IndependentTag.Create(
                                            doc,
                                            targetView.Id,
                                            pipeRef,
                                            true, // 不加引線 (addLeader = false) -> 注意: true 代表加引線
                                            TagMode.TM_ADDBY_CATEGORY,
                                            TagOrientation.Horizontal,
                                            midPoint
                                        );

                                        if (newTag != null)
                                        {
                                            newTag.ChangeTypeId(targetSymbol.Id);
                                            newTagCounts++;
                                        }
                                    }
                                }

                                t.Commit();
                            }
                            DateTime timeEnd = DateTime.Now;
                            TimeSpan totalTime = timeEnd - timeStart;
                            TaskDialog.Show("Revit", $"已產生 {newTagCounts} 個管線標籤！\n\n耗時：{totalTime.Minutes} 分 {totalTime.Seconds} 秒。");
                        }
                    }

                    return Result.Succeeded;
                }
            }

            return Result.Cancelled;
        }

        /// <summary>
        /// 取得指定的標籤族群類型 (FamilySymbol)
        /// </summary>
        private FamilySymbol GetTagSymbol(Document doc, BuiltInCategory tagCategory, string familyName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(tagCategory)
                .Cast<FamilySymbol>()
                .FirstOrDefault(x => x.FamilyName == familyName || x.Name == familyName);
        }

        /// <summary>
        /// 取得管線元件的中心點 (0.5 參數點)
        /// </summary>
        private XYZ GetCurveMidPoint(Element elem)
        {
            if (elem.Location is LocationCurve locCurve && locCurve.Curve != null)
            {
                return locCurve.Curve.Evaluate(0.5, true);
            }
            BoundingBoxXYZ bbox = elem.get_BoundingBox(null);
            if (bbox != null)
            {
                return (bbox.Min + bbox.Max) / 2.0;
            }
            return null;
        }

        /// <summary>
        /// 【新增】計算轉換座標後的包圍盒 (Outline)，用於連結模型空間粗篩
        /// </summary>
        private Outline GetTransformedOutline(BoundingBoxXYZ bbox, Transform transform)
        {
            XYZ[] corners = new XYZ[8];
            corners[0] = new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Min.Z);
            corners[1] = new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Min.Z);
            corners[2] = new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Min.Z);
            corners[3] = new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Min.Z);
            corners[4] = new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Max.Z);
            corners[5] = new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Max.Z);
            corners[6] = new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Max.Z);
            corners[7] = new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Max.Z);

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            foreach (XYZ corner in corners)
            {
                XYZ pt = transform.OfPoint(corner);
                if (pt.X < minX) minX = pt.X;
                if (pt.Y < minY) minY = pt.Y;
                if (pt.Z < minZ) minZ = pt.Z;
                if (pt.X > maxX) maxX = pt.X;
                if (pt.Y > maxY) maxY = pt.Y;
                if (pt.Z > maxZ) maxZ = pt.Z;
            }

            // 【防呆設計】給予 1.0 英呎的容差值，避免 2D 平面圖的 Z 軸厚度為 0 導致 API 報錯
            double buffer = 1.0;
            return new Outline(
                new XYZ(minX - buffer, minY - buffer, minZ - buffer),
                new XYZ(maxX + buffer, maxY + buffer, maxZ + buffer)
            );
        }
    }

    public class ProjectItem
    {
        public Document Doc { get; set; }
        public RevitLinkInstance LinkInstance { get; set; }
        public string DisplayName { get; set; }
        public bool IsMainModel { get; set; }

        public ProjectItem(Document doc)
        {
            Doc = doc;
            LinkInstance = null;
            IsMainModel = true;
            DisplayName = $"[主模型] {doc.Title}";
        }

        public ProjectItem(Document doc, RevitLinkInstance linkInstance)
        {
            Doc = doc;
            LinkInstance = linkInstance;
            IsMainModel = false;
            DisplayName = $"[連結] {doc.Title}";
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    public class TargetMepElement
    {
        public Element MepElement { get; set; }
        public ProjectItem SourceProject { get; set; }

        public string CategoryName
        {
            get
            {
                if (MepElement is Pipe) return "水管 (Pipe)";
                if (MepElement is Duct) return "風管 (Duct)";
                if (MepElement is CableTray) return "電纜架 (CableTray)";
                return "未知類型";
            }
        }
    }
}