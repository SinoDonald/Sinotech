using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using AutoSign.AutoNumber;
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
                    List<Type> mepTypes = new List<Type> { typeof(Pipe), typeof(Duct), typeof(CableTray) };
                    ElementMulticlassFilter multiFilter = new ElementMulticlassFilter(mepTypes);

                    AutoNumberForm autoNumberForm = new AutoNumberForm(doc);
                    autoNumberForm.ShowDialog();
                    if (autoNumberForm.trueOrFalse == true)
                    {
                        try
                        {
                            List<ViewPlan> viewPlans = GetAutoNumberViewPlans(doc, autoNumberForm.viewFamilyTypeName);
                            using (ChooseMultiViewPlansForm chooseMultiViewPlansForm = new ChooseMultiViewPlansForm(doc, viewPlans))
                            {
                                if (chooseMultiViewPlansForm.ShowDialog() == DialogResult.OK)
                                {
                                    DateTime timeStart = DateTime.Now;
                                    int newTagCounts = 0;
                                    List<ViewPlan> checkViewPlans = chooseMultiViewPlansForm.checkViewPlans;

                                    using (Transaction t = new Transaction(doc, "自動建立管線標籤"))
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

                                        foreach (ViewPlan checkViewPlan in checkViewPlans)
                                        {
                                            double exactZMax = GetPlaneElevation(checkViewPlan, PlanViewPlane.TopClipPlane, 1000.0, -1000.0);
                                            double exactZMin = GetPlaneElevation(checkViewPlan, PlanViewPlane.ViewDepthPlane, 1000.0, -1000.0);
                                            double defaultCutZ = (exactZMax + exactZMin) / 2.0;
                                            double exactCutZ = GetPlaneElevation(checkViewPlan, PlanViewPlane.CutPlane, defaultCutZ, defaultCutZ);

                                            double validZ_Min = exactZMin - 0.5;
                                            double validZ_Max = exactZMax + 0.5;

                                            // 防重複機制：元件ID紀錄 (避免重複標註同一根管)
                                            HashSet<string> alreadyTaggedSignatures = new HashSet<string>();

                                            // 【新增】內容防重複機制：記錄視圖中已存在的「管線內容特徵值」
                                            HashSet<string> taggedContentSignatures = new HashSet<string>();

                                            FilteredElementCollector existingTags = new FilteredElementCollector(doc, checkViewPlan.Id)
                                                .OfClass(typeof(IndependentTag));

                                            foreach (IndependentTag tag in existingTags.Cast<IndependentTag>())
                                            {
                                                try
                                                {
                                                    // 判斷是否為「管底_尺寸+系統」標籤
                                                    bool isPipeTag = false;
                                                    FamilySymbol sym = doc.GetElement(tag.GetTypeId()) as FamilySymbol;
                                                    if (sym != null && (sym.FamilyName.Contains("管底_尺寸") || sym.Name.Contains("管底_尺寸")))
                                                    {
                                                        isPipeTag = true;
                                                    }

                                                    foreach (Reference tagRef in tag.GetTaggedReferences())
                                                    {
                                                        Element taggedElem = null;
                                                        if (tagRef.LinkedElementId != ElementId.InvalidElementId)
                                                        {
                                                            alreadyTaggedSignatures.Add($"Linked_{tagRef.ElementId}_{tagRef.LinkedElementId}");

                                                            // 取得連結模型中的實體元件，用於萃取特徵值
                                                            RevitLinkInstance linkInst = doc.GetElement(tagRef.ElementId) as RevitLinkInstance;
                                                            if (linkInst != null && isPipeTag)
                                                            {
                                                                taggedElem = linkInst.GetLinkDocument()?.GetElement(tagRef.LinkedElementId);
                                                            }
                                                        }
                                                        else
                                                        {
                                                            alreadyTaggedSignatures.Add($"Local_{tagRef.ElementId}");
                                                            if (isPipeTag) taggedElem = doc.GetElement(tagRef.ElementId);
                                                        }

                                                        // 【新增】如果畫面上已經有這個管線的標籤，記錄它的「內容特徵值」
                                                        if (taggedElem != null && taggedElem is Pipe)
                                                        {
                                                            string contentSig = GetPipeContentSignature(taggedElem);
                                                            if (contentSig != null) taggedContentSignatures.Add(contentSig);
                                                        }
                                                    }
                                                }
                                                catch { }
                                            }

                                            List<TargetMepElement> validMepInThisView = new List<TargetMepElement>();

                                            ProjectItem mainProj = form.SelectedProjects.FirstOrDefault(p => p.IsMainModel);
                                            if (mainProj != null)
                                            {
                                                FilteredElementCollector mainCollector = new FilteredElementCollector(doc, checkViewPlan.Id)
                                                    .WherePasses(multiFilter)
                                                    .WhereElementIsNotElementType();

                                                foreach (Element elem in mainCollector)
                                                {
                                                    validMepInThisView.Add(new TargetMepElement { MepElement = elem, SourceProject = mainProj });
                                                }
                                            }

                                            BoundingBoxXYZ viewBBox = checkViewPlan.CropBox;
                                            foreach (ProjectItem linkedProj in form.SelectedProjects.Where(p => !p.IsMainModel))
                                            {
                                                Transform invTransform = linkedProj.LinkInstance.GetTotalTransform().Inverse;
                                                Outline linkOutline = GetTransformedOutline(checkViewPlan, viewBBox, invTransform, validZ_Min, validZ_Max);

                                                BoundingBoxIntersectsFilter bboxFilter = new BoundingBoxIntersectsFilter(linkOutline);

                                                FilteredElementCollector linkedMepCollector = new FilteredElementCollector(linkedProj.Doc)
                                                    .WherePasses(multiFilter)
                                                    .WherePasses(bboxFilter)
                                                    .WhereElementIsNotElementType();

                                                foreach (Element elem in linkedMepCollector)
                                                {
                                                    validMepInThisView.Add(new TargetMepElement { MepElement = elem, SourceProject = linkedProj });
                                                }
                                            }

                                            if (validMepInThisView.Count == 0) continue;

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

                                                XYZ pt0 = null, pt1 = null;
                                                if (elem.Location is LocationCurve locCurve && locCurve.Curve != null)
                                                {
                                                    pt0 = locCurve.Curve.GetEndPoint(0);
                                                    pt1 = locCurve.Curve.GetEndPoint(1);

                                                    if (!mepItem.SourceProject.IsMainModel)
                                                    {
                                                        Transform linkTransform = mepItem.SourceProject.LinkInstance.GetTotalTransform();
                                                        pt0 = linkTransform.OfPoint(pt0);
                                                        pt1 = linkTransform.OfPoint(pt1);
                                                    }
                                                }

                                                if (pt0 == null || pt1 == null) continue;

                                                double minZ = Math.Min(pt0.Z, pt1.Z);
                                                double maxZ = Math.Max(pt0.Z, pt1.Z);

                                                if (maxZ < validZ_Min || minZ > validZ_Max)
                                                {
                                                    continue;
                                                }

                                                Reference pipeRef = mepItem.SourceProject.IsMainModel
                                                    ? new Reference(elem)
                                                    : new Reference(elem).CreateLinkReference(mepItem.SourceProject.LinkInstance);

                                                XYZ trueMid = (pt0 + pt1) / 2.0;
                                                double tagZ = trueMid.Z;

                                                if (tagZ > exactZMax) tagZ = exactZMax - 0.01;
                                                if (tagZ < exactZMin) tagZ = exactZMin + 0.01;

                                                XYZ tagPlacementPoint = new XYZ(trueMid.X, trueMid.Y, tagZ);

                                                // =========================================================
                                                // 【關鍵新增：同視圖內相同內容標籤過濾】
                                                // =========================================================
                                                string contentSig = null;
                                                if (elem is Pipe && targetSymbol.Id == pipeTagSym?.Id)
                                                {
                                                    contentSig = GetPipeContentSignature(elem);
                                                    if (contentSig != null && taggedContentSignatures.Contains(contentSig))
                                                    {
                                                        // 若這個視圖已經有相同 [管徑+系統+高程] 的管線被標註過，則跳過此管！
                                                        continue;
                                                    }
                                                }

                                                try
                                                {
                                                    IndependentTag newTag = IndependentTag.Create(
                                                        doc,
                                                        checkViewPlan.Id,
                                                        pipeRef,
                                                        true,
                                                        TagMode.TM_ADDBY_CATEGORY,
                                                        TagOrientation.Horizontal,
                                                        tagPlacementPoint
                                                    );

                                                    if (newTag != null)
                                                    {
                                                        newTag.ChangeTypeId(targetSymbol.Id);
                                                        newTagCounts++;

                                                        // 【新增】標籤建立成功後，將該管線的特徵值存入 HashSet
                                                        // 這樣視圖中下一根平行且內容一模一樣的水管，就會在上方被過濾掉
                                                        if (contentSig != null)
                                                        {
                                                            taggedContentSignatures.Add(contentSig);
                                                        }
                                                    }
                                                }
                                                catch (Autodesk.Revit.Exceptions.ArgumentException) { }
                                            }
                                        }

                                        t.Commit();
                                    }
                                    DateTime timeEnd = DateTime.Now;
                                    TimeSpan totalTime = timeEnd - timeStart;
                                    if (newTagCounts > 0)
                                    {
                                        TaskDialog.Show("Revit", $"已產生 {newTagCounts} 個管線標籤！\n\n耗時：{totalTime.Minutes} 分 {totalTime.Seconds} 秒。");
                                    }
                                    else
                                    {
                                        TaskDialog.Show("Revit", "沒有產生新管線標籤！");
                                    }
                                }
                            }
                        }
                        catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                    }

                    return Result.Succeeded;
                }
            }

            return Result.Cancelled;
        }

        /// <summary>
        /// 【新增輔助方法】取得管線的「內容特徵值」，用於精準去重複
        /// </summary>
        private string GetPipeContentSignature(Element elem)
        {
            if (!(elem is Pipe)) return null;

            // 1. 取得管徑 (Size)
            string size = elem.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsValueString() ?? "";

            // 2. 取得系統縮寫 (System Abbreviation)
            // 【修正】Revit API 正確的系統縮寫參數為 RBS_SYSTEM_ABBREVIATION_PARAM
            string system = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_ABBREVIATION_PARAM)?.AsString() ?? "";
            if (string.IsNullOrEmpty(system)) // 如果沒填縮寫，退而求其次抓系統名稱
            {
                ElementId sysId = elem.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId();
                if (sysId != null && sysId != ElementId.InvalidElementId)
                {
                    Element sysElem = elem.Document.GetElement(sysId);
                    if (sysElem != null) system = sysElem.Name;
                }
            }

            // 3. 取得管底高程 (Bottom Elevation)
            // 【修正】Revit API 正確的管底高程參數為 RBS_PIPE_BOTTOM_ELEVATION
            double bottomElev = 0;
            Parameter bopParam = elem.get_Parameter(BuiltInParameter.RBS_PIPE_BOTTOM_ELEVATION);
            if (bopParam != null) bottomElev = bopParam.AsDouble();

            // 結合成唯一簽名。高程取小數點後三位(精確到約 0.3mm)，可避免 Revit 浮點數些微誤差導致的誤判。
            return $"PipeContent_{size}_{system}_{Math.Round(bottomElev, 3)}";
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

        private FamilySymbol GetTagSymbol(Document doc, BuiltInCategory tagCategory, string familyName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(tagCategory)
                .Cast<FamilySymbol>()
                .FirstOrDefault(x => x.FamilyName == familyName || x.Name == familyName);
        }

        private double GetPlaneElevation(ViewPlan view, PlanViewPlane plane, double defaultHigh, double defaultLow)
        {
            PlanViewRange viewRange = view.GetViewRange();
            ElementId levelId = viewRange.GetLevelId(plane);
            double offset = viewRange.GetOffset(plane);

            if (levelId == ElementId.InvalidElementId)
                return plane == PlanViewPlane.TopClipPlane ? defaultHigh : defaultLow;

            if (levelId.IntegerValue < 0)
            {
                int specialId = levelId.IntegerValue;
                if (specialId == -5) return plane == PlanViewPlane.TopClipPlane ? defaultHigh : defaultLow; // Unlimited
                if (specialId == -2) return (view.GenLevel != null ? view.GenLevel.Elevation : 0) + offset; // Current Level
                if (specialId == -4) return defaultHigh; // Level Above
                if (specialId == -3) return defaultLow;  // Level Below
            }

            Element elem = view.Document.GetElement(levelId);
            if (elem is Level lvl) return lvl.Elevation + offset;

            return (view.GenLevel != null ? view.GenLevel.Elevation : 0) + offset;
        }

        private Outline GetTransformedOutline(ViewPlan view, BoundingBoxXYZ viewBBox, Transform hostToLinkTransform, double hostZMin, double hostZMax)
        {
            Transform viewToHostTransform = viewBBox.Transform;

            double lMinX, lMinY, lMaxX, lMaxY;

            if (view.CropBoxActive)
            {
                lMinX = viewBBox.Min.X;
                lMinY = viewBBox.Min.Y;
                lMaxX = viewBBox.Max.X;
                lMaxY = viewBBox.Max.Y;
            }
            else
            {
                lMinX = -100000.0;
                lMinY = -100000.0;
                lMaxX = 100000.0;
                lMaxY = 100000.0;
            }

            XYZ[] hostCorners = new XYZ[4] {
                viewToHostTransform.OfPoint(new XYZ(lMinX, lMinY, 0)),
                viewToHostTransform.OfPoint(new XYZ(lMaxX, lMinY, 0)),
                viewToHostTransform.OfPoint(new XYZ(lMinX, lMaxY, 0)),
                viewToHostTransform.OfPoint(new XYZ(lMaxX, lMaxY, 0))
            };

            List<XYZ> worldPoints = new List<XYZ>();
            foreach (XYZ pt in hostCorners)
            {
                worldPoints.Add(new XYZ(pt.X, pt.Y, hostZMin));
                worldPoints.Add(new XYZ(pt.X, pt.Y, hostZMax));
            }

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            foreach (XYZ pt in worldPoints)
            {
                XYZ linkPt = hostToLinkTransform.OfPoint(pt);

                if (linkPt.X < minX) minX = linkPt.X;
                if (linkPt.Y < minY) minY = linkPt.Y;
                if (linkPt.Z < minZ) minZ = linkPt.Z;
                if (linkPt.X > maxX) maxX = linkPt.X;
                if (linkPt.Y > maxY) maxY = linkPt.Y;
                if (linkPt.Z > maxZ) maxZ = linkPt.Z;
            }

            double bufferXY = 5.0;
            double bufferZ = 1.0;

            return new Outline(
                new XYZ(minX - bufferXY, minY - bufferXY, minZ - bufferZ),
                new XYZ(maxX + bufferXY, maxY + bufferXY, maxZ + bufferZ)
            );
        }
    }

    // ... ProjectItem 與 TargetMepElement 類別維持原樣不變
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