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
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

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

                                            // =========================================================
                                            // 【系統級防重複機制】：記錄視圖中已標註過的「系統 ID」
                                            // =========================================================
                                            HashSet<string> alreadyTaggedSignatures = new HashSet<string>(); // 紀錄實體 ID
                                            HashSet<string> taggedSystemSignatures = new HashSet<string>();  // 紀錄系統 ID

                                            FilteredElementCollector existingTags = new FilteredElementCollector(doc, checkViewPlan.Id)
                                                .OfClass(typeof(IndependentTag));

                                            foreach (IndependentTag tag in existingTags.Cast<IndependentTag>())
                                            {
                                                try
                                                {
                                                    bool isTargetTag = false;
                                                    FamilySymbol sym = doc.GetElement(tag.GetTypeId()) as FamilySymbol;
                                                    if (sym != null && (
                                                        sym.FamilyName.Contains("管底_尺寸") || sym.Name.Contains("管底_尺寸") ||
                                                        sym.FamilyName.Contains("管道標籤_寬高") || sym.Name.Contains("管道標籤_寬高") ||
                                                        sym.FamilyName.Contains("電纜托盤") || sym.Name.Contains("電纜托盤")
                                                    ))
                                                    {
                                                        isTargetTag = true;
                                                    }

                                                    //Reference tagRef = tag.GetTaggedReference(); // 2020
                                                    foreach (Reference tagRef in tag.GetTaggedReferences())
                                                    {
                                                        Element taggedElem = null;
                                                        bool isLinked = tagRef.LinkedElementId != ElementId.InvalidElementId;
                                                        ElementId linkInstId = isLinked ? tagRef.ElementId : ElementId.InvalidElementId;

                                                        if (isLinked)
                                                        {
                                                            alreadyTaggedSignatures.Add($"Linked_{tagRef.ElementId}_{tagRef.LinkedElementId}");

                                                            RevitLinkInstance linkInst = doc.GetElement(tagRef.ElementId) as RevitLinkInstance;
                                                            if (linkInst != null && isTargetTag)
                                                            {
                                                                taggedElem = linkInst.GetLinkDocument()?.GetElement(tagRef.LinkedElementId);
                                                            }
                                                        }
                                                        else
                                                        {
                                                            alreadyTaggedSignatures.Add($"Local_{tagRef.ElementId}");
                                                            if (isTargetTag) taggedElem = doc.GetElement(tagRef.ElementId);
                                                        }

                                                        // 將畫面上已經標好的管線，萃取出它的「系統 ID」並記錄
                                                        if (taggedElem != null)
                                                        {
                                                            string sysSig = GetSystemSignature(taggedElem, isLinked, linkInstId);
                                                            if (sysSig != null) taggedSystemSignatures.Add(sysSig);
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
                                                // 【系統級判斷】：同一個系統在同一個視圖只會打一個標籤
                                                // =========================================================
                                                bool isLinked = !mepItem.SourceProject.IsMainModel;
                                                ElementId linkInstId = isLinked ? mepItem.SourceProject.LinkInstance.Id : ElementId.InvalidElementId;

                                                string sysSig = GetSystemSignature(elem, isLinked, linkInstId);
                                                if (sysSig != null && taggedSystemSignatures.Contains(sysSig))
                                                {
                                                    // 若這個視圖已經有這個「系統 ID」的標籤，則跳過這根管線！
                                                    continue;
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

                                                        // 標籤建立成功後，把這套系統的 ID 註冊進去
                                                        // 這樣同系統的下一根管子就會在上方被過濾掉
                                                        if (sysSig != null)
                                                        {
                                                            taggedSystemSignatures.Add(sysSig);
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
        /// 【全新系統級去重複方法】取得管線的「系統專屬編號 (MEPSystem ID)」
        /// </summary>
        private string GetSystemSignature(Element elem, bool isLinked, ElementId linkInstanceId)
        {
            if (elem == null) return null;

            // 區分是主模型還是連結模型，避免不同連結檔剛好有相同的系統 ID
            string prefix = isLinked ? $"Linked_{linkInstanceId.Value}_" : "Local_";

            // 1. 處理水管與風管 (透過底層繼承的 MEPCurve 取得 MEPSystem)
            if (elem is MEPCurve mepCurve && mepCurve.MEPSystem != null)
            {
                // 只要是同一個系統，這個 ID 絕對一模一樣
                return prefix + "System_" + mepCurve.MEPSystem.Id.Value.ToString();
            }

            // 2. 處理電纜架 (因 Revit 底層設計，電纜架沒有 MEPSystem，以使用者自訂編號視為系統)
            if (elem is CableTray)
            {
                string cableNum = elem.LookupParameter("電纜編號")?.AsString() ??
                                  elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ??
                                  elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ??
                                  elem.Id.Value.ToString(); // 孤立無編號則視為獨立系統
                return prefix + "TraySystem_" + cableNum;
            }

            // 3. 防呆：畫了管線但還沒有分配系統的孤立物件
            return prefix + "Isolated_" + elem.Id.Value.ToString();
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

            if (levelId.Value < 0)
            {
                //int specialId = levelId.IntegerValue; // 2020
                long specialId = levelId.Value; // 2024
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