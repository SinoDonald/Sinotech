using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
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

            // 【正確讀取單選按鈕回傳結果】
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
                //BuiltInCategory.OST_Floors,
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

            using (Transaction trans = new Transaction(doc, "標籤順序"))
            {
                trans.Start();

                // 這裡可以依據自動或手動做後續邏輯切換
                if (isAutoMode)
                {
                    foreach (ViewPlan viewPlan in selectedViews)
                    {
                        // 計算該平面圖嚴格的 Top/Bottom 與最關鍵的剖切面 CutPlane 高程
                        double exactZMax = GetPlaneElevation(viewPlan, PlanViewPlane.TopClipPlane, 1000.0, -1000.0);
                        double exactZMin = GetPlaneElevation(viewPlan, PlanViewPlane.ViewDepthPlane, 1000.0, -1000.0);
                        double defaultCutZ = (exactZMax + exactZMin) / 2.0;

                        // 【關鍵修正 1】：取得平面圖當前的剖切面高度，模型線畫在這裡絕對看得到！
                        double exactCutZ = GetPlaneElevation(viewPlan, PlanViewPlane.CutPlane, defaultCutZ, defaultCutZ);

                        double validZ_Min = exactZMin - 0.5;
                        double validZ_Max = exactZMax + 0.5;

                        double viewMinX, viewMinY, viewMaxX, viewMaxY;
                        BoundingBoxXYZ cb = viewPlan.CropBox;
                        Transform ct = cb.Transform;
                        if (viewPlan.CropBoxActive)
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
                            viewMinX = -5000.0; viewMinY = -5000.0;
                            viewMaxX = 5000.0; viewMaxY = 5000.0;
                        }

                        // 建立大底板幾何固體面時，高度改用 exactCutZ 剖切面高程
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
                                FilteredElementCollector mainCollector = new FilteredElementCollector(doc, viewPlan.Id)
                                    .WherePasses(multiCatFilter)
                                    .WhereElementIsNotElementType();
                                validElements = mainCollector.ToList();
                            }
                            else
                            {
                                Transform invTransform = projItem.LinkInstance.GetTotalTransform().Inverse;
                                Outline linkOutline = GetTransformedOutline(viewPlan, cb, invTransform, validZ_Min, validZ_Max);
                                BoundingBoxIntersectsFilter bboxFilter = new BoundingBoxIntersectsFilter(linkOutline);

                                FilteredElementCollector linkedCollector = new FilteredElementCollector(projItem.Doc)
                                    .WherePasses(multiCatFilter)
                                    .WherePasses(bboxFilter)
                                    .WhereElementIsNotElementType();

                                validElements = linkedCollector.ToList();
                            }

                            foreach (Element elem in validElements)
                            {
                                Transform linkXform = projItem.IsMainModel ? null : projItem.LinkInstance.GetTotalTransform();

                                if (elem.Location is LocationCurve locCurve && locCurve.Curve != null)
                                {
                                    double visLen = GetVisibleLengthInView(elem, linkXform, viewMinX, viewMaxX, viewMinY, viewMaxY);
                                    if (visLen <= 0) continue;
                                }

                                // 採用先前修正：唯獨傳入 View，不手動指定 DetailLevel 避免衝突
                                Options opt = new Options { View = viewPlan };
                                GeometryElement geomElem = elem.get_Geometry(opt);
                                if (geomElem == null) continue;

                                foreach (GeometryObject geomObj in geomElem)
                                {
                                    if (geomObj is GeometryInstance geomInst)
                                    {
                                        GeometryElement instGeom = geomInst.GetInstanceGeometry();
                                        foreach (GeometryObject instObj in instGeom)
                                        {
                                            if (instObj is Solid s && s.Volume > 0)
                                            {
                                                Solid transformedSolid = linkXform != null ? SolidUtils.CreateTransformed(s, linkXform) : s;
                                                emptyAreaSolid = SubtractSolid2D(emptyAreaSolid, transformedSolid);
                                            }
                                        }
                                    }
                                    else if (geomObj is Solid solid && solid.Volume > 0)
                                    {
                                        Solid transformedSolid = linkXform != null ? SolidUtils.CreateTransformed(solid, linkXform) : solid;
                                        emptyAreaSolid = SubtractSolid2D(emptyAreaSolid, transformedSolid);
                                    }
                                }
                            }
                        }

                        // 3. 劃設殘餘空白區的模型線
                        if (emptyAreaSolid != null)
                        {
                            // 【關鍵修正 2】：草圖平面同樣精準建立在剖切面高度上
                            SketchPlane sketchPlane = CreateSketchPlaneForZ(doc, exactCutZ);

                            foreach (Edge edge in emptyAreaSolid.Edges)
                            {
                                Curve curve = edge.AsCurve();
                                XYZ startPt = curve.GetEndPoint(0);
                                XYZ endPt = curve.GetEndPoint(1);

                                // 【關鍵修正 3】：將邊界線的起終點精準投影至視圖剖切面高度 exactCutZ
                                XYZ projectedStart = new XYZ(startPt.X, startPt.Y, exactCutZ);
                                XYZ projectedEnd = new XYZ(endPt.X, endPt.Y, exactCutZ);

                                if (projectedStart.DistanceTo(projectedEnd) > 0.001)
                                {
                                    try
                                    {
                                        Line modelLine = Line.CreateBound(projectedStart, projectedEnd);
                                        doc.Create.NewModelCurve(modelLine, sketchPlane);
                                    }
                                    catch { }
                                }
                            }
                        }
                    }

                    TaskDialog.Show("Revit", "分割空白區完成！");
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