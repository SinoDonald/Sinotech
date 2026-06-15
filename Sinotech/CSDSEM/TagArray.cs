using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace Sinotech.CSDSEM
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

            // =========================================================
            // 模式分流：【自動模式】 vs 【手動模式】
            // =========================================================
            if (isAutoMode)
            {
                // 【修改1】將 foreach 包在一個 Transaction 內部，確保只有一個復原紀錄
                using (Transaction trans = new Transaction(doc, "自動標籤排序"))
                {
                    trans.Start();

                    foreach (ViewPlan viewPlan in selectedViews)
                    {
                        double exactZMax = GetPlaneElevation(viewPlan, PlanViewPlane.TopClipPlane, 1000.0, -1000.0);
                        double exactZMin = GetPlaneElevation(viewPlan, PlanViewPlane.ViewDepthPlane, 1000.0, -1000.0);
                        double exactCutZ = viewPlan.GenLevel != null ? viewPlan.GenLevel.Elevation : exactZMin;
                        double validZ_Min = exactZMin - 0.5;
                        double validZ_Max = exactZMax + 0.5;

                        // 1. 獲取單一標籤的物理尺寸 (精準高度)
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

                        // 【修改3】極度縮小間距，確保排版緊密 (原15cm -> 改為 1cm 高度間距)
                        double gapX = 30.0 / 304.8; // 左右間距 3cm
                        double gapY = 10.0 / 304.8; // 上下間距 1cm 
                        double slotW = tagW + gapX;
                        double slotH = tagH + gapY;

                        // 視圖邊界計算
                        double viewMinX, viewMinY, viewMaxX, viewMaxY;
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
                        else
                        {
                            viewMinX = -2000.0; viewMinY = -2000.0; viewMaxX = 2000.0; viewMaxY = 2000.0;
                        }

                        List<SafeRegion> safeRegions = new List<SafeRegion>();
                        List<int[]> autoRectangles = GenerateAutoRectangles(doc, viewPlan, availableProjects, multiCatFilter, viewMinX, viewMaxX, viewMinY, viewMaxY, exactCutZ, validZ_Min, validZ_Max, tagW, tagH);

                        // SketchPlane sketchPlane = CreateSketchPlaneForZ(doc, exactCutZ); // 如果要畫線需解除此註解

                        foreach (var rect in autoRectangles)
                        {
                            double pMinX = viewMinX + rect[0] * tagW;
                            double pMinY = viewMinY + rect[2] * tagH;
                            double pMaxX = viewMinX + (rect[1] + 1) * tagW;
                            double pMaxY = viewMinY + (rect[3] + 1) * tagH;

                            // 【修改2】隱藏畫線
                            // DrawRectangleLines(doc, sketchPlane, pMinX, pMaxX, pMinY, pMaxY, exactCutZ);

                            SafeRegion region = CreateSafeRegion(pMinX, pMaxX, pMinY, pMaxY, exactCutZ, slotW, slotH);
                            if (region != null) safeRegions.Add(region);
                        }

                        grandTotalMovedTags += MoveTagsToSafeRegions(doc, viewPlan, tagFilter, safeRegions);
                    }

                    trans.Commit();
                }
            }
            else
            {
                // =========================================================
                // 【手動框選模式】兩階段處理
                // =========================================================

                // 【第一階段】：收集所有視圖的框選 (不開啟 Transaction，避免切換視圖報錯)
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
                            // 【修改2】取消了即時畫線的回饋，所以不再需要 tDraw
                        }
                        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                        {
                            break; // 按下 ESC
                        }
                    }

                    if (pickedBoxes.Count > 0)
                    {
                        allPickedBoxes.Add(viewPlan, pickedBoxes);
                    }
                }

                // 【第二階段】：【修改1】將收集好的框選矩形，包在單一 Transaction 內進行排版
                if (allPickedBoxes.Count > 0)
                {
                    using (Transaction trans = new Transaction(doc, "手動標籤排序"))
                    {
                        trans.Start();

                        foreach (var kvp in allPickedBoxes)
                        {
                            ViewPlan viewPlan = kvp.Key;
                            List<PickedBox> pickedBoxes = kvp.Value;

                            double exactZMin = GetPlaneElevation(viewPlan, PlanViewPlane.ViewDepthPlane, 1000.0, -1000.0);
                            double exactCutZ = viewPlan.GenLevel != null ? viewPlan.GenLevel.Elevation : exactZMin;

                            // 1. 獲取單一標籤的物理尺寸 (精準高度)
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

                            // 【修改3】縮小間距，確保排版緊密 
                            double gapX = 30.0 / 304.8;
                            double gapY = 10.0 / 304.8;
                            double slotW = tagW + gapX;
                            double slotH = tagH + gapY;

                            List<SafeRegion> safeRegions = new List<SafeRegion>();
                            // SketchPlane sketchPlane = CreateSketchPlaneForZ(doc, exactCutZ); // 畫線需要

                            foreach (PickedBox box in pickedBoxes)
                            {
                                double pMinX = Math.Min(box.Min.X, box.Max.X);
                                double pMaxX = Math.Max(box.Min.X, box.Max.X);
                                double pMinY = Math.Min(box.Min.Y, box.Max.Y);
                                double pMaxY = Math.Max(box.Min.Y, box.Max.Y);

                                // 【修改2】隱藏畫線
                                // DrawRectangleLines(doc, sketchPlane, pMinX, pMaxX, pMinY, pMaxY, exactCutZ);

                                SafeRegion region = CreateSafeRegion(pMinX, pMaxX, pMinY, pMaxY, exactCutZ, slotW, slotH);
                                if (region != null) safeRegions.Add(region);
                            }

                            grandTotalMovedTags += MoveTagsToSafeRegions(doc, viewPlan, tagFilter, safeRegions);
                        }

                        trans.Commit();
                    }
                }
            }

            TaskDialog.Show("Revit", $"智慧排版處理完畢！\n共將 {grandTotalMovedTags} 個標籤移動至安全區。");
            return Result.Succeeded;
        }

        // =========================================================================
        // 共用方法區：停車場(SafeRegion)建立、畫矩形、標籤移動邏輯
        // =========================================================================

        public class SafeRegion
        {
            public XYZ Center { get; set; }
            public List<XYZ> Slots { get; set; } = new List<XYZ>();
            public int NextSlotIndex { get; set; } = 0;
            public bool IsFull => NextSlotIndex >= Slots.Count;
        }

        private SafeRegion CreateSafeRegion(double pMinX, double pMaxX, double pMinY, double pMaxY, double exactCutZ, double slotW, double slotH)
        {
            SafeRegion region = new SafeRegion();
            region.Center = new XYZ((pMinX + pMaxX) / 2, (pMinY + pMaxY) / 2, exactCutZ);

            int slotCols = (int)((pMaxX - pMinX) / slotW);
            int slotRows = (int)((pMaxY - pMinY) / slotH);

            if (slotCols < 1 || slotRows < 1) return null;

            // 讓標籤在矩形內置中對齊
            double startX = pMinX + ((pMaxX - pMinX) - slotCols * slotW) / 2.0 + slotW / 2.0;
            double startY = pMaxY - ((pMaxY - pMinY) - slotRows * slotH) / 2.0 - slotH / 2.0;

            // 由左至右 (欄 c)，由上而下排下去 (列 r)
            for (int c = 0; c < slotCols; c++)
            {
                for (int r = 0; r < slotRows; r++)
                {
                    double cx = startX + c * slotW;
                    double cy = startY - r * slotH; // 由上往下 Y 遞減 (換行)
                    region.Slots.Add(new XYZ(cx, cy, exactCutZ));
                }
            }
            return region;
        }

        private int MoveTagsToSafeRegions(Document doc, ViewPlan viewPlan, ElementMulticategoryFilter tagFilter, List<SafeRegion> safeRegions)
        {
            int movedCount = 0;
            List<IndependentTag> existingTags = new FilteredElementCollector(doc, viewPlan.Id)
                .WherePasses(tagFilter).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList();
            existingTags = existingTags.Where(x => x.Category.BuiltInCategory != BuiltInCategory.OST_DuctTags).ToList(); // 風管標籤不移動

            foreach (IndependentTag tag in existingTags)
            {
                if (tag.IsOrphaned) continue;

                XYZ originalPos;
                try { originalPos = tag.TagHeadPosition; } catch { continue; }

                // 尋找「最近」且「還未客滿」的空白區
                SafeRegion bestRegion = safeRegions
                    .Where(r => !r.IsFull)
                    .OrderBy(r => r.Center.DistanceTo(originalPos))
                    .FirstOrDefault();

                if (bestRegion != null)
                {
                    XYZ newPos = bestRegion.Slots[bestRegion.NextSlotIndex++];
                    try
                    {
                        tag.HasLeader = true;
                        tag.LeaderEndCondition = LeaderEndCondition.Attached;
                        tag.TagHeadPosition = newPos;
                        movedCount++;
                    }
                    catch { }
                }
            }
            return movedCount;
        }

        private void DrawRectangleLines(Document doc, SketchPlane sketchPlane, double minX, double maxX, double minY, double maxY, double z)
        {
            try
            {
                XYZ p1 = new XYZ(minX, minY, z);
                XYZ p2 = new XYZ(maxX, minY, z);
                XYZ p3 = new XYZ(maxX, maxY, z);
                XYZ p4 = new XYZ(minX, maxY, z);

                doc.Create.NewModelCurve(Line.CreateBound(p1, p2), sketchPlane);
                doc.Create.NewModelCurve(Line.CreateBound(p2, p3), sketchPlane);
                doc.Create.NewModelCurve(Line.CreateBound(p3, p4), sketchPlane);
                doc.Create.NewModelCurve(Line.CreateBound(p4, p1), sketchPlane);
            }
            catch { }
        }

        // =========================================================================
        // 【自動模式】核心生成邏輯 (封裝原本的布林與洪水演算法)
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

        private SketchPlane CreateSketchPlaneForZ(Document doc, double z)
        {
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, z));
            return SketchPlane.Create(doc, plane);
        }
    }
}