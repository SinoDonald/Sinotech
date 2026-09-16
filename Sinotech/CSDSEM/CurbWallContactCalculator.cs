using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sinotech.CSDSEM
{
    // 幾何運算維持 Revit 內部單位，最後才換成 PCCES 所用的 mm。
    internal sealed class CurbWallContactCalculator
    {
        private readonly Document document;
        private readonly Dictionary<string, List<WallFace>> wallCache = new Dictionary<string, List<WallFace>>();
        private static readonly double ContactTolerance = UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Millimeters);
        private const double Epsilon = 1e-8;

        private sealed class WallFace
        {
            public XYZ Origin;
            public XYZ Normal;
            public List<XYZ[]> Triangles;
        }

        public CurbWallContactCalculator(Document document) { this.document = document; }

        public double CalculateMillimeters(Element element)
        {
            var footprint = FindFootprint(element);
            var edges = footprint.Item1;
            double bottom = footprint.Item2;
            double top = edges[0].GetEndPoint(0).Z;
            var center = edges.Select(e => e.GetEndPoint(0)).Aggregate(XYZ.Zero, (a, b) => a + b) / 4;
            var walls = new List<WallFace>();
            CollectNearbyWalls(document, Transform.Identity, element.get_BoundingBox(null), walls, "host", new HashSet<Document>());
            var lengths = edges.Select(e => e.Length).ToArray();
            var contacts = new double[4];
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
                contacts[i] = UnionLength(intervals);
            }
            // 完整貼牆僅容許數值誤差；1 mm 的面距容差不拿來吞掉未貼牆區段。
            var full = lengths.Select((length, i) => length - contacts[i] <= Epsilon).ToArray();
            double total = 0;
            double allowance = UnitUtils.ConvertToInternalUnits(10, UnitTypeId.Millimeters);
            for (int i = 0; i < 4; i++)
            {
                if (full[i]) continue;
                total += Math.Max(0, lengths[i] - contacts[i]);
                // 部分貼牆仍沿用原本該邊的端部加計；只有相鄰整邊貼牆才取消 10 mm。
                if (!full[(i + 3) % 4]) total += allowance;
                if (!full[(i + 1) % 4]) total += allowance;
            }
            return UnitUtils.ConvertFromInternalUnits(total * 2, UnitTypeId.Millimeters);
        }

        private static Tuple<List<Curve>, double> FindFootprint(Element element)
        {
            double length = element.LookupParameter("長度").AsDouble();
            double width = element.LookupParameter("寬度").AsDouble();
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
                throw new InvalidOperationException("無法唯一識別符合長度／寬度參數的矩形基座實體外框，請檢查族群幾何（不使用模型線或粉刷外框代替）。");
            return candidates[0];
        }

        private static bool Matches(double a, double b) { return Math.Abs(a - b) <= ContactTolerance; }

        private void CollectNearbyWalls(Document source, Transform transform, BoundingBoxXYZ hostBox,
            List<WallFace> result, string path, HashSet<Document> ancestors)
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
                            result, path + "/link:" + link.Id.Value, ancestors);
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
