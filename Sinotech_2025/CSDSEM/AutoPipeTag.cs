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

                                        // =========================================================
                                        // 【新增】Transaction 外：預先開啟子視圖對應的母視圖
                                        // 原因：標籤放置在子視圖時，若母視圖未開啟，標籤將無法移動
                                        // =========================================================
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
                                                        // 開啟母視圖（讓 Revit 內部完成視圖初始化）
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
                                                    // 開啟母視圖（讓 Revit 內部完成視圖初始化）
                                                    uidoc.RequestViewChange(parentView);
                                                    Application.DoEvents(); // 等待Revit視圖切換完成
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
                                                            // 更新進度條
                                                            progressForm.UpdateProgress(checkViewPlan.Name);
                                                            Application.DoEvents();
                                                            double exactZMax = GetPlaneElevation(checkViewPlan, PlanViewPlane.TopClipPlane, 1000.0, -1000.0);
                                                            double exactZMin = GetPlaneElevation(checkViewPlan, PlanViewPlane.ViewDepthPlane, 1000.0, -1000.0);
                                                            double defaultCutZ = (exactZMax + exactZMin) / 2.0;
                                                            double exactCutZ = GetPlaneElevation(checkViewPlan, PlanViewPlane.CutPlane, defaultCutZ, defaultCutZ);

                                                            double validZ_Min = exactZMin - 0.5;
                                                            double validZ_Max = exactZMax + 0.5;

                                                            // 視圖 XY 裁切範圍（提前計算，供掃描現有標籤與放置點邏輯共用）
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

                                                            // =========================================================
                                                            // 【系統級防重複機制】
                                                            // alreadyTaggedSignatures : 已標注的元件實體 ID
                                                            // taggedSystemSignatures  : 已標注的系統簽章（僅目標族、僅主模型）
                                                            // taggedSysSigMaxLength   : 各 sysSig 已標注管道的最大可見長度（英呎）
                                                            //   → Bug 修正：重新執行時若已標注的是最長管道，整個 sysSig 不再補標
                                                            // =========================================================
                                                            HashSet<string> alreadyTaggedSignatures = new HashSet<string>();
                                                            HashSet<string> taggedSystemSignatures = new HashSet<string>();
                                                            Dictionary<string, double> taggedSysSigMaxLength = new Dictionary<string, double>();

                                                            // 只掃描子視圖本身的標籤（不含母視圖，避免污染其他子視圖的判斷）
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
                                                                        bool isLinked = tagRef.LinkedElementId != ElementId.InvalidElementId;
                                                                        ElementId linkInstId = isLinked ? tagRef.ElementId : ElementId.InvalidElementId;

                                                                        if (isLinked)
                                                                        {
                                                                            if (isTargetTag)
                                                                            {
                                                                                alreadyTaggedSignatures.Add($"Linked_{tagRef.ElementId}_{tagRef.LinkedElementId}");

                                                                                // 【Bug 修正】連結管道也要記錄已標注長度，
                                                                                // 雖然不寫入 taggedSystemSignatures（後續連結標籤將關閉），
                                                                                // 但仍需記錄長度，避免第二次執行時最長管道被跳過後
                                                                                // 補標了較短的管道。
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
                                                                            // taggedSystemSignatures：連結標籤一律不寫入（後續將關閉）
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

                                                                // =========================================================
                                                                // 【排除 BusWay】
                                                                // DuctType 名稱含 "BUSWAY"（不分大小寫）的風管不標籤
                                                                // =========================================================
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

                                                                // 條件一：立管不標籤 (起終點的 X, Y 座標幾乎相同，給予 0.01 呎約 3mm 容差)
                                                                if (Math.Abs(pt1.X - pt0.X) < 0.01 && Math.Abs(pt1.Y - pt0.Y) < 0.01)
                                                                {
                                                                    continue;
                                                                }

                                                                // 條件二：長度低於 2M 不標籤 (將英呎轉換為公尺)
                                                                double lengthMeter = pt0.DistanceTo(pt1) * 0.3048;
                                                                if (lengthMeter < minM)
                                                                {
                                                                    continue;
                                                                }

                                                                // 條件三：50mm(不含)以下的管徑/尺寸不標籤
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

                                                                // 條件四：高程(下端底部高程)為負值時不標籤
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

                                                                if (maxZ < validZ_Min || minZ > validZ_Max)
                                                                {
                                                                    continue;
                                                                }

                                                                Reference pipeRef = mepItem.SourceProject.IsMainModel
                                                                    ? new Reference(elem)
                                                                    : new Reference(elem).CreateLinkReference(mepItem.SourceProject.LinkInstance);

                                                                // viewMinX/Y/MaxX/Y 已在視圖迴圈外計算，直接使用
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

                                                                XYZ tagPlacementPoint = new XYZ(tagMidPoint.X, tagMidPoint.Y, tagZ);

                                                                bool isLinked = !mepItem.SourceProject.IsMainModel;
                                                                ElementId linkInstId = isLinked ? mepItem.SourceProject.LinkInstance.Id : ElementId.InvalidElementId;

                                                                string sysSig = GetSystemSignature(elem, isLinked, linkInstId);

                                                                // =========================================================
                                                                // 【長管強制標籤條件】
                                                                // 視圖內可見長度 > 10m(使用者設定必標長度)：不論系統/標籤內容是否相同，
                                                                // 每根都各自建立標籤。
                                                                //
                                                                // 修正重複標籤 Bug 的根本做法：
                                                                //   - 以 currentSig（元件實體 ID）作為 key，確保同一根
                                                                //     管道只會有一筆候選，不會因為「長管 key」+「sysSig key」
                                                                //     各自放進字典而建立兩次標籤。
                                                                //   - isLongPipe 時直接 continue，跳過下方的 sysSig 邏輯，
                                                                //     兩條路徑完全互斥，不需要 ElementUniqueId 防呆。
                                                                // =========================================================
                                                                double visibleLengthMeter = visibleLength * 0.3048;
                                                                bool isLongPipe = visibleLengthMeter > maxM;

                                                                if (isLongPipe)
                                                                {
                                                                    // key = currentSig（元件 ID），確保同一根管道只有一筆候選
                                                                    if (!tagCandidates.TryGetValue(currentSig, out TagCandidate existingLong)
                                                                        || visibleLength > existingLong.VisibleLength)
                                                                    {
                                                                        tagCandidates[currentSig] = new TagCandidate
                                                                        {
                                                                            ElemRef = pipeRef,
                                                                            TargetSym = targetSymbol,
                                                                            PlacementPt = tagPlacementPoint,
                                                                            VisibleLength = visibleLength,
                                                                            SysSig = null  // null = 不寫入 taggedSystemSignatures
                                                                        };
                                                                    }
                                                                    continue; // 長管路徑結束，跳過下方 sysSig 邏輯，確保不重複
                                                                }

                                                                if (sysSig != null)
                                                                {
                                                                    // =========================================================
                                                                    // 【防重複核心邏輯】
                                                                    // 以 taggedSysSigMaxLength 判斷：此 sysSig 是否已有標注，
                                                                    // 且已標注的管道可見長度 >= 當前候選。
                                                                    // 條件成立 → 最長管道已標注，整個 sysSig 跳過，不補標。
                                                                    // 此邏輯同時適用主模型（taggedSystemSignatures 也記錄）
                                                                    // 和連結模型（僅 taggedSysSigMaxLength 記錄）。
                                                                    // =========================================================
                                                                    if (taggedSysSigMaxLength.TryGetValue(sysSig, out double existingTaggedLen)
                                                                        && existingTaggedLen >= visibleLength)
                                                                    {
                                                                        continue; // 已標注的管道不比當前候選短，不需補標
                                                                    }

                                                                    // 尚未有標注，或已標注的比當前候選短：加入/更新候選
                                                                    if (tagCandidates.TryGetValue(sysSig, out TagCandidate existing))
                                                                    {
                                                                        if (visibleLength > existing.VisibleLength)
                                                                        {
                                                                            tagCandidates[sysSig] = new TagCandidate
                                                                            {
                                                                                ElemRef = pipeRef,
                                                                                TargetSym = targetSymbol,
                                                                                PlacementPt = tagPlacementPoint,
                                                                                VisibleLength = visibleLength,
                                                                                SysSig = sysSig
                                                                            };
                                                                        }
                                                                    }
                                                                    else
                                                                    {
                                                                        if (!taggedSystemSignatures.Contains(sysSig))
                                                                        {
                                                                            tagCandidates[sysSig] = new TagCandidate
                                                                            {
                                                                                ElemRef = pipeRef,
                                                                                TargetSym = targetSymbol,
                                                                                PlacementPt = tagPlacementPoint,
                                                                                VisibleLength = visibleLength,
                                                                                SysSig = sysSig
                                                                            };
                                                                        }
                                                                        else
                                                                        {
                                                                            // sysSig 在 taggedSystemSignatures 中（主模型已標注）
                                                                            // 但已標注的長度 < 當前候選，更新候選（補標更長的）
                                                                            tagCandidates[sysSig] = new TagCandidate
                                                                            {
                                                                                ElemRef = pipeRef,
                                                                                TargetSym = targetSymbol,
                                                                                PlacementPt = tagPlacementPoint,
                                                                                VisibleLength = visibleLength,
                                                                                SysSig = sysSig
                                                                            };
                                                                        }
                                                                    }
                                                                }
                                                                else
                                                                {
                                                                    // sysSig 為 null（理論上不發生）：以元件 ID 為 key
                                                                    tagCandidates[currentSig] = new TagCandidate
                                                                    {
                                                                        ElemRef = pipeRef,
                                                                        TargetSym = targetSymbol,
                                                                        PlacementPt = tagPlacementPoint,
                                                                        VisibleLength = visibleLength,
                                                                        SysSig = null
                                                                    };
                                                                }
                                                            }

                                                            // =========================================================
                                                            // 【候選確定後】對每個簽章的最長管道建立標籤
                                                            // 第四個參數 true = hasLeader，確保每個標籤都有引線
                                                            // =========================================================
                                                            foreach (TagCandidate candidate in tagCandidates.Values)
                                                            {
                                                                try
                                                                {
                                                                    // =========================================================
                                                                    // 【放置前檢查】確認標籤放置點在視圖 CropBox 內
                                                                    // 若管道靠近裁切線，clippedMid 可能仍落在 CropBox 邊緣外，
                                                                    // 建立的標籤在視圖中不顯示，但掃描時也找不到，
                                                                    // 導致每次執行都重複建立「隱形標籤」。
                                                                    // 解法：放置點在 CropBox 外則跳過，不建立標籤。
                                                                    // =========================================================
                                                                    if (checkViewPlan.CropBoxActive)
                                                                    {
                                                                        XYZ pt = candidate.PlacementPt;
                                                                        const double cropTol = 1e-4;
                                                                        if (pt.X < viewMinX - cropTol || pt.X > viewMaxX + cropTol ||
                                                                            pt.Y < viewMinY - cropTol || pt.Y > viewMaxY + cropTol)
                                                                        {
                                                                            continue; // 放置點在 CropBox 外，跳過不建立
                                                                        }
                                                                    }
                                                                    IndependentTag newTag = IndependentTag.Create(
                                                                        doc,
                                                                        checkViewPlan.Id,
                                                                        candidate.ElemRef,
                                                                        true,  // hasLeader = true，強制建立引線
                                                                        TagMode.TM_ADDBY_CATEGORY,
                                                                        TagOrientation.Horizontal,
                                                                        candidate.PlacementPt
                                                                    );

                                                                    if (newTag != null)
                                                                    {
                                                                        newTag.ChangeTypeId(candidate.TargetSym.Id);

                                                                        // 確保引線端點附著在管道上（LeaderEndCondition = Attached）
                                                                        try { newTag.LeaderEndCondition = LeaderEndCondition.Attached; } catch { }
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

        /// <summary>
        /// 計算元件在指定視圖 XY 裁切範圍內的可見長度（英呎）。
        /// 用於掃描現有標籤時，記錄已標注管道的視圖內可見長度，
        /// 與後續候選比較，判斷已標注的是否為最長管道。
        /// </summary>
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
                //int specialId = levelId.IntegerValue; // 2020
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

    /// <summary>
    /// 同一簽章（同系統+同標籤內容）的標籤候選，只保留視圖內可見長度最長的管道。
    /// 長管（>10m）以 currentSig（元件 ID）為 key，與 sysSig 路徑完全互斥，不需額外防重複。
    /// </summary>
    public class TagCandidate
    {
        public Reference ElemRef { get; set; }
        public FamilySymbol TargetSym { get; set; }
        public XYZ PlacementPt { get; set; }
        public double VisibleLength { get; set; }
        public string SysSig { get; set; }
    }

    /// <summary>
    /// 進度條視窗：顯示目前處理中的視圖名稱與完成百分比。
    /// </summary>
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
            this.ControlBox = false; // 不顯示關閉按鈕，避免被誤關

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

        /// <summary>推進一格並顯示目前處理的視圖名稱。</summary>
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