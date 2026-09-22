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
        private const string RegionLineStyleName = "CSD_標籤自動空白區";
        private const int MaximumRowsPerRegion = 15;
        private const double Epsilon = 1e-7;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            AutoNumberForm typeForm = new AutoNumberForm(doc);
            typeForm.ShowDialog();
            if (typeForm.trueOrFalse != true) return Result.Cancelled;

            List<ViewPlan> availableViews = AutoPipeTag.GetAutoNumberViewPlans(doc, typeForm.viewFamilyTypeName);
            ChooseMultiViewPlansForm chooseForm = new ChooseMultiViewPlansForm(doc, availableViews,
                ChooseMultiViewPlansForm.FormMode.TagArray);
            if (chooseForm.ShowDialog() != DialogResult.OK) return Result.Cancelled;
            List<ViewPlan> selectedViews = chooseForm.checkViewPlans;
            if (selectedViews == null || selectedViews.Count == 0) return Result.Failed;

            List<IndependentTag> targetTags = selectedViews.SelectMany(view => CollectTargetTags(doc, view))
                .GroupBy(tag => tag.Id.Value).Select(group => group.First()).ToList();
            List<string> familyWarnings = DisableRotateWithComponent(doc, targetTags);
            Dictionary<ElementId, List<PickedBox>> manualBoxes = chooseForm.IsAutoResult
                ? new Dictionary<ElementId, List<PickedBox>>() : PickManualRegions(uiDoc, selectedViews);

            int totalTags = 0;
            int horizontalTags = 0;
            int totalRegions = 0;
            List<string> viewResults = new List<string>();
            using (Transaction transaction = new Transaction(doc, "標籤空白區域檢查"))
            {
                transaction.Start();
                GraphicsStyle lineStyle = GetOrCreateRegionLineStyle(doc);
                foreach (ViewPlan view in selectedViews)
                {
                    ClearOldRegionLines(doc, view);
                    List<IndependentTag> tags = CollectTargetTags(doc, view);
                    totalTags += tags.Count;
                    foreach (IndependentTag tag in tags)
                    {
                        try
                        {
                            tag.TagOrientation = TagOrientation.Horizontal;
                            if (tag.TagOrientation == TagOrientation.AnyModelDirection) tag.RotationAngle = 0;
                            horizontalTags++;
                        }
                        catch { }
                    }
                    doc.Regenerate();

                    ViewFrame frame = new ViewFrame(view);
                    List<TagSize> sizes = tags.Select(tag => MeasureTag(tag, view, frame))
                        .Where(size => size != null).ToList();
                    if (sizes.Count == 0)
                    {
                        viewResults.Add($"{view.Name}：沒有可量測的無引線管道／電纜架標籤");
                        continue;
                    }
                    double cellWidth = sizes.Max(size => size.Width);
                    double cellHeight = sizes.Max(size => size.Height);
                    List<Rect2D> regions;
                    if (chooseForm.IsAutoResult)
                    {
                        Rect2D cropBounds = frame.Project(view.CropBox);
                        List<List<UV2>> cropLoops = GetCropLoops(view, frame);
                        ObstacleSet obstacles = CollectVisibleObstacles(doc, view, frame);
                        regions = FindAutomaticRegions(cropBounds, cropLoops, obstacles, cellWidth, cellHeight);
                    }
                    else
                    {
                        manualBoxes.TryGetValue(view.Id, out List<PickedBox> boxes);
                        regions = CreateManualRegions(frame, boxes, cellWidth, cellHeight);
                    }
                    DrawRegions(doc, view, frame, regions, lineStyle);
                    totalRegions += regions.Count;
                    viewResults.Add($"{view.Name}：標籤 {tags.Count} 個，基準 " +
                        $"{cellWidth * 304.8 / view.Scale:F2} × {cellHeight * 304.8 / view.Scale:F2} mm（紙面），空白區 {regions.Count} 個");
                }
                transaction.Commit();
            }

            string warnings = familyWarnings.Count == 0 ? "" :
                "\n\n無法關閉「隨元件旋轉」：\n" + string.Join("\n", familyWarnings.Distinct());
            TaskDialog.Show("標籤空白區域檢查", $"處理完成（本階段沒有移動標籤）。\n" +
                $"無引線標籤：{totalTags} 個\n已設定水平：{horizontalTags} 個\n空白區：{totalRegions} 個\n\n" +
                string.Join("\n", viewResults) + warnings);
            return Result.Succeeded;
        }

        private static List<IndependentTag> CollectTargetTags(Document doc, ViewPlan view)
        {
            return new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>().Where(tag => tag.Category != null && !tag.HasLeader && !tag.IsOrphaned &&
                    (tag.Category.BuiltInCategory == BuiltInCategory.OST_PipeTags ||
                     tag.Category.BuiltInCategory == BuiltInCategory.OST_CableTrayTags)).ToList();
        }

        private static List<string> DisableRotateWithComponent(Document projectDoc, IEnumerable<IndependentTag> tags)
        {
            List<string> warnings = new List<string>();
            List<Family> families = tags.Select(tag => projectDoc.GetElement(tag.GetTypeId()) as FamilySymbol)
                .Where(symbol => symbol != null).Select(symbol => symbol.Family)
                .GroupBy(family => family.Id.Value).Select(group => group.First()).ToList();
            foreach (Family family in families)
            {
                Document familyDoc = null;
                try
                {
                    if (!family.IsEditable) { warnings.Add($"{family.Name}（族不可編輯）"); continue; }
                    familyDoc = projectDoc.EditFamily(family);
                    Parameter parameter = familyDoc.OwnerFamily
                        .get_Parameter(BuiltInParameter.FAMILY_ROTATE_WITH_COMPONENT);
                    if (parameter == null) { warnings.Add($"{family.Name}（找不到族參數）"); continue; }
                    using (Transaction transaction = new Transaction(familyDoc, "關閉隨元件旋轉"))
                    {
                        transaction.Start();
                        if (parameter.IsReadOnly || !parameter.Set(0))
                            throw new InvalidOperationException("族參數無法寫入");
                        transaction.Commit();
                    }
                    familyDoc.LoadFamily(projectDoc, new OverwriteFamilyLoadOptions());
                }
                catch (Exception ex) { warnings.Add($"{family.Name}（{ex.Message}）"); }
                finally
                {
                    if (familyDoc != null && familyDoc.IsValidObject)
                        try { familyDoc.Close(false); } catch { }
                }
            }
            return warnings;
        }

        private static Dictionary<ElementId, List<PickedBox>> PickManualRegions(UIDocument uiDoc,
            IEnumerable<ViewPlan> views)
        {
            Dictionary<ElementId, List<PickedBox>> result = new Dictionary<ElementId, List<PickedBox>>();
            foreach (ViewPlan view in views)
            {
                if (uiDoc.ActiveView.Id != view.Id) uiDoc.ActiveView = view;
                List<PickedBox> boxes = new List<PickedBox>();
                TaskDialog.Show("手動空白區域", $"目前視圖：【{view.Name}】\n連續框選可放置標籤的區域，完成後按 ESC。");
                while (true)
                {
                    try { boxes.Add(uiDoc.Selection.PickBox(PickBoxStyle.Directional, "框選空白區域；完成請按 ESC")); }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { break; }
                }
                result[view.Id] = boxes;
            }
            return result;
        }

        private static TagSize MeasureTag(IndependentTag tag, Autodesk.Revit.DB.View view, ViewFrame frame)
        {
            try
            {
                BoundingBoxXYZ box = tag.get_BoundingBox(view);
                if (box == null) return null;
                Rect2D projected = frame.Project(box);
                return projected.Width > Epsilon && projected.Height > Epsilon
                    ? new TagSize(projected.Width, projected.Height) : null;
            }
            catch { return null; }
        }

        private static ObstacleSet CollectVisibleObstacles(Document doc, ViewPlan view, ViewFrame frame)
        {
            ObstacleSet result = new ObstacleSet();
            foreach (Element element in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                if (element is RevitLinkInstance || IsRegionLine(element)) continue;
                AddElementObstacle(result, element, view, frame, null);
            }
            foreach (RevitLinkInstance link in new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                Document linkDoc = link.GetLinkDocument();
                if (linkDoc == null) continue;
                Transform transform = link.GetTotalTransform();
                Autodesk.Revit.DB.View linkedView = null;
                try
                {
                    RevitLinkGraphicsSettings settings = view.GetLinkOverrides(link.Id)
                        ?? view.GetLinkOverrides(link.GetTypeId());
                    if (settings != null && settings.LinkedViewId != ElementId.InvalidElementId)
                        linkedView = linkDoc.GetElement(settings.LinkedViewId) as Autodesk.Revit.DB.View;
                }
                catch { }
                try
                {
                    foreach (Element element in new FilteredElementCollector(doc, view.Id, link.Id)
                        .WhereElementIsNotElementType())
                        AddElementObstacle(result, element, linkedView, frame, transform);
                }
                catch
                {
                    foreach (Element element in new FilteredElementCollector(linkDoc).WhereElementIsNotElementType())
                        AddElementObstacle(result, element, null, frame, transform);
                }
            }
            return result;
        }

        private static void AddElementObstacle(ObstacleSet result, Element element, Autodesk.Revit.DB.View view,
            ViewFrame frame, Transform transform)
        {
            try
            {
                // 容器的 BoundingBox 會包住所有成員，不能當成實心障礙物。
                if (element is Group || element is AssemblyInstance) return;
                if (element is CurveElement curveElement)
                {
                    AddCurveObstacle(result, curveElement.GeometryCurve, frame, transform);
                    return;
                }
                if (element is ImportInstance)
                {
                    Options options = new Options { IncludeNonVisibleObjects = false };
                    if (view != null && view.Document.Equals(element.Document)) options.View = view;
                    AddGeometryObstacles(result, element.get_Geometry(options), frame, transform);
                    return;
                }

                bool isAnnotation = element.Category != null &&
                    element.Category.CategoryType == CategoryType.Annotation;
                if (isAnnotation || element is IndependentTag || element is TextNote ||
                    element is Dimension || element is FilledRegion || element is ImageInstance)
                {
                    BoundingBoxXYZ box = element.get_BoundingBox(view);
                    if (box == null) return;
                    Rect2D rect = frame.Project(box, transform);
                    if (rect.Width > Epsilon || rect.Height > Epsilon) result.Rectangles.Add(rect);
                    return;
                }

                // 模型元件只使用目前視圖實際畫出的幾何邊線。若以整個元件 BoundingBox
                // 判斷，樓板、地形或大型族的外框會把其內部及周圍空白全部誤判為障礙。
                Options geometryOptions = new Options
                {
                    IncludeNonVisibleObjects = false,
                    ComputeReferences = false
                };
                if (view != null && view.Document.Equals(element.Document)) geometryOptions.View = view;
                else geometryOptions.DetailLevel = ViewDetailLevel.Fine;
                AddGeometryObstacles(result, element.get_Geometry(geometryOptions), frame, transform);
            }
            catch { }
        }

        private static void AddGeometryObstacles(ObstacleSet result, GeometryElement geometry,
            ViewFrame frame, Transform outerTransform)
        {
            if (geometry == null) return;
            foreach (GeometryObject item in geometry)
            {
                if (item is Curve curve) AddCurveObstacle(result, curve, frame, outerTransform);
                else if (item is PolyLine polyLine)
                {
                    IList<XYZ> points = polyLine.GetCoordinates();
                    for (int i = 1; i < points.Count; i++)
                        AddSegmentObstacle(result, points[i - 1], points[i], frame, outerTransform);
                }
                else if (item is GeometryInstance instance)
                    AddGeometryObstacles(result, instance.GetInstanceGeometry(), frame, outerTransform);
                else if (item is Solid solid && solid.Edges.Size > 0)
                    foreach (Edge edge in solid.Edges)
                        AddCurveObstacle(result, edge.AsCurve(), frame, outerTransform);
                else if (item is Mesh mesh)
                {
                    for (int i = 0; i < mesh.NumTriangles; i++)
                    {
                        MeshTriangle triangle = mesh.get_Triangle(i);
                        XYZ a = triangle.get_Vertex(0); XYZ b = triangle.get_Vertex(1); XYZ c = triangle.get_Vertex(2);
                        AddSegmentObstacle(result, a, b, frame, outerTransform);
                        AddSegmentObstacle(result, b, c, frame, outerTransform);
                        AddSegmentObstacle(result, c, a, frame, outerTransform);
                    }
                }
            }
        }

        private static void AddCurveObstacle(ObstacleSet result, Curve curve, ViewFrame frame, Transform transform)
        {
            if (curve == null) return;
            IList<XYZ> points = curve.Tessellate();
            for (int i = 1; i < points.Count; i++)
                AddSegmentObstacle(result, points[i - 1], points[i], frame, transform);
        }

        private static void AddSegmentObstacle(ObstacleSet result, XYZ a, XYZ b,
            ViewFrame frame, Transform transform)
        {
            if (transform != null) { a = transform.OfPoint(a); b = transform.OfPoint(b); }
            UV2 start = frame.Project(a); UV2 end = frame.Project(b);
            if (Distance(start, end) > Epsilon) result.Segments.Add(new Segment2D(start, end));
        }

        private static bool IsRegionLine(Element element)
        {
            CurveElement curve = element as CurveElement;
            return curve?.LineStyle != null && curve.LineStyle.Name == RegionLineStyleName;
        }

        private static List<Rect2D> FindAutomaticRegions(Rect2D crop, List<List<UV2>> cropLoops,
            ObstacleSet obstacles, double cellWidth, double cellHeight)
        {
            List<Rect2D> candidates = new List<Rect2D>();
            if (cellWidth <= Epsilon || cellHeight <= Epsilon) return candidates;

            // 固定單一格網起點會漏掉未與裁切框對齊的空白帶。
            // 以四分之一格為位移掃描 16 組格網，再挑選互不重疊的最大候選區。
            const int phaseCount = 4;
            for (int horizontalPhase = 0; horizontalPhase < phaseCount; horizontalPhase++)
            {
                double startU = crop.MinU + horizontalPhase * cellWidth / phaseCount;
                int columns = (int)Math.Floor((crop.MaxU - startU) / cellWidth);
                if (columns < 1) continue;
                for (int verticalPhase = 0; verticalPhase < phaseCount; verticalPhase++)
                {
                    double startV = crop.MinV + verticalPhase * cellHeight / phaseCount;
                    int rows = (int)Math.Floor((crop.MaxV - startV) / cellHeight);
                    if (rows < 1) continue;
                    for (int column = 0; column < columns; column++)
                    {
                        bool[] free = new bool[rows];
                        double minU = startU + column * cellWidth;
                        for (int row = 0; row < rows; row++)
                        {
                            Rect2D cell = new Rect2D(minU, minU + cellWidth,
                                startV + row * cellHeight, startV + (row + 1) * cellHeight);
                            free[row] = IsInsideCrop(cell, cropLoops) && !obstacles.IsBlocked(cell);
                        }
                        int rowIndex = 0;
                        while (rowIndex < rows)
                        {
                            while (rowIndex < rows && !free[rowIndex]) rowIndex++;
                            int runStart = rowIndex;
                            while (rowIndex < rows && free[rowIndex]) rowIndex++;
                            int remaining = rowIndex - runStart;
                            int partStart = runStart;
                            while (remaining > 0)
                            {
                                int count = Math.Min(MaximumRowsPerRegion, remaining);
                                candidates.Add(new Rect2D(minU, minU + cellWidth,
                                    startV + partStart * cellHeight,
                                    startV + (partStart + count) * cellHeight));
                                partStart += count;
                                remaining -= count;
                            }
                        }
                    }
                }
            }

            List<Rect2D> result = new List<Rect2D>();
            foreach (Rect2D candidate in candidates
                .OrderByDescending(region => region.Height)
                .ThenBy(region => region.MinU).ThenBy(region => region.MinV))
            {
                if (!result.Any(existing => existing.IntersectsInterior(candidate))) result.Add(candidate);
            }
            return result.OrderBy(region => region.MinU).ThenBy(region => region.MinV).ToList();
        }

        private static List<Rect2D> CreateManualRegions(ViewFrame frame, List<PickedBox> boxes,
            double cellWidth, double cellHeight)
        {
            List<Rect2D> result = new List<Rect2D>();
            if (boxes == null) return result;
            foreach (PickedBox box in boxes)
            {
                UV2 a = frame.Project(box.Min); UV2 b = frame.Project(box.Max);
                double minU = Math.Min(a.U, b.U); double minV = Math.Min(a.V, b.V);
                int columns = (int)Math.Floor(Math.Abs(a.U - b.U) / cellWidth);
                int rows = (int)Math.Floor(Math.Abs(a.V - b.V) / cellHeight);
                for (int column = 0; column < columns; column++)
                    for (int row = 0; row < rows; row += MaximumRowsPerRegion)
                    {
                        int count = Math.Min(MaximumRowsPerRegion, rows - row);
                        result.Add(new Rect2D(minU + column * cellWidth, minU + (column + 1) * cellWidth,
                            minV + row * cellHeight, minV + (row + count) * cellHeight));
                    }
            }
            return result;
        }

        private static List<List<UV2>> GetCropLoops(ViewPlan view, ViewFrame frame)
        {
            List<List<UV2>> result = new List<List<UV2>>();
            try
            {
                foreach (CurveLoop loop in view.GetCropRegionShapeManager().GetCropShape())
                {
                    List<UV2> points = new List<UV2>();
                    foreach (Curve curve in loop)
                        foreach (XYZ point in curve.Tessellate())
                        {
                            UV2 projected = frame.Project(point);
                            if (points.Count == 0 || Distance(points.Last(), projected) > Epsilon) points.Add(projected);
                        }
                    if (points.Count >= 3) result.Add(points);
                }
            }
            catch { }
            return result;
        }

        private static bool IsInsideCrop(Rect2D cell, List<List<UV2>> loops)
        {
            if (loops.Count == 0) return true;
            return new[] { new UV2(cell.MinU, cell.MinV), new UV2(cell.MaxU, cell.MinV),
                new UV2(cell.MaxU, cell.MaxV), new UV2(cell.MinU, cell.MaxV) }
                .All(point => IsPointInside(point, loops));
        }

        private static bool IsPointInside(UV2 point, List<List<UV2>> loops)
        {
            bool inside = false;
            foreach (List<UV2> polygon in loops)
            {
                bool inThisLoop = false;
                for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                {
                    UV2 a = polygon[i]; UV2 b = polygon[j];
                    if (((a.V > point.V) != (b.V > point.V)) &&
                        point.U < (b.U - a.U) * (point.V - a.V) / (b.V - a.V) + a.U)
                        inThisLoop = !inThisLoop;
                }
                if (inThisLoop) inside = !inside;
            }
            return inside;
        }

        private static void ClearOldRegionLines(Document doc, ViewPlan view)
        {
            List<ElementId> ids = new FilteredElementCollector(doc, view.Id).OfClass(typeof(CurveElement))
                .Cast<CurveElement>().Where(IsRegionLine).Where(curve => curve.OwnerViewId == view.Id)
                .Select(curve => curve.Id).ToList();
            if (ids.Count > 0) doc.Delete(ids);
        }

        private static GraphicsStyle GetOrCreateRegionLineStyle(Document doc)
        {
            Category lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            Category style = lines.SubCategories.Cast<Category>()
                .FirstOrDefault(category => category.Name == RegionLineStyleName);
            if (style == null) style = doc.Settings.Categories.NewSubcategory(lines, RegionLineStyleName);
            style.LineColor = new Autodesk.Revit.DB.Color(0, 255, 255);
            try { style.SetLineWeight(3, GraphicsStyleType.Projection); } catch { }
            return style.GetGraphicsStyle(GraphicsStyleType.Projection);
        }

        private static void DrawRegions(Document doc, ViewPlan view, ViewFrame frame,
            IEnumerable<Rect2D> regions, GraphicsStyle style)
        {
            foreach (Rect2D box in regions)
            {
                XYZ a = frame.PointAt(view.Origin, box.MinU, box.MinV);
                XYZ b = frame.PointAt(view.Origin, box.MaxU, box.MinV);
                XYZ c = frame.PointAt(view.Origin, box.MaxU, box.MaxV);
                XYZ d = frame.PointAt(view.Origin, box.MinU, box.MaxV);
                foreach (Line line in new[] { Line.CreateBound(a, b), Line.CreateBound(b, c),
                    Line.CreateBound(c, d), Line.CreateBound(d, a) })
                {
                    DetailCurve curve = doc.Create.NewDetailCurve(view, line);
                    curve.LineStyle = style;
                }
            }
        }

        private static double Distance(UV2 a, UV2 b)
            => Math.Sqrt((a.U - b.U) * (a.U - b.U) + (a.V - b.V) * (a.V - b.V));

        private class OverwriteFamilyLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            { overwriteParameterValues = true; return true; }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            { source = FamilySource.Family; overwriteParameterValues = true; return true; }
        }

        private class TagSize
        {
            public TagSize(double width, double height) { Width = width; Height = height; }
            public double Width { get; }
            public double Height { get; }
        }

        private class UV2
        {
            public UV2(double u, double v) { U = u; V = v; }
            public double U { get; }
            public double V { get; }
        }

        private class Rect2D
        {
            public Rect2D(double minU, double maxU, double minV, double maxV)
            { MinU = minU; MaxU = maxU; MinV = minV; MaxV = maxV; }
            public double MinU { get; }
            public double MaxU { get; }
            public double MinV { get; }
            public double MaxV { get; }
            public double Width => MaxU - MinU;
            public double Height => MaxV - MinV;
            public bool Contains(UV2 point) => point.U >= MinU - Epsilon && point.U <= MaxU + Epsilon &&
                point.V >= MinV - Epsilon && point.V <= MaxV + Epsilon;
            public bool IntersectsInterior(Rect2D other) => MaxU > other.MinU + Epsilon &&
                MinU < other.MaxU - Epsilon && MaxV > other.MinV + Epsilon && MinV < other.MaxV - Epsilon;
        }

        private class Segment2D
        {
            public Segment2D(UV2 start, UV2 end) { Start = start; End = end; }
            public UV2 Start { get; }
            public UV2 End { get; }
            public bool Intersects(Rect2D box)
            {
                Rect2D inner = new Rect2D(box.MinU + Epsilon, box.MaxU - Epsilon,
                    box.MinV + Epsilon, box.MaxV - Epsilon);
                if (inner.Width <= Epsilon || inner.Height <= Epsilon) return false;
                if ((Start.U < inner.MinU && End.U < inner.MinU) || (Start.U > inner.MaxU && End.U > inner.MaxU) ||
                    (Start.V < inner.MinV && End.V < inner.MinV) || (Start.V > inner.MaxV && End.V > inner.MaxV)) return false;
                if (inner.Contains(Start) || inner.Contains(End)) return true;
                UV2 a = new UV2(inner.MinU, inner.MinV); UV2 b = new UV2(inner.MaxU, inner.MinV);
                UV2 c = new UV2(inner.MaxU, inner.MaxV); UV2 d = new UV2(inner.MinU, inner.MaxV);
                return Crosses(Start, End, a, b) || Crosses(Start, End, b, c) ||
                       Crosses(Start, End, c, d) || Crosses(Start, End, d, a);
            }
            private static bool Crosses(UV2 a, UV2 b, UV2 c, UV2 d)
            {
                double o1 = Orientation(a, b, c); double o2 = Orientation(a, b, d);
                double o3 = Orientation(c, d, a); double o4 = Orientation(c, d, b);
                return o1 * o2 <= Epsilon && o3 * o4 <= Epsilon;
            }
            private static double Orientation(UV2 a, UV2 b, UV2 c)
                => (b.U - a.U) * (c.V - a.V) - (b.V - a.V) * (c.U - a.U);
        }

        private class ObstacleSet
        {
            public List<Rect2D> Rectangles { get; } = new List<Rect2D>();
            public List<Segment2D> Segments { get; } = new List<Segment2D>();
            public bool IsBlocked(Rect2D cell) => Rectangles.Any(rect => rect.IntersectsInterior(cell)) ||
                Segments.Any(segment => segment.Intersects(cell));
        }

        private class ViewFrame
        {
            private readonly XYZ _right;
            private readonly XYZ _up;
            public ViewFrame(Autodesk.Revit.DB.View view) { _right = view.RightDirection.Normalize(); _up = view.UpDirection.Normalize(); }
            public UV2 Project(XYZ point) => new UV2(point.DotProduct(_right), point.DotProduct(_up));
            public Rect2D Project(BoundingBoxXYZ box, Transform outerTransform = null)
            {
                List<UV2> points = new List<UV2>();
                foreach (double x in new[] { box.Min.X, box.Max.X })
                    foreach (double y in new[] { box.Min.Y, box.Max.Y })
                        foreach (double z in new[] { box.Min.Z, box.Max.Z })
                        {
                            XYZ point = box.Transform.OfPoint(new XYZ(x, y, z));
                            if (outerTransform != null) point = outerTransform.OfPoint(point);
                            points.Add(Project(point));
                        }
                return new Rect2D(points.Min(p => p.U), points.Max(p => p.U),
                    points.Min(p => p.V), points.Max(p => p.V));
            }
            public XYZ PointAt(XYZ planePoint, double u, double v)
            {
                UV2 current = Project(planePoint);
                return planePoint + _right * (u - current.U) + _up * (v - current.V);
            }
        }
    }
}
