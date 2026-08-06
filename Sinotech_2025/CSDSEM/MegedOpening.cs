using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sinotech_2025.CSDSEM
{
    public class MegedOpening
    {
        // -------------------------------------------------------------
        // 電纜架牆開口合併區塊
        // -------------------------------------------------------------
        public class CableTrayOpeningCandidate
        {
            public Element CableTrayElement { get; set; }
            public ElementId CableTrayId { get; set; }
            public string DocName { get; set; }
            public string HostDocName { get; set; }
            public ElementId HostElementId { get; set; }
            public string PipeType { get; set; }

            public double OriginalWidthFeet { get; set; }
            public double OriginalHeightFeet { get; set; }

            public XYZ IntersectionCenter { get; set; }
            public double Deviation { get; set; }

            public Line Axis { get; set; }
            public double PipeAngle { get; set; }
            public double WallThickness { get; set; }
        }

        public class MergedOpeningResult
        {
            public Element LeaderElement { get; set; }
            public string DocName { get; set; }
            public string PipeType { get; set; }
            public XYZ PlacementCenter { get; set; }
            public double CableTrayWidthFeet { get; set; }
            public double FinalOpeningWidthFeet { get; set; }
            public double FinalOpeningHeightFeet { get; set; }
            public double WallThickness { get; set; }
            public double DeviationFeet { get; set; }
            public Line Axis { get; set; }
            public double PipeAngle { get; set; }

            public ElementId LowestCableTrayId { get; set; }
            public string GeneratedComment { get; set; }
            public List<ElementId> IncludedCableTrayIds { get; set; } = new List<ElementId>();
        }

        private class ProjectedBox
        {
            public MergedOpeningResult Result { get; set; }
            public double MinU { get; set; }
            public double MaxU { get; set; }
            public double MinZ { get; set; }
            public double MaxZ { get; set; }
        }

        public class OpeningMergeService
        {
            private readonly double _unitConversion = 304.8;
            private readonly double _verticalCenterGapMaxMm = 250.1;
            private readonly double _stackAddHeightMm = 250.0;
            private readonly double _marginFeet = 100.0 / 304.8;
            private readonly double _clearanceFeet = 50.0 / 304.8;

            public OpeningMergeService(double mergeThresholdMm = 250.0)
            {
                _verticalCenterGapMaxMm = mergeThresholdMm + 0.1;
            }

            public List<MergedOpeningResult> ProcessAndMergeCandidates(Element hostElement, List<CableTrayOpeningCandidate> candidates)
            {
                if (candidates == null || !candidates.Any())
                    return new List<MergedOpeningResult>();

                XYZ wallDirU = GetWallDirectionUnitVector(hostElement);

                var verticalMergedResults = PerformVerticalMerge(hostElement, candidates, wallDirU, out HashSet<ElementId> mergedCableTrayIds);
                var finalMergedResults = PerformHorizontalMergeWithIdExclusion(hostElement, candidates, verticalMergedResults, mergedCableTrayIds, wallDirU);

                return finalMergedResults;
            }

            private XYZ GetWallDirectionUnitVector(Element host)
            {
                if (host is Wall wall && wall.Location is LocationCurve lc && lc.Curve is Line line)
                {
                    XYZ p0 = line.GetEndPoint(0);
                    XYZ p1 = line.GetEndPoint(1);
                    return new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0).Normalize();
                }
                return XYZ.BasisX;
            }

            private List<MergedOpeningResult> PerformVerticalMerge(
                Element host,
                List<CableTrayOpeningCandidate> candidates,
                XYZ wallDirU,
                out HashSet<ElementId> mergedCableTrayIds)
            {
                mergedCableTrayIds = new HashSet<ElementId>();
                var results = new List<MergedOpeningResult>();
                var widthGroups = candidates.GroupBy(c => Math.Round(c.OriginalWidthFeet * _unitConversion, 1));

                foreach (var widthGroup in widthGroups)
                {
                    var list = widthGroup.ToList();
                    int[] parent = new int[list.Count];
                    for (int i = 0; i < list.Count; i++) parent[i] = i;

                    int Find(int i) { return parent[i] == i ? i : (parent[i] = Find(parent[i])); }
                    void Union(int i, int j)
                    {
                        int rootI = Find(i); int rootJ = Find(j);
                        if (rootI != rootJ) parent[rootI] = rootJ;
                    }

                    for (int i = 0; i < list.Count; i++)
                    {
                        for (int j = i + 1; j < list.Count; j++)
                        {
                            var c1 = list[i];
                            var c2 = list[j];

                            double distMm = Math.Abs(c1.IntersectionCenter.Z - c2.IntersectionCenter.Z) * _unitConversion;

                            double u1 = (c1.IntersectionCenter.X * wallDirU.X) + (c1.IntersectionCenter.Y * wallDirU.Y);
                            double u2 = (c2.IntersectionCenter.X * wallDirU.X) + (c2.IntersectionCenter.Y * wallDirU.Y);
                            double shiftUMm = Math.Abs(u1 - u2) * _unitConversion;

                            if (distMm <= _verticalCenterGapMaxMm && shiftUMm <= 100.0)
                            {
                                Union(i, j);
                            }
                        }
                    }

                    var clusters = new Dictionary<int, List<CableTrayOpeningCandidate>>();
                    for (int i = 0; i < list.Count; i++)
                    {
                        int root = Find(i);
                        if (!clusters.ContainsKey(root)) clusters[root] = new List<CableTrayOpeningCandidate>();
                        clusters[root].Add(list[i]);
                    }

                    foreach (var cluster in clusters.Values)
                    {
                        var merged = CalculateVerticalClusterGeometry(host, cluster);
                        results.Add(merged);

                        if (cluster.Count > 1)
                        {
                            foreach (var item in cluster)
                            {
                                mergedCableTrayIds.Add(item.CableTrayId);
                            }
                        }
                    }
                }

                return results;
            }

            private MergedOpeningResult CalculateVerticalClusterGeometry(Element host, List<CableTrayOpeningCandidate> cluster)
            {
                var lowestCandidate = cluster.OrderBy(c => c.IntersectionCenter.Z).First();
                int count = cluster.Count;

                double lowestTrayBottomZFeet = lowestCandidate.IntersectionCenter.Z - (lowestCandidate.OriginalHeightFeet / 2.0);
                double openingBottomZFeet = lowestTrayBottomZFeet - _clearanceFeet;

                double paramTrayHeightFeet = lowestCandidate.OriginalHeightFeet + ((count - 1) * _stackAddHeightMm / _unitConversion);
                double totalPhysOpeningHeightFeet = paramTrayHeightFeet + _marginFeet;

                double centerZ = openingBottomZFeet + (totalPhysOpeningHeightFeet / 2.0);
                double centerX = cluster.Average(c => c.IntersectionCenter.X);
                double centerY = cluster.Average(c => c.IntersectionCenter.Y);

                var result = new MergedOpeningResult
                {
                    LeaderElement = lowestCandidate.CableTrayElement,
                    DocName = lowestCandidate.DocName,
                    PipeType = lowestCandidate.PipeType,
                    Axis = lowestCandidate.Axis,
                    PipeAngle = lowestCandidate.PipeAngle,
                    WallThickness = lowestCandidate.WallThickness,
                    CableTrayWidthFeet = lowestCandidate.OriginalWidthFeet,
                    FinalOpeningWidthFeet = lowestCandidate.OriginalWidthFeet,
                    FinalOpeningHeightFeet = paramTrayHeightFeet,
                    LowestCableTrayId = lowestCandidate.CableTrayId,
                    GeneratedComment = $"{lowestCandidate.DocName}_{lowestCandidate.CableTrayId}_{lowestCandidate.HostDocName}_{lowestCandidate.HostElementId}",
                    PlacementCenter = new XYZ(centerX, centerY, centerZ)
                };

                foreach (var item in cluster)
                {
                    result.IncludedCableTrayIds.Add(item.CableTrayId);
                }

                result.DeviationFeet = lowestCandidate.Deviation + (centerZ - lowestCandidate.IntersectionCenter.Z) + (25.0 / 304.8);

                return result;
            }

            private List<MergedOpeningResult> PerformHorizontalMergeWithIdExclusion(
                Element host,
                List<CableTrayOpeningCandidate> originalCandidates,
                List<MergedOpeningResult> verticalResults,
                HashSet<ElementId> mergedCableTrayIds,
                XYZ wallDirU)
            {
                if (verticalResults.Count <= 1) return verticalResults;

                var boxes = verticalResults.Select(r =>
                {
                    double uCenter = (r.PlacementCenter.X * wallDirU.X) + (r.PlacementCenter.Y * wallDirU.Y);
                    double physWidth = r.FinalOpeningWidthFeet + _marginFeet;
                    double physHeight = r.FinalOpeningHeightFeet + _marginFeet;

                    return new ProjectedBox
                    {
                        Result = r,
                        MinU = uCenter - (physWidth / 2.0),
                        MaxU = uCenter + (physWidth / 2.0),
                        MinZ = r.PlacementCenter.Z - (physHeight / 2.0),
                        MaxZ = r.PlacementCenter.Z + (physHeight / 2.0)
                    };
                }).ToList();

                int[] parent = new int[boxes.Count];
                for (int i = 0; i < boxes.Count; i++) parent[i] = i;

                int Find(int i) { return parent[i] == i ? i : (parent[i] = Find(parent[i])); }
                void Union(int i, int j)
                {
                    int rootI = Find(i); int rootJ = Find(j);
                    if (rootI != rootJ) parent[rootI] = rootJ;
                }

                for (int i = 0; i < boxes.Count; i++)
                {
                    for (int j = i + 1; j < boxes.Count; j++)
                    {
                        var b1 = boxes[i];
                        var b2 = boxes[j];

                        bool b1HasMergedId = b1.Result.IncludedCableTrayIds.Any(id => mergedCableTrayIds.Contains(id));
                        bool b2HasMergedId = b2.Result.IncludedCableTrayIds.Any(id => mergedCableTrayIds.Contains(id));

                        if (b1HasMergedId != b2HasMergedId)
                        {
                            continue;
                        }

                        double zDiffMm = Math.Abs(b1.Result.PlacementCenter.Z - b2.Result.PlacementCenter.Z) * _unitConversion;
                        bool isZParallel = zDiffMm <= 50.0;

                        double gapUMm = (Math.Max(b1.MinU, b2.MinU) - Math.Min(b1.MaxU, b2.MaxU)) * _unitConversion;
                        bool isHorizontalTouching = gapUMm <= 5.0;

                        if (isZParallel && isHorizontalTouching)
                        {
                            Union(i, j);
                        }
                    }
                }

                var clusters = new Dictionary<int, List<ProjectedBox>>();
                for (int i = 0; i < boxes.Count; i++)
                {
                    int root = Find(i);
                    if (!clusters.ContainsKey(root)) clusters[root] = new List<ProjectedBox>();
                    clusters[root].Add(boxes[i]);
                }

                var finalResults = new List<MergedOpeningResult>();
                foreach (var cluster in clusters.Values)
                {
                    if (cluster.Count == 1)
                        finalResults.Add(cluster[0].Result);
                    else
                        finalResults.Add(MergeHorizontalCluster(host, cluster, wallDirU));
                }

                return finalResults;
            }

            private MergedOpeningResult MergeHorizontalCluster(Element host, List<ProjectedBox> cluster, XYZ wallDirU)
            {
                var leaderBox = cluster.OrderBy(b => b.Result.PlacementCenter.Z).First();
                var leader = leaderBox.Result;

                double maxZ = cluster.Max(b => b.MaxZ);
                double minZ = cluster.Min(b => b.MinZ);
                double maxU = cluster.Max(b => b.MaxU);
                double minU = cluster.Min(b => b.MinU);

                double totalPhysWidth = maxU - minU;
                double totalPhysHeight = maxZ - minZ;

                double centerU = (maxU + minU) / 2.0;
                double centerZ = (maxZ + minZ) / 2.0;

                XYZ firstPt = cluster.First().Result.PlacementCenter;
                double currentU = (firstPt.X * wallDirU.X) + (firstPt.Y * wallDirU.Y);
                double deltaU = centerU - currentU;

                XYZ newCenterWCS = new XYZ(firstPt.X + (deltaU * wallDirU.X),
                                           firstPt.Y + (deltaU * wallDirU.Y),
                                           centerZ);

                var mergedResult = new MergedOpeningResult
                {
                    LeaderElement = leader.LeaderElement,
                    DocName = leader.DocName,
                    PipeType = leader.PipeType,
                    Axis = leader.Axis,
                    PipeAngle = leader.PipeAngle,
                    WallThickness = leader.WallThickness,

                    CableTrayWidthFeet = totalPhysWidth - _marginFeet,
                    FinalOpeningWidthFeet = totalPhysWidth - _marginFeet,
                    FinalOpeningHeightFeet = totalPhysHeight - _marginFeet,

                    PlacementCenter = newCenterWCS,
                    LowestCableTrayId = leader.LowestCableTrayId,
                    GeneratedComment = leader.GeneratedComment
                };

                mergedResult.DeviationFeet = leader.DeviationFeet + (centerZ - leader.PlacementCenter.Z);

                return mergedResult;
            }
        }

        // -------------------------------------------------------------
        // 樓板開口合併區塊 (Top-Anchored 頂層定錨高程修復)
        // -------------------------------------------------------------
        public class FloorOpeningCandidate
        {
            public Element PipeOrDuctElement { get; set; }
            public Element HostFloorElement { get; set; }
            public string DocName { get; set; } = string.Empty;
            public string ElementType { get; set; } = string.Empty;
            public string PipeType { get; set; } = string.Empty;
            public Level Level { get; set; }

            public double PipeSizeFeet { get; set; }
            public double PipeDiameterFeet { get; set; }
            public double DuctWidthFeet { get; set; }
            public double DuctHeightFeet { get; set; }
            public double InsulationThicknessFeet { get; set; }

            public double EntryZ { get; set; }
            public double ExitZ { get; set; }
            public XYZ IntersectionCenter { get; set; }
            public double SingleFloorThicknessFeet { get; set; }
            public double FloorHeightOffsetFeet { get; set; } // 【新增】：保留原生樓板的高度偏移量

            public Line Axis { get; set; }
            public double PipeAngle { get; set; }
            public double Number { get; set; }
        }

        public class MergedFloorOpeningResult
        {
            public Element LeaderElement { get; set; }
            public Element LeaderFloorElement { get; set; }
            public string DocName { get; set; } = string.Empty;
            public string ElementType { get; set; } = string.Empty;
            public string PipeType { get; set; } = string.Empty;
            public Level ReferenceLevel { get; set; }

            public double PipeSizeFeet { get; set; }
            public double SpecifiedDiameterFeet { get; set; }
            public double DuctWidthFeet { get; set; }
            public double DuctHeightFeet { get; set; }
            public double TotalThicknessFeet { get; set; }

            public XYZ PlacementCenter { get; set; }
            public double DeviationFeet { get; set; }
            public Line Axis { get; set; }
            public double PipeAngle { get; set; }
            public double Number { get; set; }

            public List<ElementId> MergedFloorIds { get; set; } = new List<ElementId>();
        }

        internal class SafePipeGroupKey : IEquatable<SafePipeGroupKey>
        {
            public string DocName { get; }
            public ElementId ElementId { get; }
            public string ElementType { get; }
            public string PipeType { get; }
            public long GridX { get; }
            public long GridY { get; }

            public SafePipeGroupKey(FloorOpeningCandidate candidate)
            {
                DocName = candidate.DocName ?? string.Empty;
                ElementId = candidate.PipeOrDuctElement?.Id ?? ElementId.InvalidElementId;
                ElementType = candidate.ElementType ?? string.Empty;
                PipeType = candidate.PipeType ?? string.Empty;

                if (candidate.IntersectionCenter != null)
                {
                    GridX = (long)Math.Round(candidate.IntersectionCenter.X * 304.8);
                    GridY = (long)Math.Round(candidate.IntersectionCenter.Y * 304.8);
                }
            }

            public bool Equals(SafePipeGroupKey other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;

                bool isSameDoc = string.Equals(DocName, other.DocName, StringComparison.OrdinalIgnoreCase);
                bool isSameType = string.Equals(ElementType, other.ElementType, StringComparison.OrdinalIgnoreCase);

                if (ElementId != ElementId.InvalidElementId && other.ElementId != ElementId.InvalidElementId)
                {
                    return isSameDoc && ElementId == other.ElementId;
                }

                return isSameDoc && isSameType && GridX == other.GridX && GridY == other.GridY;
            }

            public override bool Equals(object obj) => Equals(obj as SafePipeGroupKey);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 23 + (DocName?.GetHashCode() ?? 0);
                    if (ElementId != ElementId.InvalidElementId)
                    {
                        hash = hash * 23 + ElementId.GetHashCode();
                    }
                    else
                    {
                        hash = hash * 23 + ElementType.GetHashCode();
                        hash = hash * 23 + GridX.GetHashCode();
                        hash = hash * 23 + GridY.GetHashCode();
                    }
                    return hash;
                }
            }
        }

        public class FloorOpeningMergeService
        {
            private readonly double _unitConversion = 304.8;
            private readonly double _maxMergeGapMm;
            private readonly double _horizontalShiftToleranceMm = 50.0;
            private readonly double _maxFloorLevelSpanFeet = 3.28;

            public FloorOpeningMergeService(double maxMergeGapMm = 150.0)
            {
                _maxMergeGapMm = maxMergeGapMm;
            }

            public List<MergedFloorOpeningResult> ProcessAndMergeFloorOpenings(List<FloorOpeningCandidate> candidates)
            {
                var results = new List<MergedFloorOpeningResult>();

                if (candidates == null || !candidates.Any())
                    return results;

                var validCandidates = candidates.Where(c => c != null && c.IntersectionCenter != null).ToList();
                var pipeGroups = validCandidates.GroupBy(c => new SafePipeGroupKey(c));

                foreach (var pipeGroup in pipeGroups)
                {
                    var sortedCandidates = pipeGroup.OrderBy(c => c.IntersectionCenter.Z).ToList();

                    List<List<FloorOpeningCandidate>> floorLevelClusters = SplitByFloorLevel(sortedCandidates);

                    foreach (var levelCluster in floorLevelClusters)
                    {
                        var currentCluster = new List<FloorOpeningCandidate>();

                        for (int i = 0; i < levelCluster.Count; i++)
                        {
                            var current = levelCluster[i];

                            if (!currentCluster.Any())
                            {
                                currentCluster.Add(current);
                                continue;
                            }

                            var previous = currentCluster.Last();

                            double prevTopZFeet = Math.Max(previous.EntryZ, previous.ExitZ);
                            double currBottomZFeet = Math.Min(current.EntryZ, current.ExitZ);

                            double gapMm = (currBottomZFeet - prevTopZFeet) * _unitConversion;

                            double horizontalShiftMm = new XYZ(
                                previous.IntersectionCenter.X - current.IntersectionCenter.X,
                                previous.IntersectionCenter.Y - current.IntersectionCenter.Y,
                                0).GetLength() * _unitConversion;

                            bool isCloseGap = gapMm <= _maxMergeGapMm;
                            bool isAligned = horizontalShiftMm <= _horizontalShiftToleranceMm;

                            if (isCloseGap && isAligned)
                            {
                                currentCluster.Add(current);
                            }
                            else
                            {
                                results.Add(CalculateMergedFloorGeometry(currentCluster));
                                currentCluster = new List<FloorOpeningCandidate> { current };
                            }
                        }

                        if (currentCluster.Any())
                        {
                            results.Add(CalculateMergedFloorGeometry(currentCluster));
                        }
                    }
                }

                return results;
            }

            private List<List<FloorOpeningCandidate>> SplitByFloorLevel(List<FloorOpeningCandidate> sortedCandidates)
            {
                var floorClusters = new List<List<FloorOpeningCandidate>>();
                var currentFloorCluster = new List<FloorOpeningCandidate>();

                foreach (var candidate in sortedCandidates)
                {
                    if (!currentFloorCluster.Any())
                    {
                        currentFloorCluster.Add(candidate);
                        continue;
                    }

                    var lastCandidate = currentFloorCluster.Last();
                    double zSpanFeet = Math.Abs(candidate.IntersectionCenter.Z - lastCandidate.IntersectionCenter.Z);

                    if (zSpanFeet > _maxFloorLevelSpanFeet)
                    {
                        floorClusters.Add(currentFloorCluster);
                        currentFloorCluster = new List<FloorOpeningCandidate> { candidate };
                    }
                    else
                    {
                        currentFloorCluster.Add(candidate);
                    }
                }

                if (currentFloorCluster.Any())
                {
                    floorClusters.Add(currentFloorCluster);
                }

                return floorClusters;
            }

            /// <summary>
            /// 核心修復：以「最上方樓板」作為定錨點，100% 複製其 Offset 參數
            /// </summary>
            private MergedFloorOpeningResult CalculateMergedFloorGeometry(List<FloorOpeningCandidate> cluster)
            {
                // 以最上層樓板 (例如地坪) 作為主導 Leader
                var topCandidate = cluster.OrderByDescending(c => Math.Max(c.EntryZ, c.ExitZ)).First();

                var result = new MergedFloorOpeningResult
                {
                    LeaderElement = topCandidate.PipeOrDuctElement,
                    LeaderFloorElement = topCandidate.HostFloorElement, // 以最上方樓板為主
                    DocName = topCandidate.DocName ?? string.Empty,
                    ElementType = topCandidate.ElementType ?? string.Empty,
                    PipeType = topCandidate.PipeType ?? string.Empty,
                    ReferenceLevel = topCandidate.Level,
                    PipeSizeFeet = topCandidate.PipeSizeFeet,
                    SpecifiedDiameterFeet = topCandidate.PipeDiameterFeet,
                    DuctWidthFeet = topCandidate.DuctWidthFeet,
                    DuctHeightFeet = topCandidate.DuctHeightFeet,
                    Axis = topCandidate.Axis,
                    PipeAngle = topCandidate.PipeAngle,
                    Number = topCandidate.Number
                };

                foreach (var candidate in cluster)
                {
                    if (candidate.HostFloorElement != null)
                    {
                        result.MergedFloorIds.Add(candidate.HostFloorElement.Id);
                    }
                }

                // 總厚度 = 最頂端 - 最底端
                double maxTopZ = cluster.Max(c => Math.Max(c.EntryZ, c.ExitZ));
                double minBottomZ = cluster.Min(c => Math.Min(c.EntryZ, c.ExitZ));
                result.TotalThicknessFeet = maxTopZ - minBottomZ;

                double centerX = cluster.Average(c => c.IntersectionCenter.X);
                double centerY = cluster.Average(c => c.IntersectionCenter.Y);

                // 放置點 Z 座標強制對齊最頂部 (配合族群向下生長特性)
                result.PlacementCenter = new XYZ(centerX, centerY, maxTopZ);

                // 【核心對接】：距離樓層的高程 (Offset) 100% 複製最上方樓板的原生 Offset
                result.DeviationFeet = topCandidate.FloorHeightOffsetFeet;

                return result;
            }
        }
    }
}