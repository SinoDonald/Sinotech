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

namespace Sinotech_2025.CSDSEM
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
                            using (ChooseMultiViewPlansForm chooseMultiViewPlansForm = new ChooseMultiViewPlansForm(doc, viewPlans, ChooseMultiViewPlansForm.FormMode.AutoPipeTag))
                            {
                                if (chooseMultiViewPlansForm.ShowDialog() == DialogResult.OK)
                                {
                                    DateTime timeStart = DateTime.Now;
                                    int newTagCounts = 0;
                                    List<ViewPlan> checkViewPlans = chooseMultiViewPlansForm.checkViewPlans;
                                    double maxM = chooseMultiViewPlansForm.maxM; // 大於此長度必標
                                    double minM = chooseMultiViewPlansForm.minM; // 小於此長度不標
                                    if (checkViewPlans.Count > 0)
                                    {
                                        // 進度條視窗
                                        ProgressForm progressForm = new ProgressForm("自動管線標籤", checkViewPlans.Count);
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
                                                    using (Transaction t = new Transaction(doc, "自動標籤"))
                                                    {
                                                        t.Start();

                                                        FamilySymbol pipeTagSym = GetTagSymbol(doc, BuiltInCategory.OST_PipeTags, "管底_尺寸+系統");
                                                        FamilySymbol ductTagSym = GetTagSymbol(doc, BuiltInCategory.OST_DuctTags, "管道標籤_寬高_一行");
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

                                                            HashSet<string> alreadyTaggedSignatures = new HashSet<string>();
                                                            HashSet<string> taggedSystemSignatures = new HashSet<string>();
                                                            Dictionary<string, double> taggedSysSigMaxLength = new Dictionary<string, double>();

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

                                                                    foreach (Reference tagRef in tag.GetTaggedReferences())
                                                                    {
                                                                        bool isLinked = tagRef.LinkedElementId != ElementId.InvalidElementId;
                                                                        ElementId linkInstId = isLinked ? tagRef.ElementId : ElementId.InvalidElementId;

                                                                        if (isLinked)
                                                                        {
                                                                            if (isTargetTag)
                                                                            {
                                                                                alreadyTaggedSignatures.Add($"Linked_{tagRef.ElementId}_{tagRef.LinkedElementId}");

                                                                                RevitLinkInstance linkInst = doc.GetElement(tagRef.ElementId) as RevitLinkInstance;
                                                                                Element taggedElem = linkInst?.GetLinkDocument()?.GetElement(tagRef.LinkedElementId);
                                                                                if (taggedElem != null && IsEligibleForTag(taggedElem))
                                                                                {
                                                                                    ElementId lInstId = tagRef.ElementId;
                                                                                    string sysSigLinked = GetSystemSignature(taggedElem, true, lInstId);
                                                                                    if (sysSigLinked != null)
                                                                                    {
                                                                                        Transform linkXform = linkInst.GetTotalTransform();
                                                                                        double taggedVisLen = GetVisibleLengthInView(
                                                                                            taggedElem, linkXform,
                                                                                            viewMinX, viewMaxX, viewMinY, viewMaxY);
                                                                                        if (!taggedSysSigMaxLength.ContainsKey(sysSigLinked)
                                                                                            || taggedVisLen > taggedSysSigMaxLength[sysSigLinked])
                                                                                        {
                                                                                            taggedSysSigMaxLength[sysSigLinked] = taggedVisLen;
                                                                                        }
                                                                                    }
                                                                                }
                                                                            }
                                                                        }
                                                                        else
                                                                        {
                                                                            alreadyTaggedSignatures.Add($"Local_{tagRef.ElementId}");

                                                                            if (isTargetTag)
                                                                            {
                                                                                Element taggedElem = doc.GetElement(tagRef.ElementId);
                                                                                if (taggedElem != null && IsEligibleForTag(taggedElem))
                                                                                {
                                                                                    string sysSig = GetSystemSignature(taggedElem, false, ElementId.InvalidElementId);
                                                                                    if (sysSig != null)
                                                                                    {
                                                                                        taggedSystemSignatures.Add(sysSig);
                                                                                        double taggedVisLen = GetVisibleLengthInView(
                                                                                            taggedElem, null,
                                                                                            viewMinX, viewMaxX, viewMinY, viewMaxY);
                                                                                        if (!taggedSysSigMaxLength.ContainsKey(sysSig)
                                                                                            || taggedVisLen > taggedSysSigMaxLength[sysSig])
                                                                                        {
                                                                                            taggedSysSigMaxLength[sysSig] = taggedVisLen;
                                                                                        }
                                                                                    }
                                                                                }
                                                                            }
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
                                                                    try
                                                                    {
                                                                        validMepInThisView.Add(new TargetMepElement { MepElement = elem, SourceProject = linkedProj });
                                                                    }
                                                                    catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                                                                }
                                                            }

                                                            if (validMepInThisView.Count == 0) continue;

                                                            Dictionary<string, TagCandidate> tagCandidates = new Dictionary<string, TagCandidate>();

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

                                                                if (elem is Duct ductElem)
                                                                {
                                                                    ElementId dtId = ductElem.GetTypeId();
                                                                    if (dtId != null && dtId != ElementId.InvalidElementId)
                                                                    {
                                                                        Element dtElem = (mepItem.SourceProject.IsMainModel
                                                                            ? doc
                                                                            : mepItem.SourceProject.Doc).GetElement(dtId);
                                                                        if (dtElem != null &&
                                                                            (dtElem.Name ?? string.Empty).IndexOf("BUSWAY", StringComparison.OrdinalIgnoreCase) >= 0)
                                                                        {
                                                                            continue;
                                                                        }
                                                                    }
                                                                }

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

                                                                if (Math.Abs(pt1.X - pt0.X) < 0.01 && Math.Abs(pt1.Y - pt0.Y) < 0.01) continue;

                                                                double lengthMeter = pt0.DistanceTo(pt1) * 0.3048;
                                                                if (lengthMeter < minM) continue;

                                                                if (elem is Pipe)
                                                                {
                                                                    Parameter diaParam = elem.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                                                                    if (diaParam != null && diaParam.HasValue)
                                                                    {
                                                                        double diaMm = diaParam.AsDouble() * 304.8;
                                                                        if (diaMm < 49.9) continue;
                                                                    }
                                                                }
                                                                else if (elem is Duct)
                                                                {
                                                                    double widthMm = (elem.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.AsDouble() ?? 0) * 304.8;
                                                                    double heightMm = (elem.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble() ?? 0) * 304.8;
                                                                    if (Math.Min(widthMm, heightMm) < 49.9) continue;
                                                                }
                                                                else if (elem is CableTray)
                                                                {
                                                                    double widthMm = (elem.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() ?? 0) * 304.8;
                                                                    double heightMm = (elem.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() ?? 0) * 304.8;
                                                                    if (Math.Min(widthMm, heightMm) < 49.9) continue;
                                                                }

                                                                if (elem is Pipe)
                                                                {
                                                                    Parameter pipeParam = elem.get_Parameter(BuiltInParameter.RBS_PIPE_BOTTOM_ELEVATION);
                                                                    if (pipeParam != null && pipeParam.HasValue)
                                                                    {
                                                                        if (pipeParam.AsDouble() < 0) continue;
                                                                    }
                                                                }
                                                                else if (elem is Duct)
                                                                {
                                                                    Parameter ductParam = elem.get_Parameter(BuiltInParameter.RBS_DUCT_BOTTOM_ELEVATION);
                                                                    if (ductParam != null && ductParam.HasValue)
                                                                    {
                                                                        if (ductParam.AsDouble() < 0) continue;
                                                                    }
                                                                }
                                                                else if (elem is CableTray)
                                                                {
                                                                    Parameter ctcParam = elem.get_Parameter(BuiltInParameter.RBS_CTC_BOTTOM_ELEVATION);
                                                                    if (ctcParam != null && ctcParam.HasValue)
                                                                    {
                                                                        if (ctcParam.AsDouble() < 0) continue;
                                                                    }
                                                                }

                                                                double minZ = Math.Min(pt0.Z, pt1.Z);
                                                                double maxZ = Math.Max(pt0.Z, pt1.Z);

                                                                if (maxZ < validZ_Min || minZ > validZ_Max) continue;

                                                                Reference pipeRef = mepItem.SourceProject.IsMainModel
                                                                    ? new Reference(elem)
                                                                    : new Reference(elem).CreateLinkReference(mepItem.SourceProject.LinkInstance);

                                                                const double xyTol = 1e-6;
                                                                bool pt0InView = pt0.X >= viewMinX - xyTol && pt0.X <= viewMaxX + xyTol &&
                                                                                 pt0.Y >= viewMinY - xyTol && pt0.Y <= viewMaxY + xyTol;
                                                                bool pt1InView = pt1.X >= viewMinX - xyTol && pt1.X <= viewMaxX + xyTol &&
                                                                                 pt1.Y >= viewMinY - xyTol && pt1.Y <= viewMaxY + xyTol;

                                                                XYZ tagMidPoint;
                                                                double visibleLength;

                                                                if (pt0InView && pt1InView)
                                                                {
                                                                    tagMidPoint = (pt0 + pt1) / 2.0;
                                                                    visibleLength = pt0.DistanceTo(pt1);
                                                                }
                                                                else
                                                                {
                                                                    XYZ clippedPt0, clippedPt1;
                                                                    bool clipped = ClipSegmentToViewBounds(
                                                                        pt0, pt1, viewMinX, viewMaxX, viewMinY, viewMaxY,
                                                                        out clippedPt0, out clippedPt1);
                                                                    if (!clipped) continue;
                                                                    tagMidPoint = (clippedPt0 + clippedPt1) / 2.0;
                                                                    visibleLength = clippedPt0.DistanceTo(clippedPt1);
                                                                }

                                                                double tagZ = tagMidPoint.Z;
                                                                if (tagZ > exactZMax) tagZ = exactZMax - 0.01;
                                                                if (tagZ < exactZMin) tagZ = exactZMin + 0.01;

                                                                // =========================================================
                                                                // 【計算管道角度】保證文字水平易讀 (控制在 -90 到 90 度)
                                                                // 並且藉此判斷是水平管還是垂直管，以決定引線折線邏輯
                                                                // =========================================================
                                                                XYZ dir = (pt1 - pt0).Normalize();
                                                                double pipeAngle = Math.Atan2(dir.Y, dir.X);
                                                                if (pipeAngle > Math.PI / 2.0 + 1e-6) pipeAngle -= Math.PI;
                                                                else if (pipeAngle < -Math.PI / 2.0 - 1e-6) pipeAngle += Math.PI;

                                                                // 判斷管線主要是「水平向(X軸)」還是「垂直向(Y軸)」
                                                                bool isHorizontalPipe = Math.Abs(dir.X) >= Math.Abs(dir.Y);

                                                                // =========================================================
                                                                // 【四象限標籤偏移與折線邏輯】
                                                                // =========================================================
                                                                double viewCenterX = (viewMinX + viewMaxX) / 2.0;
                                                                double viewCenterY = (viewMinY + viewMaxY) / 2.0;

                                                                XYZ tagPlacementPoint;
                                                                XYZ elbowPt = null;
                                                                XYZ headPt = new XYZ(tagMidPoint.X, tagMidPoint.Y, tagZ);

                                                                if (elem is Pipe || elem is CableTray)
                                                                {
                                                                    // 【重要修正】：放大 X 與 Y 的偏移量！
                                                                    // 若 X 偏移量太小 (例如小於標籤文字的一半)，標籤的文字框會包住轉折點
                                                                    // Revit 為了避開文字，會強制斜拉引線，破壞 90 度的美觀。
                                                                    // 此處設定圖面上約偏移 X:50mm, Y:20mm，保證有充足空間劃出 90 度直角。
                                                                    double offX = (50.0 / 304.8) * checkViewPlan.Scale;
                                                                    double offY = (20.0 / 304.8) * checkViewPlan.Scale;

                                                                    // 判斷該管線在視圖的哪個象限，決定向外推移的方向
                                                                    double pX = tagMidPoint.X + (tagMidPoint.X >= viewCenterX ? offX : -offX);
                                                                    double pY = tagMidPoint.Y + (tagMidPoint.Y >= viewCenterY ? offY : -offY);

                                                                    tagPlacementPoint = new XYZ(pX, pY, tagZ);

                                                                    // 【完美 90 度折線邏輯】
                                                                    if (isHorizontalPipe)
                                                                    {
                                                                        // 管道為水平：轉折點先上下走 (Y 與標籤齊平)，再左右接標籤
                                                                        elbowPt = new XYZ(tagMidPoint.X, pY, tagZ);
                                                                    }
                                                                    else
                                                                    {
                                                                        // 管道為垂直：轉折點先左右走 (X 與標籤齊平)，再上下接標籤
                                                                        // 這樣引線才不會在起點就跟垂直管線重疊！
                                                                        elbowPt = new XYZ(pX, tagMidPoint.Y, tagZ);
                                                                    }
                                                                }
                                                                else
                                                                {
                                                                    // 風管直接放置於中心，無引線
                                                                    tagPlacementPoint = headPt;
                                                                }

                                                                bool isLinked = !mepItem.SourceProject.IsMainModel;
                                                                ElementId linkInstId = isLinked ? mepItem.SourceProject.LinkInstance.Id : ElementId.InvalidElementId;

                                                                string sysSig = GetSystemSignature(elem, isLinked, linkInstId);

                                                                double visibleLengthMeter = visibleLength * 0.3048;
                                                                bool isLongPipe = visibleLengthMeter > maxM;

                                                                if (isLongPipe)
                                                                {
                                                                    if (!tagCandidates.TryGetValue(currentSig, out TagCandidate existingLong)
                                                                        || visibleLength > existingLong.VisibleLength)
                                                                    {
                                                                        tagCandidates[currentSig] = new TagCandidate
                                                                        {
                                                                            ElemRef = pipeRef,
                                                                            TargetSym = targetSymbol,
                                                                            PlacementPt = tagPlacementPoint,
                                                                            HeadPt = headPt,
                                                                            ElbowPt = elbowPt,
                                                                            VisibleLength = visibleLength,
                                                                            SysSig = null,
                                                                            Angle = pipeAngle
                                                                        };
                                                                    }
                                                                    continue;
                                                                }

                                                                if (sysSig != null)
                                                                {
                                                                    if (taggedSysSigMaxLength.TryGetValue(sysSig, out double existingTaggedLen)
                                                                        && existingTaggedLen >= visibleLength)
                                                                    {
                                                                        continue;
                                                                    }

                                                                    if (tagCandidates.TryGetValue(sysSig, out TagCandidate existing))
                                                                    {
                                                                        if (visibleLength > existing.VisibleLength)
                                                                        {
                                                                            tagCandidates[sysSig] = new TagCandidate
                                                                            {
                                                                                ElemRef = pipeRef,
                                                                                TargetSym = targetSymbol,
                                                                                PlacementPt = tagPlacementPoint,
                                                                                HeadPt = headPt,
                                                                                ElbowPt = elbowPt,
                                                                                VisibleLength = visibleLength,
                                                                                SysSig = sysSig,
                                                                                Angle = pipeAngle
                                                                            };
                                                                        }
                                                                    }
                                                                    else
                                                                    {
                                                                        tagCandidates[sysSig] = new TagCandidate
                                                                        {
                                                                            ElemRef = pipeRef,
                                                                            TargetSym = targetSymbol,
                                                                            PlacementPt = tagPlacementPoint,
                                                                            HeadPt = headPt,
                                                                            ElbowPt = elbowPt,
                                                                            VisibleLength = visibleLength,
                                                                            SysSig = sysSig,
                                                                            Angle = pipeAngle
                                                                        };
                                                                    }
                                                                }
                                                                else
                                                                {
                                                                    tagCandidates[currentSig] = new TagCandidate
                                                                    {
                                                                        ElemRef = pipeRef,
                                                                        TargetSym = targetSymbol,
                                                                        PlacementPt = tagPlacementPoint,
                                                                        HeadPt = headPt,
                                                                        ElbowPt = elbowPt,
                                                                        VisibleLength = visibleLength,
                                                                        SysSig = null,
                                                                        Angle = pipeAngle
                                                                    };
                                                                }
                                                            }

                                                            // =========================================================
                                                            // 【候選確定後】對每個簽章的最長管道建立標籤
                                                            // =========================================================
                                                            foreach (TagCandidate candidate in tagCandidates.Values)
                                                            {
                                                                try
                                                                {
                                                                    if (checkViewPlan.CropBoxActive)
                                                                    {
                                                                        XYZ pt = candidate.PlacementPt;
                                                                        const double cropTol = 1e-4;
                                                                        if (pt.X < viewMinX - cropTol || pt.X > viewMaxX + cropTol ||
                                                                            pt.Y < viewMinY - cropTol || pt.Y > viewMaxY + cropTol)
                                                                        {
                                                                            continue;
                                                                        }
                                                                    }

                                                                    bool isDuctTag = candidate.TargetSym.Category.Id.Value == (long)BuiltInCategory.OST_DuctTags;
                                                                    bool hasLeader = !isDuctTag;
                                                                    TagOrientation tagOri = isDuctTag ? TagOrientation.AnyModelDirection : TagOrientation.Horizontal;

                                                                    IndependentTag newTag = IndependentTag.Create(
                                                                        doc,
                                                                        checkViewPlan.Id,
                                                                        candidate.ElemRef,
                                                                        hasLeader,
                                                                        TagMode.TM_ADDBY_CATEGORY,
                                                                        tagOri,
                                                                        candidate.PlacementPt
                                                                    );

                                                                    if (newTag != null)
                                                                    {
                                                                        newTag.ChangeTypeId(candidate.TargetSym.Id);

                                                                        if (hasLeader)
                                                                        {
                                                                            // =========================================================
                                                                            // 【引線四象限折線設定】
                                                                            // =========================================================
                                                                            // 1. 改為自由端點 (Free)
                                                                            try { newTag.LeaderEndCondition = LeaderEndCondition.Free; } catch { }

                                                                            // 2. 重新對齊標籤文字位置 (避免設為 Free 後，Revit 自動調整導致亂飄)
                                                                            try { newTag.TagHeadPosition = candidate.PlacementPt; } catch { }

                                                                            // 3. 設定端點(在管上)與轉折點(90度轉彎)
                                                                            // // 2024
                                                                            try
                                                                            {
                                                                                newTag.SetLeaderEnd(candidate.ElemRef, candidate.HeadPt);
                                                                                if (candidate.ElbowPt != null)
                                                                                    newTag.SetLeaderElbow(candidate.ElemRef, candidate.ElbowPt);
                                                                            }
                                                                            catch { }

                                                                            // // 2020
                                                                            /*
                                                                            try 
                                                                            { 
                                                                                newTag.LeaderEnd = candidate.HeadPt;
                                                                                if (candidate.ElbowPt != null) 
                                                                                    newTag.LeaderElbow = candidate.ElbowPt; 
                                                                            } 
                                                                            catch { }
                                                                            */
                                                                        }

                                                                        // // 2024
                                                                        // if (isDuctTag)
                                                                        // {
                                                                        //     try { newTag.RotationAngle = candidate.Angle; } catch { }
                                                                        // }
                                                                        // // 2020

                                                                        newTagCounts++;
                                                                        if (candidate.SysSig != null)
                                                                            taggedSystemSignatures.Add(candidate.SysSig);
                                                                    }
                                                                }
                                                                catch (Autodesk.Revit.Exceptions.ArgumentException) { }
                                                            }

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
                                            TaskDialog.Show("Revit", $"已產生 {newTagCounts} 個管線標籤！\n\n耗時：{totalTime.Minutes} 分 {totalTime.Seconds} 秒。");
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
                    }

                    return Result.Succeeded;
                }
            }

            return Result.Cancelled;
        }

        private bool IsEligibleForTag(Element elem, Transform linkTransform = null)
        {
            if (elem == null) return false;
            if (!(elem.Location is LocationCurve locCurve) || locCurve.Curve == null) return false;

            XYZ pt0 = locCurve.Curve.GetEndPoint(0);
            XYZ pt1 = locCurve.Curve.GetEndPoint(1);

            if (linkTransform != null)
            {
                pt0 = linkTransform.OfPoint(pt0);
                pt1 = linkTransform.OfPoint(pt1);
            }

            if (Math.Abs(pt1.X - pt0.X) < 0.01 && Math.Abs(pt1.Y - pt0.Y) < 0.01)
                return false;

            double lengthMeter = pt0.DistanceTo(pt1) * 0.3048;
            if (lengthMeter < 1.0)
                return false;

            if (elem is Pipe)
            {
                double diaMm = (elem.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble() ?? 0) * 304.8;
                if (diaMm < 49.9) return false;
            }
            else if (elem is Duct)
            {
                double widthMm = (elem.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.AsDouble() ?? 0) * 304.8;
                double heightMm = (elem.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble() ?? 0) * 304.8;
                if (Math.Min(widthMm, heightMm) < 49.9) return false;
            }
            else if (elem is CableTray)
            {
                double widthMm = (elem.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsDouble() ?? 0) * 304.8;
                double heightMm = (elem.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsDouble() ?? 0) * 304.8;
                if (Math.Min(widthMm, heightMm) < 49.9) return false;
            }

            return true;
        }

        private double GetVisibleLengthInView(
            Element elem, Transform linkTransform,
            double viewMinX, double viewMaxX,
            double viewMinY, double viewMaxY)
        {
            try
            {
                if (!(elem.Location is LocationCurve lc) || lc.Curve == null) return 0;
                XYZ p0 = lc.Curve.GetEndPoint(0);
                XYZ p1 = lc.Curve.GetEndPoint(1);
                if (linkTransform != null) { p0 = linkTransform.OfPoint(p0); p1 = linkTransform.OfPoint(p1); }

                const double tol = 1e-6;
                bool p0In = p0.X >= viewMinX - tol && p0.X <= viewMaxX + tol &&
                            p0.Y >= viewMinY - tol && p0.Y <= viewMaxY + tol;
                bool p1In = p1.X >= viewMinX - tol && p1.X <= viewMaxX + tol &&
                            p1.Y >= viewMinY - tol && p1.Y <= viewMaxY + tol;

                if (p0In && p1In) return p0.DistanceTo(p1);

                XYZ c0, c1;
                if (ClipSegmentToViewBounds(p0, p1, viewMinX, viewMaxX, viewMinY, viewMaxY, out c0, out c1))
                    return c0.DistanceTo(c1);
            }
            catch { }
            return 0;
        }

        private string GetSystemSignature(Element elem, bool isLinked, ElementId linkInstanceId)
        {
            if (elem == null) return null;

            string prefix = isLinked ? $"Linked_{linkInstanceId.Value}_" : "Local_";

            if (elem is MEPCurve mepCurve)
            {
                string systemId = mepCurve.MEPSystem != null
                    ? mepCurve.MEPSystem.Id.Value.ToString()
                    : "NoSys_" + elem.Id.Value.ToString();

                string tagContent = GetTagContentSignature(elem);

                return $"{prefix}System_{systemId}__{tagContent}";
            }

            if (elem is CableTray)
            {
                string cableNum = elem.LookupParameter("電纜編號")?.AsString() ??
                                  elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ??
                                  elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ??
                                  elem.Id.Value.ToString();
                return $"{prefix}TraySystem_{cableNum}";
            }

            return $"{prefix}Isolated_{elem.Id.Value}";
        }

        private string GetTagContentSignature(Element elem)
        {
            try
            {
                string sysAbbr = string.Empty;
                if (elem is MEPCurve mepCurve && mepCurve.MEPSystem != null)
                {
                    sysAbbr = mepCurve.MEPSystem.get_Parameter(BuiltInParameter.RBS_SYSTEM_ABBREVIATION_PARAM)?.AsString() ?? string.Empty;
                }

                if (elem is Pipe)
                {
                    double diaMm = Math.Round((elem.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble() ?? 0) * 304.8);
                    double centerElev = (elem.get_Parameter(BuiltInParameter.RBS_PIPE_BOTTOM_ELEVATION)?.AsDouble() ?? 0) * 304.8;
                    double elevMm = Math.Round(centerElev / 100.0) * 100.0;
                    return $"P_{diaMm}_{sysAbbr}_{elevMm}";
                }

                if (elem is Duct)
                {
                    double widthMm = Math.Round((elem.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.AsDouble() ?? 0) * 304.8);
                    double heightMm = Math.Round((elem.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble() ?? 0) * 304.8);
                    double centerElev = (elem.get_Parameter(BuiltInParameter.RBS_DUCT_BOTTOM_ELEVATION)?.AsDouble() ?? 0) * 304.8;
                    double elevMm = Math.Round(centerElev / 100.0) * 100.0;
                    return $"D_{widthMm}x{heightMm}_{sysAbbr}_{elevMm}";
                }
            }
            catch { }

            return $"Elem_{elem.Id.Value}";
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

        private bool ClipSegmentToViewBounds(
            XYZ p0, XYZ p1,
            double xMin, double xMax,
            double yMin, double yMax,
            out XYZ clipped0, out XYZ clipped1)
        {
            double dx = p1.X - p0.X;
            double dy = p1.Y - p0.Y;
            double dz = p1.Z - p0.Z;

            double tMin = 0.0;
            double tMax = 1.0;

            double[] p = new double[] { -dx, dx, -dy, dy };
            double[] q = new double[] {
        p0.X - xMin,
        xMax - p0.X,
        p0.Y - yMin,
        yMax - p0.Y
    };

            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(p[i]) < 1e-10)
                {
                    if (q[i] < 0)
                    {
                        clipped0 = p0;
                        clipped1 = p1;
                        return false;
                    }
                }
                else
                {
                    double t = q[i] / p[i];
                    if (p[i] < 0)
                    {
                        if (t > tMin) tMin = t;
                    }
                    else
                    {
                        if (t < tMax) tMax = t;
                    }
                }

                if (tMin > tMax)
                {
                    clipped0 = p0;
                    clipped1 = p1;
                    return false;
                }
            }

            clipped0 = new XYZ(
                p0.X + tMin * dx,
                p0.Y + tMin * dy,
                p0.Z + tMin * dz);

            clipped1 = new XYZ(
                p0.X + tMax * dx,
                p0.Y + tMax * dy,
                p0.Z + tMax * dz);

            return true;
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

    public class TagCandidate
    {
        public Reference ElemRef { get; set; }
        public FamilySymbol TargetSym { get; set; }
        public XYZ PlacementPt { get; set; }
        public double VisibleLength { get; set; }
        public string SysSig { get; set; }
        public double Angle { get; set; }

        // 【新增】：用於四象限引線折線設定
        public XYZ HeadPt { get; set; }
        public XYZ ElbowPt { get; set; }
    }

    public class ProgressForm : System.Windows.Forms.Form
    {
        private System.Windows.Forms.Label _labelTitle;
        private System.Windows.Forms.Label _labelCurrent;
        private System.Windows.Forms.ProgressBar _progressBar;
        private System.Windows.Forms.Label _labelPercent;

        private readonly int _total;
        private int _current = 0;

        public ProgressForm(string title, int totalCount)
        {
            _total = Math.Max(1, totalCount);
            InitializeComponents(title);
        }

        private void InitializeComponents(string title)
        {
            this.Text = title;
            this.Width = 420;
            this.Height = 150;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.ControlBox = false;

            _labelTitle = new System.Windows.Forms.Label
            {
                Text = title,
                Left = 12,
                Top = 10,
                Width = 390,
                Font = new System.Drawing.Font("微軟正黑體", 10, System.Drawing.FontStyle.Bold)
            };

            _labelCurrent = new System.Windows.Forms.Label
            {
                Text = "準備中...",
                Left = 12,
                Top = 35,
                Width = 390,
                Font = new System.Drawing.Font("微軟正黑體", 9)
            };

            _progressBar = new System.Windows.Forms.ProgressBar
            {
                Left = 12,
                Top = 60,
                Width = 390,
                Height = 22,
                Minimum = 0,
                Maximum = _total,
                Value = 0,
                Style = System.Windows.Forms.ProgressBarStyle.Continuous
            };

            _labelPercent = new System.Windows.Forms.Label
            {
                Text = $"0 / {_total}（0%）",
                Left = 12,
                Top = 88,
                Width = 390,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Font = new System.Drawing.Font("微軟正黑體", 9)
            };

            this.Controls.Add(_labelTitle);
            this.Controls.Add(_labelCurrent);
            this.Controls.Add(_progressBar);
            this.Controls.Add(_labelPercent);
        }

        public void UpdateProgress(string currentViewName)
        {
            _current++;
            int pct = (int)Math.Round(_current * 100.0 / _total);
            _labelCurrent.Text = $"處理中：{currentViewName}";
            _progressBar.Value = Math.Min(_current, _total);
            _labelPercent.Text = $"{_current} / {_total}（{pct}%）";
        }
    }
}