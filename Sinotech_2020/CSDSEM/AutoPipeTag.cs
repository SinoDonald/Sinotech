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

namespace Sinotech_2020.CSDSEM
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
                                    // =========================================================
                                    // 【新增結束】
                                    // =========================================================

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
                                            // 【系統級防重複機制】
                                            // alreadyTaggedSignatures：記錄「已被任意標籤標注過的管道實體 ID」
                                            //   → 防止對同一根管道重複建立標籤（無論標籤族類型）
                                            // taggedSystemSignatures：記錄「已被本程式目標標籤族標注過的系統 ID」
                                            //   → 防止同一系統在同一視圖中被標注兩次
                                            //   → 【關鍵】只有「目標標籤族」才能寫入此 set；
                                            //     非目標族的標籤不得污染此 set，否則會導致某系統
                                            //     因為「被其他族標過」而被誤跳過（Bug 2 根因）
                                            // =========================================================
                                            HashSet<string> alreadyTaggedSignatures = new HashSet<string>(); // 紀錄實體 ID
                                            HashSet<string> taggedSystemSignatures = new HashSet<string>();  // 紀錄系統 ID（僅目標標籤族）

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
                                                    Reference tagRef = tag.GetTaggedReference(); // 2020
                                                    //foreach (Reference tagRef in tag.GetTaggedReferences()) // 2024
                                                    //{
                                                        bool isLinked = tagRef.LinkedElementId != ElementId.InvalidElementId;
                                                        ElementId linkInstId = isLinked ? tagRef.ElementId : ElementId.InvalidElementId;

                                                        if (isLinked)
                                                        {
                                                            // 【設計決策】連結模型的既有標籤：
                                                            // 僅目標標籤族才寫入 alreadyTaggedSignatures，防止本次執行對同一管道重複建立。
                                                            // 非目標族不寫入，因為連結模型的標籤之後會全部關閉，
                                                            // 不應讓其他族的標籤封鎖本次執行對連結管道的標注。
                                                            if (isTargetTag)
                                                            {
                                                                alreadyTaggedSignatures.Add($"Linked_{tagRef.ElementId}_{tagRef.LinkedElementId}");
                                                            }
                                                            // taggedSystemSignatures：連結模型既有標籤一律不寫入（同上理由）
                                                        }
                                                        else
                                                        {
                                                            // 主模型：任何標籤族都寫入 alreadyTaggedSignatures，防止重複建立
                                                            alreadyTaggedSignatures.Add($"Local_{tagRef.ElementId}");

                                                            // 只有主模型 + 目標標籤族：才記錄系統 ID
                                                            if (isTargetTag)
                                                            {
                                                                Element taggedElem = doc.GetElement(tagRef.ElementId);
                                                                if (taggedElem != null && IsEligibleForTag(taggedElem))
                                                                {
                                                                    string sysSig = GetSystemSignature(taggedElem, false, ElementId.InvalidElementId);
                                                                    if (sysSig != null) taggedSystemSignatures.Add(sysSig);
                                                                }
                                                            }
                                                        }
                                                    //}
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

                                                // =========================================================
                                                // 【新增過濾條件：不進行標籤的物件】
                                                // =========================================================

                                                // 條件一：立管不標籤 (起終點的 X, Y 座標幾乎相同，給予 0.01 呎約 3mm 容差)
                                                if (Math.Abs(pt1.X - pt0.X) < 0.01 && Math.Abs(pt1.Y - pt0.Y) < 0.01)
                                                {
                                                    continue;
                                                }

                                                // 條件二：長度低於 1M 不標籤 (將英呎轉換為公尺)
                                                double lengthMeter = pt0.DistanceTo(pt1) * 0.3048;
                                                if (lengthMeter < 1.0)
                                                {
                                                    continue;
                                                }

                                                // 條件三：50mm(不含)以下的管徑不標籤 (僅針對水管 Pipe)
                                                if (elem is Pipe)
                                                {
                                                    Parameter diaParam = elem.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                                                    if (diaParam != null && diaParam.HasValue)
                                                    {
                                                        double diaMm = diaParam.AsDouble() * 304.8;
                                                        // 50mm(不含)以下代表小於50mm。加入浮點數容差(49.9)以防止將精確的 50mm 誤濾掉
                                                        if (diaMm < 49.9)
                                                        {
                                                            continue;
                                                        }
                                                    }
                                                }
                                                // =========================================================

                                                double minZ = Math.Min(pt0.Z, pt1.Z);
                                                double maxZ = Math.Max(pt0.Z, pt1.Z);

                                                if (maxZ < validZ_Min || minZ > validZ_Max)
                                                {
                                                    continue;
                                                }

                                                Reference pipeRef = mepItem.SourceProject.IsMainModel
                                                    ? new Reference(elem)
                                                    : new Reference(elem).CreateLinkReference(mepItem.SourceProject.LinkInstance);

                                                // =========================================================
                                                // 【Bug 3 修正 + Bug 1 修正】
                                                // 計算標籤放置點：
                                                //   - 管道兩端皆在視圖 XY 範圍內 → 取原始中點
                                                //   - 管道被視圖裁切 → 取「視圖內可見線段」的中點
                                                //   - 管道完全在視圖外 → 跳過（不建立標籤，不計數）
                                                // Bug 1 根因：原本 ClipSegmentToViewBounds 對「無 CropBox」
                                                //   視圖使用極大值，但子視圖本身有 CropBox，
                                                //   導致標籤點落在 CropBox 外仍被建立並計數。
                                                // =========================================================

                                                // 取得視圖的 XY 裁切範圍（世界座標）
                                                BoundingBoxXYZ cropBox = checkViewPlan.CropBox;
                                                Transform cropTransform = cropBox.Transform;

                                                double viewMinX, viewMinY, viewMaxX, viewMaxY;

                                                if (checkViewPlan.CropBoxActive)
                                                {
                                                    // CropBox 的 Min/Max 在視圖局部座標系，需透過 Transform 轉回世界座標
                                                    viewMinX = double.MaxValue; viewMinY = double.MaxValue;
                                                    viewMaxX = double.MinValue; viewMaxY = double.MinValue;
                                                    double[] localXs = new double[] { cropBox.Min.X, cropBox.Max.X };
                                                    double[] localYs = new double[] { cropBox.Min.Y, cropBox.Max.Y };
                                                    foreach (double lx in localXs)
                                                    {
                                                        foreach (double ly in localYs)
                                                        {
                                                            XYZ worldPt = cropTransform.OfPoint(new XYZ(lx, ly, 0));
                                                            if (worldPt.X < viewMinX) viewMinX = worldPt.X;
                                                            if (worldPt.Y < viewMinY) viewMinY = worldPt.Y;
                                                            if (worldPt.X > viewMaxX) viewMaxX = worldPt.X;
                                                            if (worldPt.Y > viewMaxY) viewMaxY = worldPt.Y;
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    // 無裁切框：不限制 XY 範圍
                                                    viewMinX = double.MinValue / 2;
                                                    viewMinY = double.MinValue / 2;
                                                    viewMaxX = double.MaxValue / 2;
                                                    viewMaxY = double.MaxValue / 2;
                                                }

                                                // 判斷兩端點是否皆在視圖 XY 範圍內（加小容差避免浮點誤差）
                                                const double xyTol = 1e-6;
                                                bool pt0InView = pt0.X >= viewMinX - xyTol && pt0.X <= viewMaxX + xyTol &&
                                                                 pt0.Y >= viewMinY - xyTol && pt0.Y <= viewMaxY + xyTol;
                                                bool pt1InView = pt1.X >= viewMinX - xyTol && pt1.X <= viewMaxX + xyTol &&
                                                                 pt1.Y >= viewMinY - xyTol && pt1.Y <= viewMaxY + xyTol;

                                                XYZ tagMidPoint;

                                                if (pt0InView && pt1InView)
                                                {
                                                    // 【情況 A】管道完全在視圖內：取原始兩端點中心
                                                    tagMidPoint = (pt0 + pt1) / 2.0;
                                                }
                                                else
                                                {
                                                    // 【情況 B】管道被視圖裁切：用 Liang-Barsky 取視圖內線段，再取其中心
                                                    XYZ clippedPt0, clippedPt1;
                                                    bool clipped = ClipSegmentToViewBounds(
                                                        pt0, pt1,
                                                        viewMinX, viewMaxX, viewMinY, viewMaxY,
                                                        out clippedPt0, out clippedPt1);

                                                    // 【Bug 1 修正】完全在視圖外：跳過，不建立標籤、不計數
                                                    if (!clipped) continue;

                                                    tagMidPoint = (clippedPt0 + clippedPt1) / 2.0;
                                                }

                                                double tagZ = tagMidPoint.Z;
                                                if (tagZ > exactZMax) tagZ = exactZMax - 0.01;
                                                if (tagZ < exactZMin) tagZ = exactZMin + 0.01;

                                                XYZ tagPlacementPoint = new XYZ(tagMidPoint.X, tagMidPoint.Y, tagZ);
                                                // =========================================================

                                                bool isLinked = !mepItem.SourceProject.IsMainModel;
                                                ElementId linkInstId = isLinked ? mepItem.SourceProject.LinkInstance.Id : ElementId.InvalidElementId;

                                                string sysSig = GetSystemSignature(elem, isLinked, linkInstId);

                                                // 【系統級防重複】主模型與連結模型在同一視圖內都只標一次。
                                                // 差異在於：連結模型的「既有標籤」不寫入 taggedSystemSignatures（掃描階段），
                                                // 但本次執行中新建的連結標籤仍會寫入，確保同一視圖內不重複。
                                                if (sysSig != null && taggedSystemSignatures.Contains(sysSig))
                                                {
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

                                                        // 本次執行中新建的標籤（主模型與連結模型）都寫入 taggedSystemSignatures，
                                                        // 確保同一視圖內同一系統不重複標注。
                                                        // 注意：掃描「既有標籤」階段，連結模型的標籤不寫入此 set，
                                                        // 因為這些標籤之後會被關閉，不應影響本次執行的判斷。
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
        /// 判斷管道元件是否符合「值得建立標籤」的條件，供掃描現有標籤與放置標籤兩處共用。
        /// 條件與放置標籤時的過濾條件完全一致：
        ///   1. 非立管（XY 位移需超過 0.01 呎）
        ///   2. 長度 >= 1M
        ///   3. 水管管徑 >= 50mm
        /// linkTransform 為 null 表示主模型元件，不需座標轉換。
        /// </summary>
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

            // 條件一：立管不標籤
            if (Math.Abs(pt1.X - pt0.X) < 0.01 && Math.Abs(pt1.Y - pt0.Y) < 0.01)
                return false;

            // 條件二：長度低於 1M 不標籤
            double lengthMeter = pt0.DistanceTo(pt1) * 0.3048;
            if (lengthMeter < 1.0)
                return false;

            // 條件三：水管管徑 50mm(不含)以下不標籤
            if (elem is Pipe)
            {
                Parameter diaParam = elem.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (diaParam != null && diaParam.HasValue)
                {
                    double diaMm = diaParam.AsDouble() * 304.8;
                    if (diaMm < 49.9) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 取得元件的「防重複簽章」。
        /// 防重複基準：同一來源（主/連結）+ 同一系統 + 同一標籤內容，才視為重複，只建立一次。
        /// 標籤內容由元件的尺寸、系統縮寫、高程參數組成，與標籤族實際顯示內容一致。
        /// 同一系統但尺寸或高程不同（標籤內容不同）→ 簽章不同 → 各自建立標籤。
        /// </summary>
        private string GetSystemSignature(Element elem, bool isLinked, ElementId linkInstanceId)
        {
            if (elem == null) return null;

            string prefix = isLinked ? $"Linked_{linkInstanceId.IntegerValue}_" : "Local_";

            if (elem is MEPCurve mepCurve)
            {
                string systemId = mepCurve.MEPSystem != null
                    ? mepCurve.MEPSystem.Id.IntegerValue.ToString()
                    : "NoSys_" + elem.Id.IntegerValue.ToString();

                // 取得標籤內容相關參數，組成與標籤顯示一致的簽章
                string tagContent = GetTagContentSignature(elem);

                return $"{prefix}System_{systemId}__{tagContent}";
            }

            if (elem is CableTray)
            {
                string cableNum = elem.LookupParameter("電纜編號")?.AsString() ??
                                  elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ??
                                  elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ??
                                  elem.Id.IntegerValue.ToString();
                return $"{prefix}TraySystem_{cableNum}";
            }

            return $"{prefix}Isolated_{elem.Id.IntegerValue}";
        }

        /// <summary>
        /// 從元件參數組出與標籤族顯示內容一致的字串，作為防重複簽章的一部分。
        /// Pipe  → 管徑(mm) + 系統縮寫 + 管底高程(mm)
        /// Duct  → 寬(mm) x 高(mm) + 系統縮寫 + 中心高程(mm)
        /// </summary>
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
                    // 管徑 (mm)
                    double diaMm = Math.Round((elem.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble() ?? 0) * 304.8);
                    // 管底高程 (mm)：中心高程 - 半徑
                    double centerElev = (elem.get_Parameter(BuiltInParameter.RBS_PIPE_BOTTOM_ELEVATION)?.AsDouble() ?? 0) * 304.8;
                    double elevMm = Math.Round(centerElev);
                    return $"P_{diaMm}_{sysAbbr}_{elevMm}";
                }

                if (elem is Duct)
                {
                    // 風管：寬 x 高 (mm)
                    double widthMm = Math.Round((elem.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.AsDouble() ?? 0) * 304.8);
                    double heightMm = Math.Round((elem.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble() ?? 0) * 304.8);
                    // 中心高程 (mm)
                    double centerElev = (elem.get_Parameter(BuiltInParameter.RBS_DUCT_BOTTOM_ELEVATION)?.AsDouble() ?? 0) * 304.8;
                    double elevMm = Math.Round(centerElev);
                    return $"D_{widthMm}x{heightMm}_{sysAbbr}_{elevMm}";
                }
            }
            catch { }

            // fallback：用元件 Id，確保不會誤跳過
            return $"Elem_{elem.Id.IntegerValue}";
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
                //int specialId = levelId.IntegerValue; // 2020
                long specialId = levelId.IntegerValue;
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

        /// <summary>
        /// 將線段 (p0→p1) 裁切到矩形範圍 [xMin,xMax]×[yMin,yMax]。
        /// 回傳 true 表示裁切後線段有效（至少部分在範圍內）；
        /// out 參數為裁切後的兩端點（Z 值以線性插值計算）。
        /// </summary>
        private bool ClipSegmentToViewBounds(
            XYZ p0, XYZ p1,
            double xMin, double xMax,
            double yMin, double yMax,
            out XYZ clipped0, out XYZ clipped1)
        {
            // 使用參數式裁切（Liang-Barsky 演算法）
            // 線段表示為 P(t) = p0 + t*(p1-p0)，t ∈ [0,1]
            double dx = p1.X - p0.X;
            double dy = p1.Y - p0.Y;
            double dz = p1.Z - p0.Z;

            double tMin = 0.0;
            double tMax = 1.0;

            // 對四個邊界各做一次參數裁切
            // 左邊界 (x >= xMin)：p = -dx, q = p0.X - xMin
            // 右邊界 (x <= xMax)：p =  dx, q = xMax - p0.X
            // 下邊界 (y >= yMin)：p = -dy, q = p0.Y - yMin
            // 上邊界 (y <= yMax)：p =  dy, q = yMax - p0.Y
            double[] p = new double[] { -dx, dx, -dy, dy };
            double[] q = new double[] {
        p0.X - xMin,
        xMax - p0.X,
        p0.Y - yMin,
        yMax - p0.Y
    };

            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(p[i]) < 1e-10) // 平行於此邊界
                {
                    if (q[i] < 0)
                    {
                        // 線段完全在此邊界外
                        clipped0 = p0;
                        clipped1 = p1;
                        return false;
                    }
                    // 否則此邊界不限制，繼續
                }
                else
                {
                    double t = q[i] / p[i];
                    if (p[i] < 0)
                    {
                        // 進入邊界：更新 tMin
                        if (t > tMin) tMin = t;
                    }
                    else
                    {
                        // 離開邊界：更新 tMax
                        if (t < tMax) tMax = t;
                    }
                }

                if (tMin > tMax)
                {
                    // 線段完全在範圍外
                    clipped0 = p0;
                    clipped1 = p1;
                    return false;
                }
            }

            // 計算裁切後端點（Z 值線性插值）
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
}