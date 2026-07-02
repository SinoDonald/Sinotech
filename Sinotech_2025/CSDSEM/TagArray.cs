using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace Sinotech_2025.CSDSEM
{
    [Transaction(TransactionMode.Manual)]
    public class TagArray : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            AutoNumberForm autoNumberForm = new AutoNumberForm(doc);
            autoNumberForm.ShowDialog();
            if (autoNumberForm.trueOrFalse != true) return Result.Cancelled;

            List<ViewPlan> viewPlans = AutoPipeTag.GetAutoNumberViewPlans(doc, autoNumberForm.viewFamilyTypeName);
            ChooseMultiViewPlansForm chooseForm = new ChooseMultiViewPlansForm(doc, viewPlans, ChooseMultiViewPlansForm.FormMode.TagArray);

            if (chooseForm.ShowDialog() != DialogResult.OK) return Result.Cancelled;

            List<ViewPlan> selectedViews = chooseForm.checkViewPlans;
            if (selectedViews == null || selectedViews.Count == 0) return Result.Failed;

            bool isAutoMode = chooseForm.IsAutoResult;

            List<ProjectItem> availableProjects = new List<ProjectItem> { new ProjectItem(doc) };
            FilteredElementCollector linkCollector = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance));
            foreach (RevitLinkInstance linkInst in linkCollector.Cast<RevitLinkInstance>())
            {
                Document linkedDoc = linkInst.GetLinkDocument();
                if (linkedDoc != null) availableProjects.Add(new ProjectItem(linkedDoc, linkInst));
            }

            BuiltInCategory[] targetCategories = new BuiltInCategory[]
            {
                BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Walls, BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_CableTray, BuiltInCategory.OST_PipeTags, BuiltInCategory.OST_DuctTags,
                BuiltInCategory.OST_CableTrayTags, BuiltInCategory.OST_MultiCategoryTags
            };
            ElementMulticategoryFilter multiCatFilter = new ElementMulticategoryFilter(targetCategories);

            List<BuiltInCategory> tagCategories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_PipeTags, BuiltInCategory.OST_DuctTags, BuiltInCategory.OST_CableTrayTags
            };
            ElementMulticategoryFilter tagFilter = new ElementMulticategoryFilter(tagCategories);

            int grandTotalMovedTags = 0;

            if (isAutoMode)
            {
                using (Transaction trans = new Transaction(doc, "自動標籤排序"))
                {
                    trans.Start();
                    foreach (ViewPlan viewPlan in selectedViews)
                    {
                        grandTotalMovedTags += ProcessViewTags(doc, viewPlan, true, multiCatFilter, tagFilter, availableProjects, null);
                    }
                    trans.Commit();
                }
            }
            else
            {
                Dictionary<ViewPlan, List<PickedBox>> allPickedBoxes = new Dictionary<ViewPlan, List<PickedBox>>();

                foreach (ViewPlan viewPlan in selectedViews)
                {
                    if (uidoc.ActiveView.Id != viewPlan.Id)
                    {
                        uidoc.ActiveView = viewPlan;
                    }

                    List<PickedBox> pickedBoxes = new List<PickedBox>();
                    TaskDialog.Show("手動框選模式", $"目前視圖: 【{viewPlan.Name}】\n\n操作說明：\n1. 滑鼠左鍵拖曳框選標籤放置區\n2. 框選完畢請按鍵盤 [ESC] 鍵結束此視圖。");

                    while (true)
                    {
                        try
                        {
                            PickedBox box = uidoc.Selection.PickBox(PickBoxStyle.Directional, "請框選標籤放置的矩形範圍 (完成請按鍵盤 ESC 鍵結束)");
                            pickedBoxes.Add(box);
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break;
                        }
                    }

                    if (pickedBoxes.Count > 0)
                    {
                        allPickedBoxes.Add(viewPlan, pickedBoxes);
                    }
                }

                if (allPickedBoxes.Count > 0)
                {
                    using (Transaction trans = new Transaction(doc, "手動標籤排序"))
                    {
                        trans.Start();
                        foreach (var kvp in allPickedBoxes)
                        {
                            grandTotalMovedTags += ProcessViewTags(doc, kvp.Key, false, multiCatFilter, tagFilter, availableProjects, kvp.Value);
                        }
                        trans.Commit();
                    }
                }
            }

            TaskDialog.Show("Revit", $"智慧排版處理完畢！\n共將 {grandTotalMovedTags} 個標籤移動至安全區並精準對齊。");
            return Result.Succeeded;
        }

        // =========================================================================
        // 【核心大腦】動態邊界分析與群組排版引擎
        // =========================================================================
        private int ProcessViewTags(Document doc, ViewPlan viewPlan, bool isAutoMode, ElementMulticategoryFilter multiCatFilter, ElementMulticategoryFilter tagFilter, List<ProjectItem> availableProjects, List<PickedBox> pickedBoxes)
        {
            List<IndependentTag> existingTags = new FilteredElementCollector(doc, viewPlan.Id)
                .WherePasses(tagFilter).OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
                .Where(x => x.Category.BuiltInCategory != BuiltInCategory.OST_DuctTags)
                .ToList();

            if (existingTags.Count == 0) return 0;

            double tagW = 1000.0 / 304.8;
            double tagH = 300.0 / 304.8;

            IndependentTag sampleTag = new FilteredElementCollector(doc, viewPlan.Id)
                .WherePasses(tagFilter).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().FirstOrDefault();

            if (sampleTag != null)
            {
                BoundingBoxXYZ tagBbox = sampleTag.get_BoundingBox(viewPlan);
                if (tagBbox != null)
                {
                    double w = tagBbox.Max.X - tagBbox.Min.X;
                    double h = tagBbox.Max.Y - tagBbox.Min.Y;
                    if (w > 0 && w < 15.0) tagW = w;
                    if (h > 0 && h < 15.0) tagH = h;
                }
            }

            double gapX = 30.0 / 304.8;
            double gapY = 30.0 / 304.8;
            double slotW = tagW + gapX;
            double slotH = tagH + gapY + 0.3;

            double exactZMin = GetPlaneElevation(viewPlan, PlanViewPlane.ViewDepthPlane, 1000.0, -1000.0);
            double exactCutZ = viewPlan.GenLevel != null ? viewPlan.GenLevel.Elevation : exactZMin;

            List<SafeRegion> safeRegions = new List<SafeRegion>();

            if (isAutoMode)
            {
                double exactZMax = GetPlaneElevation(viewPlan, PlanViewPlane.TopClipPlane, 1000.0, -1000.0);
                double validZ_Min = exactZMin - 0.5;
                double validZ_Max = exactZMax + 0.5;

                double viewMinX = -2000.0, viewMinY = -2000.0, viewMaxX = 2000.0, viewMaxY = 2000.0;
                BoundingBoxXYZ cb = viewPlan.CropBox;
                if (viewPlan.CropBoxActive)
                {
                    Transform ct = cb.Transform;
                    viewMinX = double.MaxValue; viewMinY = double.MaxValue; viewMaxX = double.MinValue; viewMaxY = double.MinValue;
                    foreach (double lx in new[] { cb.Min.X, cb.Max.X })
                        foreach (double ly in new[] { cb.Min.Y, cb.Max.Y })
                        {
                            XYZ wp = ct.OfPoint(new XYZ(lx, ly, 0));
                            if (wp.X < viewMinX) viewMinX = wp.X; if (wp.Y < viewMinY) viewMinY = wp.Y;
                            if (wp.X > viewMaxX) viewMaxX = wp.X; if (wp.Y > viewMaxY) viewMaxY = wp.Y;
                        }
                }

                List<int[]> autoRectangles = GenerateAutoRectangles(doc, viewPlan, availableProjects, multiCatFilter, viewMinX, viewMaxX, viewMinY, viewMaxY, exactCutZ, validZ_Min, validZ_Max, tagW, tagH);
                foreach (var rect in autoRectangles)
                {
                    double pMinX = viewMinX + rect[0] * tagW;
                    double pMinY = viewMinY + rect[2] * tagH;
                    double pMaxX = viewMinX + (rect[1] + 1) * tagW;
                    double pMaxY = viewMinY + (rect[3] + 1) * tagH;
                    SafeRegion region = CreateSafeRegion(pMinX, pMaxX, pMinY, pMaxY, exactCutZ, slotW, slotH);
                    if (region != null) safeRegions.Add(region);
                }
            }
            else
            {
                if (pickedBoxes != null)
                {
                    foreach (PickedBox box in pickedBoxes)
                    {
                        double pMinX = Math.Min(box.Min.X, box.Max.X);
                        double pMaxX = Math.Max(box.Min.X, box.Max.X);
                        double pMinY = Math.Min(box.Min.Y, box.Max.Y);
                        double pMaxY = Math.Max(box.Min.Y, box.Max.Y);
                        SafeRegion region = CreateSafeRegion(pMinX, pMaxX, pMinY, pMaxY, exactCutZ, slotW, slotH);
                        if (region != null) safeRegions.Add(region);
                    }
                }
            }

            int movedCount = 0;

            // =========================================================
            // 【群組填入邏輯】：解決跳行問題，確保框選區依序填滿
            // =========================================================
            Dictionary<SafeRegion, List<IndependentTag>> regionAssignments = new Dictionary<SafeRegion, List<IndependentTag>>();
            foreach (var r in safeRegions) regionAssignments[r] = new List<IndependentTag>();

            // 步驟 A：初步分發標籤到最靠近的框選區
            foreach (IndependentTag tag in existingTags)
            {
                if (tag.IsOrphaned) continue;
                XYZ pos;
                try { pos = tag.TagHeadPosition; } catch { continue; }

                SafeRegion closestRegion = safeRegions.OrderBy(r => r.TopLeft.DistanceTo(pos)).FirstOrDefault();
                if (closestRegion != null)
                {
                    regionAssignments[closestRegion].Add(tag);
                }
            }

            List<IndependentTag> overflowTags = new List<IndependentTag>();

            // 步驟 B：在各個框選區內排序
            foreach (var kvp in regionAssignments)
            {
                SafeRegion region = kvp.Key;

                // 【防交叉演算法】：
                // 1. 水管標籤優先於電纜架標籤
                // 2. 以管線實際 Y 座標進行排序 (由上而下)
                // 3. 以管線實際 X 座標進行排序 (由左至右)
                List<IndependentTag> tagsInRegion = kvp.Value
                    .OrderBy(t => t.Category.Id.Value == (long)BuiltInCategory.OST_PipeTags ? 0 : 1)
                    .ThenByDescending(t => GetTagLeaderEndSafe(doc, t).Y)
                    .ThenBy(t => GetTagLeaderEndSafe(doc, t).X)
                    .ToList();

                foreach (var tag in tagsInRegion)
                {
                    if (!region.IsFull)
                    {
                        ApplyTagToSlot(doc, tag, region, exactCutZ, tagW);
                        movedCount++;
                    }
                    else
                    {
                        overflowTags.Add(tag);
                    }
                }
            }

            // 步驟 C：處理塞不下的溢出標籤，尋找下一個框
            foreach (var tag in overflowTags)
            {
                XYZ pos;
                try { pos = tag.TagHeadPosition; } catch { continue; }

                SafeRegion nextBestRegion = safeRegions.Where(r => !r.IsFull).OrderBy(r => r.TopLeft.DistanceTo(pos)).FirstOrDefault();
                if (nextBestRegion != null)
                {
                    ApplyTagToSlot(doc, tag, nextBestRegion, exactCutZ, tagW);
                    movedCount++;
                }
            }

            return movedCount;
        }

        // 取得真實的管線附著點做為防交叉排序依據
        private XYZ GetTagLeaderEndSafe(Document doc, IndependentTag tag)
        {
            try
            {
                if (tag.HasLeader)
                {
                    Reference r = tag.GetTaggedReferences().FirstOrDefault();
                    if (r != null) return tag.GetLeaderEnd(r);
                }
            }
            catch { }
            // 若因為手動設置為無引線，則 TagHeadPosition 即為管線位置
            try { return tag.TagHeadPosition; } catch { return XYZ.Zero; }
        }

        // =========================================================================
        // 【移動並修正 引線頭尾錨點】核心函式
        // =========================================================================
        private void ApplyTagToSlot(Document doc, IndependentTag tag, SafeRegion region, double exactCutZ, double tagW)
        {
            XYZ targetTopLeft = region.Slots[region.NextSlotIndex++];

            // 1. 移動標籤到格子左上角
            XYZ newHeadPos = new XYZ(targetTopLeft.X, targetTopLeft.Y, exactCutZ);
            if (tag.Name.Contains("管_尺寸+系統"))
            {
                newHeadPos = new XYZ(targetTopLeft.X + 5.8, targetTopLeft.Y, exactCutZ);
            }
            try
            {
                tag.TagHeadPosition = newHeadPos;

                // 2. 以基準的方式排序後的標籤, 才開啟引線並設定自由端點
                tag.HasLeader = true;
                tag.LeaderEndCondition = LeaderEndCondition.Free;

                Reference taggedRef = tag.GetTaggedReferences().FirstOrDefault();
                if (taggedRef != null)
                {
                    // 取得真正附著在管線上的座標點
                    XYZ endPt = tag.GetLeaderEnd(taggedRef);

                    // =========================================================
                    // 【智慧錨點】：引線只接在文字的頭或尾，不穿越文字
                    // =========================================================
                    double textLeft = newHeadPos.X;
                    double textRight = newHeadPos.X + tagW;
                    double midX = (textLeft + textRight) / 2.0;

                    double elbowGap = 10.0 / 304.8; // 預留 10mm 安全間距避免貼太緊
                    double elbowX = endPt.X;

                    // 若管線附著點在文字上下方，強制將 Elbow 推到文字頭或尾
                    if (elbowX >= textLeft - elbowGap && elbowX <= textRight + elbowGap)
                    {
                        if (elbowX < midX)
                        {
                            elbowX = textLeft - elbowGap; // 連接頭部 (左側)
                        }
                        else
                        {
                            elbowX = textRight + elbowGap; // 連接尾部 (右側)
                        }
                    }

                    // 給它們90度的轉折：Y維持標籤高度，X強制移至管線或頭尾避讓區
                    XYZ elbowPt = new XYZ(elbowX, newHeadPos.Y, exactCutZ);
                    tag.SetLeaderElbow(taggedRef, elbowPt);
                }
            }
            catch { }
        }

        // =========================================================================
        // 共用方法區：停車場(SafeRegion)建立邏輯
        // =========================================================================
        public class SafeRegion
        {
            public XYZ TopLeft { get; set; }
            public List<XYZ> Slots { get; set; } = new List<XYZ>();
            public int NextSlotIndex { get; set; } = 0;
            public bool IsFull => NextSlotIndex >= Slots.Count;
        }

        private SafeRegion CreateSafeRegion(double pMinX, double pMaxX, double pMinY, double pMaxY, double exactCutZ, double slotW, double slotH)
        {
            SafeRegion region = new SafeRegion();
            region.TopLeft = new XYZ(pMinX, pMaxY, exactCutZ);

            int slotCols = (int)((pMaxX - pMinX) / slotW);
            int slotRows = (int)((pMaxY - pMinY) / slotH);

            if (slotCols < 1 || slotRows < 1) return null;

            double startX = pMinX;
            double startY = pMaxY;

            // 【保留你的客製化寬度間距】由左至右，由上而下排下去
            for (int c = 0; c < slotCols; c++)
            {
                for (int r = 0; r < slotRows; r++)
                {
                    double cx = startX + c * (slotW + 1); // 你的客製參數 +1
                    double cy = startY - r * slotH;
                    region.Slots.Add(new XYZ(cx, cy, exactCutZ));
                }
            }
            return region;
        }

        // =========================================================================
        // 【自動模式】核心生成邏輯
        // =========================================================================
        private List<int[]> GenerateAutoRectangles(Document doc, ViewPlan viewPlan, List<ProjectItem> availableProjects, ElementMulticategoryFilter multiCatFilter, double viewMinX, double viewMaxX, double viewMinY, double viewMaxY, double exactCutZ, double validZ_Min, double validZ_Max, double tagW, double tagH)
        {
            List<int[]> finalRectangles = new List<int[]>();
            int minTagsFit = 5;

            List<CurveLoop> baseLoops = new List<CurveLoop>();
            CurveLoop viewExtentLoop = new CurveLoop();
            viewExtentLoop.Append(Line.CreateBound(new XYZ(viewMinX, viewMinY, exactCutZ), new XYZ(viewMaxX, viewMinY, exactCutZ)));
            viewExtentLoop.Append(Line.CreateBound(new XYZ(viewMaxX, viewMinY, exactCutZ), new XYZ(viewMaxX, viewMaxY, exactCutZ)));
            viewExtentLoop.Append(Line.CreateBound(new XYZ(viewMaxX, viewMaxY, exactCutZ), new XYZ(viewMinX, viewMaxY, exactCutZ)));
            viewExtentLoop.Append(Line.CreateBound(new XYZ(viewMinX, viewMaxY, exactCutZ), new XYZ(viewMinX, viewMinY, exactCutZ)));
            baseLoops.Add(viewExtentLoop);

            Solid emptyAreaSolid = GeometryCreationUtilities.CreateExtrusionGeometry(baseLoops, XYZ.BasisZ, 0.1);
            BoundingBoxXYZ cb = viewPlan.CropBox;

            foreach (ProjectItem projItem in availableProjects)
            {
                List<Element> validElements = new List<Element>();
                if (projItem.IsMainModel)
                {
                    validElements = new FilteredElementCollector(doc, viewPlan.Id).WherePasses(multiCatFilter).WhereElementIsNotElementType().ToList();
                }
                else
                {
                    Transform invTransform = projItem.LinkInstance.GetTotalTransform().Inverse;
                    Outline linkOutline = GetTransformedOutline(viewPlan, cb, invTransform, validZ_Min, validZ_Max);
                    validElements = new FilteredElementCollector(projItem.Doc).WherePasses(multiCatFilter).WherePasses(new BoundingBoxIntersectsFilter(linkOutline)).WhereElementIsNotElementType().ToList();
                }

                foreach (Element elem in validElements)
                {
                    Transform linkXform = projItem.IsMainModel ? null : projItem.LinkInstance.GetTotalTransform();
                    if (elem.Location is LocationCurve locCurve && locCurve.Curve != null)
                    {
                        double visLen = GetVisibleLengthInView(elem, linkXform, viewMinX, viewMaxX, viewMinY, viewMaxY);
                        if (visLen <= 0) continue;
                    }

                    GeometryElement geomElem = elem.get_Geometry(new Options { View = viewPlan });
                    if (geomElem == null) continue;

                    foreach (GeometryObject geomObj in geomElem)
                    {
                        if (geomObj is GeometryInstance geomInst)
                        {
                            foreach (GeometryObject instObj in geomInst.GetInstanceGeometry())
                            {
                                if (instObj is Solid s && s.Volume > 0)
                                    emptyAreaSolid = SubtractSolid2D(emptyAreaSolid, linkXform != null ? SolidUtils.CreateTransformed(s, linkXform) : s);
                            }
                        }
                        else if (geomObj is Solid solid && solid.Volume > 0)
                        {
                            emptyAreaSolid = SubtractSolid2D(emptyAreaSolid, linkXform != null ? SolidUtils.CreateTransformed(solid, linkXform) : solid);
                        }
                    }
                }
            }

            if (emptyAreaSolid == null) return finalRectangles;

            PlanarFace topFace = null;
            foreach (Face face in emptyAreaSolid.Faces)
            {
                if (face is PlanarFace pf && pf.FaceNormal.IsAlmostEqualTo(XYZ.BasisZ, 0.01))
                {
                    topFace = pf; break;
                }
            }
            if (topFace == null) return finalRectangles;

            int cols = (int)Math.Ceiling((viewMaxX - viewMinX) / tagW);
            int rows = (int)Math.Ceiling((viewMaxY - viewMinY) / tagH);
            bool[,] isFree = new bool[cols, rows];

            double zTop = topFace.Origin.Z;
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    double x = viewMinX + c * tagW + tagW / 2;
                    double y = viewMinY + r * tagH + tagH / 2;
                    XYZ testPt = new XYZ(x, y, zTop);
                    IntersectionResult ir = topFace.Project(testPt);
                    if (ir != null && ir.Distance < 0.01 && topFace.IsInside(ir.UVPoint))
                    {
                        isFree[c, r] = true;
                    }
                }
            }

            bool[,] isExterior = new bool[cols, rows];
            Queue<int[]> queue = new Queue<int[]>();
            for (int c = 0; c < cols; c++)
            {
                for (int r = 0; r < rows; r++)
                {
                    if (c == 0 || c == cols - 1 || r == 0 || r == rows - 1)
                    {
                        if (isFree[c, r])
                        {
                            isExterior[c, r] = true;
                            queue.Enqueue(new int[] { c, r });
                        }
                    }
                }
            }

            int[] dc = { -1, 1, 0, 0 };
            int[] dr = { 0, 0, -1, 1 };
            while (queue.Count > 0)
            {
                int[] curr = queue.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    int nc = curr[0] + dc[i];
                    int nr = curr[1] + dr[i];
                    if (nc >= 0 && nc < cols && nr >= 0 && nr < rows)
                    {
                        if (isFree[nc, nr] && !isExterior[nc, nr])
                        {
                            isExterior[nc, nr] = true;
                            queue.Enqueue(new int[] { nc, nr });
                        }
                    }
                }
            }

            while (true)
            {
                int maxArea = 0;
                int bestMinC = 0, bestMaxC = 0, bestMinR = 0, bestMaxR = 0;
                int[] heights = new int[cols];

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                        heights[c] = isExterior[c, r] ? heights[c] + 1 : 0;

                    for (int c = 0; c < cols; c++)
                    {
                        int minH = heights[c];
                        for (int c2 = c; c2 < cols; c2++)
                        {
                            minH = Math.Min(minH, heights[c2]);
                            if (minH == 0) break;

                            int area = minH * (c2 - c + 1);
                            if (area > maxArea)
                            {
                                maxArea = area;
                                bestMinC = c; bestMaxC = c2;
                                bestMinR = r - minH + 1; bestMaxR = r;
                            }
                        }
                    }
                }

                if (maxArea < minTagsFit) break;
                finalRectangles.Add(new int[] { bestMinC, bestMaxC, bestMinR, bestMaxR });

                for (int c = bestMinC; c <= bestMaxC; c++)
                    for (int r = bestMinR; r <= bestMaxR; r++)
                        isExterior[c, r] = false;
            }

            return finalRectangles;
        }

        // =========================================================================
        // 原有幾何核心輔助方法（不變）
        // =========================================================================
        private double GetVisibleLengthInView(Element elem, Transform linkTransform, double viewMinX, double viewMaxX, double viewMinY, double viewMaxY)
        {
            try
            {
                if (!(elem.Location is LocationCurve lc) || lc.Curve == null) return 0;
                XYZ p0 = lc.Curve.GetEndPoint(0); XYZ p1 = lc.Curve.GetEndPoint(1);
                if (linkTransform != null) { p0 = linkTransform.OfPoint(p0); p1 = linkTransform.OfPoint(p1); }
                const double tol = 1e-6;
                bool p0In = p0.X >= viewMinX - tol && p0.X <= viewMaxX + tol && p0.Y >= viewMinY - tol && p0.Y <= viewMaxY + tol;
                bool p1In = p1.X >= viewMinX - tol && p1.X <= viewMaxX + tol && p1.Y >= viewMinY - tol && p1.Y <= viewMaxY + tol;
                if (p0In && p1In) return p0.DistanceTo(p1);
                XYZ c0, c1;
                if (ClipSegmentToViewBounds(p0, p1, viewMinX, viewMaxX, viewMinY, viewMaxY, out c0, out c1)) return c0.DistanceTo(c1);
            }
            catch { }
            return 0;
        }

        private bool ClipSegmentToViewBounds(XYZ p0, XYZ p1, double xMin, double xMax, double yMin, double yMax, out XYZ clipped0, out XYZ clipped1)
        {
            double dx = p1.X - p0.X; double dy = p1.Y - p0.Y; double dz = p1.Z - p0.Z;
            double tMin = 0.0; double tMax = 1.0;
            double[] p = new double[] { -dx, dx, -dy, dy };
            double[] q = new double[] { p0.X - xMin, xMax - p0.X, p0.Y - yMin, yMax - p0.Y };
            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(p[i]) < 1e-10) { if (q[i] < 0) { clipped0 = p0; clipped1 = p1; return false; } }
                else
                {
                    double t = q[i] / p[i];
                    if (p[i] < 0) { if (t > tMin) tMin = t; } else { if (t < tMax) tMax = t; }
                }
                if (tMin > tMax) { clipped0 = p0; clipped1 = p1; return false; }
            }
            clipped0 = new XYZ(p0.X + tMin * dx, p0.Y + tMin * dy, p0.Z + tMin * dz);
            clipped1 = new XYZ(p0.X + tMax * dx, p0.Y + tMax * dy, p0.Z + tMax * dz);
            return true;
        }

        private double GetPlaneElevation(ViewPlan view, PlanViewPlane plane, double defaultHigh, double defaultLow)
        {
            PlanViewRange viewRange = view.GetViewRange();
            ElementId levelId = viewRange.GetLevelId(plane);
            double offset = viewRange.GetOffset(plane);
            if (levelId == ElementId.InvalidElementId) return plane == PlanViewPlane.TopClipPlane ? defaultHigh : defaultLow;
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
            double lMinX = view.CropBoxActive ? viewBBox.Min.X : -100000.0;
            double lMinY = view.CropBoxActive ? viewBBox.Min.Y : -100000.0;
            double lMaxX = view.CropBoxActive ? viewBBox.Max.X : 100000.0;
            double lMaxY = view.CropBoxActive ? viewBBox.Max.Y : 100000.0;
            XYZ[] hostCorners = new XYZ[4] {
                viewToHostTransform.OfPoint(new XYZ(lMinX, lMinY, 0)), viewToHostTransform.OfPoint(new XYZ(lMaxX, lMinY, 0)),
                viewToHostTransform.OfPoint(new XYZ(lMinX, lMaxY, 0)), viewToHostTransform.OfPoint(new XYZ(lMaxX, lMaxY, 0))
            };
            List<XYZ> worldPoints = new List<XYZ>();
            foreach (XYZ pt in hostCorners) { worldPoints.Add(new XYZ(pt.X, pt.Y, hostZMin)); worldPoints.Add(new XYZ(pt.X, pt.Y, hostZMax)); }
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (XYZ pt in worldPoints)
            {
                XYZ linkPt = hostToLinkTransform.OfPoint(pt);
                if (linkPt.X < minX) minX = linkPt.X; if (linkPt.Y < minY) minY = linkPt.Y; if (linkPt.Z < minZ) minZ = linkPt.Z;
                if (linkPt.X > maxX) maxX = linkPt.X; if (linkPt.Y > maxY) maxY = linkPt.Y; if (linkPt.Z > maxZ) maxZ = linkPt.Z;
            }
            return new Outline(new XYZ(minX - 5.0, minY - 5.0, minZ - 1.0), new XYZ(maxX + 5.0, maxY + 5.0, maxZ + 1.0));
        }

        private Solid SubtractSolid2D(Solid baseSolid, Solid subtractorSolid)
        {
            try
            {
                Solid result = BooleanOperationsUtils.ExecuteBooleanOperation(baseSolid, subtractorSolid, BooleanOperationsType.Difference);
                if (result != null && result.Edges.Size > 0) return result;
            }
            catch { }
            return baseSolid;
        }
    }
}