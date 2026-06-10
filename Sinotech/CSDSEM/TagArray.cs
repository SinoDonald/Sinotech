using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
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
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_PipeTags,
                BuiltInCategory.OST_DuctTags,
                BuiltInCategory.OST_CableTrayTags,
                BuiltInCategory.OST_MultiCategoryTags
            };
            ElementMulticategoryFilter multiCatFilter = new ElementMulticategoryFilter(targetCategories);

            List<BuiltInCategory> tagCategories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_PipeTags,
                BuiltInCategory.OST_DuctTags,
                BuiltInCategory.OST_CableTrayTags
            };
            ElementMulticategoryFilter tagFilter = new ElementMulticategoryFilter(tagCategories);

            using (Transaction trans = new Transaction(doc, "標籤順序"))
            {
                trans.Start();

                if (isAutoMode)
                {
                    foreach (ViewPlan viewPlan in selectedViews)
                    {
                        double exactZMax = GetPlaneElevation(viewPlan, PlanViewPlane.TopClipPlane, 1000.0, -1000.0);
                        double exactZMin = GetPlaneElevation(viewPlan, PlanViewPlane.ViewDepthPlane, 1000.0, -1000.0);
                        double exactCutZ = viewPlan.GenLevel != null ? viewPlan.GenLevel.Elevation : exactZMin;
                        double validZ_Min = exactZMin - 0.5;
                        double validZ_Max = exactZMax + 0.5;

                        // =========================================================
                        // 1. 獲取單一標籤的物理尺寸 (網格的基礎單位)
                        // =========================================================
                        double tagW = 1000.0 / 304.8; // 預設寬度
                        double tagH = 300.0 / 304.8;  // 預設高度
                        int minTagsFit = 5;           // 【參數】：此空白區至少要能塞得下幾個標籤才畫框！

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

                        // 視圖邊界計算
                        double viewMinX, viewMinY, viewMaxX, viewMaxY;
                        BoundingBoxXYZ cb = viewPlan.CropBox;
                        if (viewPlan.CropBoxActive)
                        {
                            Transform ct = cb.Transform;
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
                            viewMinX = -2000.0; viewMinY = -2000.0;
                            viewMaxX = 2000.0; viewMaxY = 2000.0;
                        }

                        // =========================================================
                        // 2. 布林運算：建立基底大面，並扣除所有建築管線實體
                        // =========================================================
                        List<CurveLoop> baseLoops = new List<CurveLoop>();
                        CurveLoop viewExtentLoop = new CurveLoop();
                        viewExtentLoop.Append(Line.CreateBound(new XYZ(viewMinX, viewMinY, exactCutZ), new XYZ(viewMaxX, viewMinY, exactCutZ)));
                        viewExtentLoop.Append(Line.CreateBound(new XYZ(viewMaxX, viewMinY, exactCutZ), new XYZ(viewMaxX, viewMaxY, exactCutZ)));
                        viewExtentLoop.Append(Line.CreateBound(new XYZ(viewMaxX, viewMaxY, exactCutZ), new XYZ(viewMinX, viewMaxY, exactCutZ)));
                        viewExtentLoop.Append(Line.CreateBound(new XYZ(viewMinX, viewMaxY, exactCutZ), new XYZ(viewMinX, viewMinY, exactCutZ)));
                        baseLoops.Add(viewExtentLoop);

                        Solid emptyAreaSolid = GeometryCreationUtilities.CreateExtrusionGeometry(baseLoops, XYZ.BasisZ, 0.1);

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
                                validElements = new FilteredElementCollector(projItem.Doc).WherePasses(multiCatFilter)
                                    .WherePasses(new BoundingBoxIntersectsFilter(linkOutline)).WhereElementIsNotElementType().ToList();
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

                        // =========================================================
                        // 3. 網格化與洪水演算法 (過濾室內空間)
                        // =========================================================
                        if (emptyAreaSolid == null) continue;

                        PlanarFace topFace = null;
                        foreach (Face face in emptyAreaSolid.Faces)
                        {
                            if (face is PlanarFace pf && pf.FaceNormal.IsAlmostEqualTo(XYZ.BasisZ, 0.01))
                            {
                                topFace = pf; break; // 取得扣除完畢的頂面
                            }
                        }
                        if (topFace == null) continue;

                        int cols = (int)Math.Ceiling((viewMaxX - viewMinX) / tagW);
                        int rows = (int)Math.Ceiling((viewMaxY - viewMinY) / tagH);
                        bool[,] isFree = new bool[cols, rows];

                        // 判斷每一格「磁磚」是否落在安全面內
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

                        // 洪水演算法 (BFS)：只保留與視圖邊界相連的空間 (剔除室內房間)
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

                        // =========================================================
                        // 4. 最大矩形演算法 (Maximal Rectangle) - 只抓外圍邊界
                        // =========================================================
                        List<int[]> finalRectangles = new List<int[]>(); // [minC, maxC, minR, maxR]

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

                                        int area = minH * (c2 - c + 1); // area 就代表這個矩形能放幾個標籤！
                                        if (area > maxArea)
                                        {
                                            maxArea = area;
                                            bestMinC = c; bestMaxC = c2;
                                            bestMinR = r - minH + 1; bestMaxR = r;
                                        }
                                    }
                                }
                            }

                            // 如果找出來的最大矩形，連使用者指定的標籤數量都塞不下，就停止尋找
                            if (maxArea < minTagsFit) break;

                            finalRectangles.Add(new int[] { bestMinC, bestMaxC, bestMinR, bestMaxR });

                            // 將找到的大矩形區域從網格中抹除，避免重複計算
                            for (int c = bestMinC; c <= bestMaxC; c++)
                                for (int r = bestMinR; r <= bestMaxR; r++)
                                    isExterior[c, r] = false;
                        }

                        // =========================================================
                        // 5. 將找出的矩形頂點轉回 3D 座標，並只畫出外圍四邊
                        // =========================================================
                        SketchPlane sketchPlane = CreateSketchPlaneForZ(doc, exactCutZ);

                        foreach (var rect in finalRectangles)
                        {
                            double pMinX = viewMinX + rect[0] * tagW;
                            double pMinY = viewMinY + rect[2] * tagH;
                            // +1 是因為 max index 包含該格本身的寬度
                            double pMaxX = viewMinX + (rect[1] + 1) * tagW;
                            double pMaxY = viewMinY + (rect[3] + 1) * tagH;

                            XYZ p1 = new XYZ(pMinX, pMinY, exactCutZ);
                            XYZ p2 = new XYZ(pMaxX, pMinY, exactCutZ);
                            XYZ p3 = new XYZ(pMaxX, pMaxY, exactCutZ);
                            XYZ p4 = new XYZ(pMinX, pMaxY, exactCutZ);

                            try
                            {
                                doc.Create.NewModelCurve(Line.CreateBound(p1, p2), sketchPlane);
                                doc.Create.NewModelCurve(Line.CreateBound(p2, p3), sketchPlane);
                                doc.Create.NewModelCurve(Line.CreateBound(p3, p4), sketchPlane);
                                doc.Create.NewModelCurve(Line.CreateBound(p4, p1), sketchPlane);
                            }
                            catch { }
                        }

                        //TaskDialog.Show("Revit", $"已過濾建築室內空間！\n並成功繪製出所有可容納 {minTagsFit} 個以上標籤的矩形安全區！");
                    }
                }
                else
                {
                    TaskDialog.Show("Revit", "開發中...");
                }

                trans.Commit();
            }

            return Result.Succeeded;
        }

        // =========================================================================
        // 幾何核心輔助方法（不變）
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