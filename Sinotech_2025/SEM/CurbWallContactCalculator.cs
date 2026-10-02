using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sinotech_2025.SEM
{
    // 幾何運算維持 Revit 內部單位，最後才換成 PCCES 所用的 mm。
    internal sealed class CurbWallContactCalculator
    {
        private readonly Document document;
        private readonly Dictionary<string, List<WallFace>> wallCache = new Dictionary<string, List<WallFace>>();
        private readonly Dictionary<string, List<WallVolume>> wallVolumeCache = new Dictionary<string, List<WallVolume>>();
        private static readonly double ContactTolerance = UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Millimeters);
        private const double Epsilon = 1e-8;

        private sealed class WallFace
        {
            public XYZ Origin;
            public XYZ Normal;
            public List<XYZ[]> Triangles;
        }

        private sealed class WallVolume
        {
            public Solid Solid;
            public Transform Transform;
            public string Description;
        }

        public CurbWallContactCalculator(Document document) { this.document = document; }

        public double CalculateMillimeters(Element element)
        {
            return CalculateMillimeters(element, "長度", "寬度", 100);
        }

        public double CalculateFloorOpeningMillimeters(Element element)
        {
            // 此樓版開口族群的中心(左/右)、中心(前/後)均定義原點。
            // 本地 X = 矩形開口寬度，Y = 矩形開口高度，Z = 0 為族群參考樓層。
            // Transform 已包含放置標高及偏移，不再重複加 LocationPoint 或 Level.Elevation。
            var instance = element as FamilyInstance;
            if (instance == null || !(instance.Location is LocationPoint))
                throw new InvalidOperationException("樓板開口必須是以點定位的矩形族群。");
            Transform transform = instance.GetTransform();
            if (Math.Abs(transform.BasisX.Z) > Epsilon || Math.Abs(transform.BasisY.Z) > Epsilon)
                throw new InvalidOperationException("樓板開口參考平面並非水平，請確認族群方向，避免計算錯誤的投影周長。");
            double width = ReadOpeningDimension(element, "矩形開口寬度");
            double height = ReadOpeningDimension(element, "矩形開口高度");
            var points = new[] { new XYZ(-width / 2, -height / 2, 0), new XYZ(width / 2, -height / 2, 0),
                new XYZ(width / 2, height / 2, 0), new XYZ(-width / 2, height / 2, 0) }
                .Select(transform.OfPoint).ToArray();
            var bounds = new BoundingBoxXYZ
            {
                Min = new XYZ(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z)),
                Max = new XYZ(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z))
            };
            var walls = new List<WallVolume>();
            CollectNearbyWalls(document, Transform.Identity, bounds, null, "host", new HashSet<Document>(), walls);
            var lengths = new double[4];
            var contacts = new List<Tuple<double, double>>[4];
            using (var options = new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside })
            {
                for (int i = 0; i < 4; i++)
                {
                    using (var edge = Line.CreateBound(points[i], points[(i + 1) % 4]))
                    {
                        lengths[i] = edge.Length;
                        contacts[i] = new List<Tuple<double, double>>();
                        foreach (var wall in walls)
                        {
                            try
                            {
                                // 在牆所在文件座標求交，避免鏡射連結的 Solid 轉換限制。
                                using (var localEdge = edge.CreateTransformed(wall.Transform.Inverse))
                                using (var intersection = wall.Solid.IntersectWithCurve(localEdge, options))
                                {
                                    for (int s = 0; s < intersection.SegmentCount; s++)
                                    {
                                        using (var segment = intersection.GetCurveSegment(s))
                                        {
                                            double a = (wall.Transform.OfPoint(segment.GetEndPoint(0)) - points[i]).DotProduct(edge.Direction);
                                            double b = (wall.Transform.OfPoint(segment.GetEndPoint(1)) - points[i]).DotProduct(edge.Direction);
                                            contacts[i].Add(Tuple.Create(Math.Min(a, b), Math.Max(a, b)));
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                throw new InvalidOperationException("開口邊 " + (i + 1) + " 與 " + wall.Description + " 求交失敗。", ex);
                            }
                        }
                    }
                }
            }
            // 重疊牆的接觸區段合併，不外推，且僅在這裡套用一次預算倍率 2。
            return UnitUtils.ConvertFromInternalUnits(CalculateExposedPerimeter(lengths, contacts, 0), UnitTypeId.Millimeters);
        }

        private static double ReadOpeningDimension(Element element, string name)
        {
            var parameter = element.LookupParameter(name);
            if (parameter == null || parameter.StorageType != StorageType.Double || !parameter.HasValue)
                throw new InvalidOperationException("缺少有效開口尺寸：" + name);
            double value = parameter.AsDouble();
            if (!double.IsFinite(value) || value <= element.Document.Application.ShortCurveTolerance)
                throw new InvalidOperationException("開口尺寸必須大於 Revit 最短曲線長度：" + name);
            return value;
        }

        private double CalculateMillimeters(Element element, string lengthParameter, string widthParameter, double offsetMillimeters)
        {
            var footprint = FindFootprint(element, lengthParameter, widthParameter);
            var edges = footprint.Item1;
            double bottom = footprint.Item2;
            double top = edges[0].GetEndPoint(0).Z;
            var center = edges.Select(e => e.GetEndPoint(0)).Aggregate(XYZ.Zero, (a, b) => a + b) / 4;
            var walls = new List<WallFace>();
            CollectNearbyWalls(document, Transform.Identity, element.get_BoundingBox(null), walls, "host", new HashSet<Document>());
            var lengths = edges.Select(e => e.Length).ToArray();
            var contacts = new List<Tuple<double, double>>[4];
            for (int i = 0; i < 4; i++)
            {
                XYZ start = edges[i].GetEndPoint(0);
                XYZ direction = (edges[i].GetEndPoint(1) - start).Normalize();
                XYZ normal = direction.CrossProduct(XYZ.BasisZ).Normalize();
                if (normal.DotProduct(start - center) < 0) normal = -normal;
                var intervals = new List<Tuple<double, double>>();
                foreach (var face in walls)
                {
                    // 相向且平行的側面才算貼牆，排除垂直相交、牆頂及只有角點接觸。
                    if (normal.DotProduct(face.Normal) > -1 + Epsilon ||
                        Math.Abs((face.Origin - start).DotProduct(normal)) > ContactTolerance) continue;
                    foreach (var triangle in face.Triangles)
                    {
                        var polygon = Clip(triangle.ToList(), p => p.Z - bottom);
                        polygon = Clip(polygon, p => top - p.Z);
                        polygon = Clip(polygon, p => (p - start).DotProduct(direction));
                        polygon = Clip(polygon, p => lengths[i] - (p - start).DotProduct(direction));
                        if (polygon.Count < 3 || polygon.Max(p => p.Z) - polygon.Min(p => p.Z) <= Epsilon) continue;
                        double from = polygon.Min(p => (p - start).DotProduct(direction));
                        double to = polygon.Max(p => (p - start).DotProduct(direction));
                        if (to - from > Epsilon) intervals.Add(Tuple.Create(from, to));
                    }
                }
                contacts[i] = intervals;
            }
            double offset = UnitUtils.ConvertToInternalUnits(offsetMillimeters, UnitTypeId.Millimeters);
            return UnitUtils.ConvertFromInternalUnits(CalculateExposedPerimeter(lengths, contacts, offset), UnitTypeId.Millimeters);
        }

        // 四邊依 CurveLoop 順序排列，所有數值使用相同單位；回傳已包含預算倍率 2。
        // 只在兩側都未貼牆的原始轉角延伸相接，接觸區段的端點截止、不額外繞回基座。
        internal static double CalculateExposedPerimeter(double[] lengths, List<Tuple<double, double>>[] contacts, double offset)
        {
            var ranges = contacts.Select((items, i) => items
                .Select(r => Tuple.Create(Math.Max(0, r.Item1), Math.Min(lengths[i], r.Item2)))
                .Where(r => r.Item2 - r.Item1 > Epsilon).ToList()).ToArray();
            double total = lengths.Select((length, i) => Math.Max(0, length - UnionLength(ranges[i]))).Sum();
            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                bool endTouches = ranges[i].Any(r => r.Item2 >= lengths[i] - Epsilon);
                bool nextStartTouches = ranges[next].Any(r => r.Item1 <= Epsilon);
                if (!endTouches && !nextStartTouches) total += 2 * offset;
            }
            return total * 2;
        }

        private static Tuple<List<Curve>, double> FindFootprint(Element element, string lengthParameter, string widthParameter)
        {
            double length = element.LookupParameter(lengthParameter)?.AsDouble() ?? 0;
            double width = element.LookupParameter(widthParameter)?.AsDouble() ?? 0;
            if (length <= 0 || width <= 0)
                throw new InvalidOperationException("缺少有效尺寸參數：" + lengthParameter + "／" + widthParameter);
            var candidates = new List<Tuple<List<Curve>, double>>();
            foreach (var solid in Solids(element.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine })))
            {
                var points = solid.Edges.Cast<Edge>().SelectMany(e => e.Tessellate()).ToList();
                if (points.Count == 0) continue;
                double bottom = points.Min(p => p.Z);
                foreach (Face face in solid.Faces)
                {
                    var plane = face as PlanarFace;
                    if (plane == null || plane.FaceNormal.Z < 1 - Epsilon) continue;
                    // 只取面的外圈，不將內框／孔洞當作基座外側。
                    var loop = face.GetEdgesAsCurveLoops().OrderByDescending(l => l.Sum(c => c.Length)).FirstOrDefault();
                    if (loop == null) continue;
                    var edges = loop.ToList();
                    if (edges.Count != 4 || edges.Any(e => !(e is Line))) continue;
                    if (edges[0].GetEndPoint(0).Z - bottom <= Epsilon) continue;
                    if (!Matches(edges[0].Length, length) || !Matches(edges[1].Length, width))
                    {
                        if (!Matches(edges[0].Length, width) || !Matches(edges[1].Length, length)) continue;
                    }
                    if (!Matches(edges[0].Length, edges[2].Length) || !Matches(edges[1].Length, edges[3].Length)) continue;
                    if (Math.Abs(((Line)edges[0]).Direction.DotProduct(((Line)edges[1]).Direction)) > Epsilon) continue;
                    candidates.Add(Tuple.Create(edges, bottom));
                }
            }
            if (candidates.Count != 1)
                throw new InvalidOperationException("無法唯一識別符合「" + lengthParameter + "／" + widthParameter + "」的水平矩形實體外框，請檢查族群幾何（不使用模型線或粉刷外框代替）。");
            return candidates[0];
        }

        private static bool Matches(double a, double b) { return Math.Abs(a - b) <= ContactTolerance; }

        private void CollectNearbyWalls(Document source, Transform transform, BoundingBoxXYZ hostBox,
            List<WallFace> result, string path, HashSet<Document> ancestors, List<WallVolume> volumes = null)
        {
            if (!ancestors.Add(source)) return;
            try
            {
                // 將主檔包圍盒的八個角轉回連結座標；旋轉／鏡射連結不能只轉 Min、Max。
                var corners = new List<XYZ>();
                for (int x = 0; x < 2; x++)
                    for (int y = 0; y < 2; y++)
                        for (int z = 0; z < 2; z++)
                            corners.Add(transform.Inverse.OfPoint(hostBox.Transform.OfPoint(new XYZ(
                                x == 0 ? hostBox.Min.X : hostBox.Max.X, y == 0 ? hostBox.Min.Y : hostBox.Max.Y,
                                z == 0 ? hostBox.Min.Z : hostBox.Max.Z))));
                XYZ margin = new XYZ(ContactTolerance, ContactTolerance, ContactTolerance);
                using (var outline = new Outline(new XYZ(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z)) - margin,
                    new XYZ(corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z)) + margin))
                using (var filter = new BoundingBoxIntersectsFilter(outline))
                {
                    foreach (var wall in new FilteredElementCollector(source).OfClass(typeof(Wall)).WherePasses(filter))
                    {
                        string key = path + "/" + wall.Id.Value;
                        if (volumes != null)
                        {
                            if (!wallVolumeCache.TryGetValue(key, out var cachedVolumes))
                            {
                                cachedVolumes = OpeningWallSolids(wall.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }))
                                    .Select(s => new WallVolume { Solid = s, Transform = transform,
                                        Description = source.Title + "／牆 ID " + wall.Id.Value + "／" + path }).ToList();
                                if (cachedVolumes.Count == 0)
                                    throw new InvalidOperationException(source.Title + "／牆 ID " + wall.Id.Value + " 找不到可求交的非空實體。");
                                wallVolumeCache.Add(key, cachedVolumes);
                            }
                            volumes.AddRange(cachedVolumes);
                            continue;
                        }
                        List<WallFace> faces;
                        if (!wallCache.TryGetValue(key, out faces))
                        {
                            faces = new List<WallFace>();
                            foreach (var solid in Solids(wall.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine })))
                                foreach (Face face in solid.Faces)
                                {
                                    var plane = face as PlanarFace;
                                    if (plane == null) continue; // 曲面牆與直線基座的切點沒有可扣除的貼合長度。
                                    XYZ normal = transform.OfVector(plane.FaceNormal).Normalize();
                                    if (Math.Abs(normal.Z) > Epsilon) continue;
                                    var triangles = new List<XYZ[]>();
                                    var mesh = face.Triangulate();
                                    for (int t = 0; t < mesh.NumTriangles; t++)
                                    {
                                        var triangle = mesh.get_Triangle(t);
                                        triangles.Add(Enumerable.Range(0, 3).Select(v => transform.OfPoint(triangle.get_Vertex(v))).ToArray());
                                    }
                                    faces.Add(new WallFace { Origin = transform.OfPoint(plane.Origin), Normal = normal, Triangles = triangles });
                                }
                            wallCache.Add(key, faces);
                        }
                        result.AddRange(faces);
                    }
                }
                foreach (RevitLinkInstance link in new FilteredElementCollector(source).OfClass(typeof(RevitLinkInstance)))
                {
                    var linkedDocument = link.GetLinkDocument();
                    if (linkedDocument != null)
                        CollectNearbyWalls(linkedDocument, transform.Multiply(link.GetTotalTransform()), hostBox,
                            result, path + "/link:" + link.Id.Value, ancestors, volumes);
                }
            }
            finally { ancestors.Remove(source); }
        }

        private static IEnumerable<Solid> Solids(GeometryElement geometry)
        {
            if (geometry == null) yield break;
            foreach (GeometryObject item in geometry)
            {
                var solid = item as Solid;
                if (solid != null && solid.Volume > Epsilon) yield return solid;
                var instance = item as GeometryInstance;
                if (instance != null)
                    foreach (var nested in Solids(instance.GetInstanceGeometry())) yield return nested;
            }
        }

        // 僅供樓板開口的牆求交路徑使用；不更動基座已驗證的幾何取得方式。
        private static IEnumerable<Solid> OpeningWallSolids(GeometryElement geometry)
        {
            if (geometry == null) yield break;
            foreach (GeometryObject item in geometry)
            {
                if (item is Solid solid && solid.Faces.Size > 0 && solid.Edges.Size > 0)
                {
                    double volume = solid.Volume; // signed volume；負值本身不代表無效
                    if (double.IsFinite(volume) && Math.Abs(volume) > Epsilon) yield return solid;
                }
                else if (item is GeometryInstance instance)
                    foreach (var nested in OpeningWallSolids(instance.GetInstanceGeometry())) yield return nested;
            }
        }

        private static List<XYZ> Clip(List<XYZ> polygon, Func<XYZ, double> distance)
        {
            var result = new List<XYZ>();
            if (polygon.Count == 0) return result;
            XYZ previous = polygon[polygon.Count - 1];
            double previousDistance = distance(previous);
            foreach (XYZ current in polygon)
            {
                double currentDistance = distance(current);
                if ((previousDistance >= 0) != (currentDistance >= 0))
                    result.Add(previous + (current - previous) * (previousDistance / (previousDistance - currentDistance)));
                if (currentDistance >= 0) result.Add(current);
                previous = current;
                previousDistance = currentDistance;
            }
            return result;
        }

        private static double UnionLength(List<Tuple<double, double>> intervals)
        {
            double total = 0, end = double.NegativeInfinity;
            foreach (var interval in intervals.OrderBy(i => i.Item1))
            {
                if (interval.Item2 <= end) continue;
                total += interval.Item2 - Math.Max(interval.Item1, end);
                end = interval.Item2;
            }
            return total;
        }
    }
}
