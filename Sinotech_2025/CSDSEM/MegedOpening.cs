using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sinotech_2025.CSDSEM
{
    public class MegedOpening
    {
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
        }

        // 用於牆面 2D 投影碰撞偵測的包覆盒結構
        private class ProjectedBox
        {
            public MergedOpeningResult Result { get; set; }
            public double MinU { get; set; }
            public double MaxU { get; set; }
            public double MinZ { get; set; }
            public double MaxZ { get; set; }
        }

        /// <summary>
        /// 雙階段精準合併服務：1. 上下間距<=250mm連鎖疊加  2. 左右邊界碰觸合併
        /// </summary>
        public class OpeningMergeService
        {
            private readonly double _unitConversion = 304.8;
            private readonly double _verticalCenterGapMaxMm = 250.1; // 上下中心距容許值 (含 0.1mm 浮點誤差)
            private readonly double _stackAddHeightMm = 250.0;       // 每多一管向上疊加 250mm
            private readonly double _marginFeet = 100.0 / 304.8;     // 開口族群預設加寬 100mm 邊界

            public OpeningMergeService(double mergeThresholdMm = 250.0)
            {
                _verticalCenterGapMaxMm = mergeThresholdMm + 0.1;
            }

            public List<MergedOpeningResult> ProcessAndMergeCandidates(Element hostElement, List<CableTrayOpeningCandidate> candidates)
            {
                if (candidates == null || !candidates.Any())
                    return new List<MergedOpeningResult>();

                // 取得牆面的水平基準向量 (U軸)
                XYZ wallDirU = GetWallDirectionUnitVector(hostElement);

                // 第一階段：上下排列合併
                List<MergedOpeningResult> verticalMergedList = PerformVerticalMerge(hostElement, candidates, wallDirU);

                // 第二階段：左右碰觸合併
                List<MergedOpeningResult> finalMergedList = PerformHorizontalBoundaryMerge(hostElement, verticalMergedList, wallDirU);

                return finalMergedList;
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

            /// <summary>
            /// 第一階段：上下連鎖合併 (Union-Find 聚類)
            /// </summary>
            private List<MergedOpeningResult> PerformVerticalMerge(Element host, List<CableTrayOpeningCandidate> candidates, XYZ wallDirU)
            {
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

                            // 1. 3D 中心點距離
                            double distMm = c1.IntersectionCenter.DistanceTo(c2.IntersectionCenter) * _unitConversion;

                            // 2. 牆面 U 軸水平偏移 (確保真的是上下排列)
                            double u1 = (c1.IntersectionCenter.X * wallDirU.X) + (c1.IntersectionCenter.Y * wallDirU.Y);
                            double u2 = (c2.IntersectionCenter.X * wallDirU.X) + (c2.IntersectionCenter.Y * wallDirU.Y);
                            double shiftUMm = Math.Abs(u1 - u2) * _unitConversion;

                            if (distMm <= _verticalCenterGapMaxMm && shiftUMm <= 100.0)
                            {
                                Union(i, j); // 條件符合，連鎖合併
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
                        results.Add(CalculateVerticalClusterGeometry(host, cluster));
                    }
                }

                return results;
            }

            private MergedOpeningResult CalculateVerticalClusterGeometry(Element host, List<CableTrayOpeningCandidate> cluster)
            {
                var lowest = cluster.OrderBy(c => c.IntersectionCenter.Z).First();
                int count = cluster.Count;

                // 物理開口的底部 Z 座標不變 (等於最底下電纜架的開口底部)
                double openingBottomZFeet = lowest.IntersectionCenter.Z - (lowest.OriginalHeightFeet / 2.0) - (_marginFeet / 2.0);

                // 計算參數 '電纜架高度'：每多一管向上疊加 250mm
                double paramTrayHeightFeet = lowest.OriginalHeightFeet + ((count - 1) * _stackAddHeightMm / _unitConversion);

                // 總物理開口高度 = 參數高度 + 100mm餘裕
                double totalOpeningHeightFeet = paramTrayHeightFeet + _marginFeet;

                // 新中心點 Z
                double centerZ = openingBottomZFeet + (totalOpeningHeightFeet / 2.0);
                double centerX = cluster.Average(c => c.IntersectionCenter.X);
                double centerY = cluster.Average(c => c.IntersectionCenter.Y);

                var result = new MergedOpeningResult
                {
                    LeaderElement = lowest.CableTrayElement,
                    DocName = lowest.DocName,
                    PipeType = lowest.PipeType,
                    Axis = lowest.Axis,
                    PipeAngle = lowest.PipeAngle,
                    WallThickness = lowest.WallThickness,
                    CableTrayWidthFeet = lowest.OriginalWidthFeet,
                    FinalOpeningWidthFeet = lowest.OriginalWidthFeet, // 上下合併，寬度維持原電纜架寬度
                    FinalOpeningHeightFeet = paramTrayHeightFeet, // 這裡存的是「電纜架高度」參數值
                    LowestCableTrayId = lowest.CableTrayId,
                    GeneratedComment = $"{lowest.DocName}_{lowest.CableTrayId}_{lowest.HostDocName}_{lowest.HostElementId}",
                    PlacementCenter = new XYZ(centerX, centerY, centerZ)
                };

                if (host.Document.GetElement(host.LevelId) is Level baseLevel)
                {
                    result.DeviationFeet = centerZ - baseLevel.ProjectElevation;
                }
                else
                {
                    result.DeviationFeet = lowest.Deviation + (centerZ - lowest.IntersectionCenter.Z);
                }

                return result;
            }

            /// <summary>
            /// 第二階段：左右邊界碰撞合併 (Union-Find 聚類)
            /// </summary>
            private List<MergedOpeningResult> PerformHorizontalBoundaryMerge(Element host, List<MergedOpeningResult> verticalResults, XYZ wallDirU)
            {
                if (verticalResults.Count <= 1) return verticalResults;

                // 將開口參數轉換為 2D 牆面真正的「物理包覆盒」
                var boxes = verticalResults.Select(r =>
                {
                    double uCenter = (r.PlacementCenter.X * wallDirU.X) + (r.PlacementCenter.Y * wallDirU.Y);

                    // 加上族群隱藏的 100mm 邊界，求出真正的物理邊界
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

                        // 1. 垂直高程 Z 軸重疊檢測 (容許 1mm 誤差)
                        bool isVerticalOverlap = !(b1.MaxZ < b2.MinZ - 0.003 || b2.MaxZ < b1.MinZ - 0.003);

                        // 2. 水平 U 軸「碰觸」檢測 (容許 1mm 誤差)
                        bool isHorizontalTouching = !(b1.MaxU < b2.MinU - 0.003 || b2.MaxU < b1.MinU - 0.003);

                        if (isVerticalOverlap && isHorizontalTouching)
                        {
                            Union(i, j); // 條件符合，啟動左右合併
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

                // 取得最大物理外框邊界
                double maxZ = cluster.Max(b => b.MaxZ);
                double minZ = cluster.Min(b => b.MinZ);
                double maxU = cluster.Max(b => b.MaxU);
                double minU = cluster.Min(b => b.MinU);

                double totalPhysWidth = maxU - minU;
                double totalPhysHeight = maxZ - minZ;

                double centerU = (maxU + minU) / 2.0;
                double centerZ = (maxZ + minZ) / 2.0;

                // 由 U 坐標反推回 WCS 空間的 XYZ
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

                    // 扣回 100mm 邊界餘裕，還原為族群參數所需輸入的值
                    CableTrayWidthFeet = totalPhysWidth - _marginFeet,
                    FinalOpeningWidthFeet = totalPhysWidth - _marginFeet,
                    FinalOpeningHeightFeet = totalPhysHeight - _marginFeet,

                    PlacementCenter = newCenterWCS,
                    LowestCableTrayId = leader.LowestCableTrayId,
                    GeneratedComment = leader.GeneratedComment
                };

                if (host.Document.GetElement(host.LevelId) is Level baseLevel)
                {
                    mergedResult.DeviationFeet = centerZ - baseLevel.ProjectElevation;
                }
                else
                {
                    mergedResult.DeviationFeet = leader.DeviationFeet + (centerZ - leader.PlacementCenter.Z);
                }

                return mergedResult;
            }
        }

        // 下方保留原有的 FloorOpeningCandidate, MergedFloorOpeningResult, FloorOpeningMergeService 等...

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

            public Line Axis { get; set; }
            public double PipeAngle { get; set; }
            public double Number { get; set; }
        }

        public class MergedFloorOpeningResult
        {
            public Element LeaderElement { get; set; }
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
                    var currentCluster = new List<FloorOpeningCandidate>();

                    for (int i = 0; i < sortedCandidates.Count; i++)
                    {
                        var current = sortedCandidates[i];

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

                return results;
            }

            private MergedFloorOpeningResult CalculateMergedFloorGeometry(List<FloorOpeningCandidate> cluster)
            {
                var leader = cluster.OrderBy(c => c.IntersectionCenter.Z).First();
                var result = new MergedFloorOpeningResult
                {
                    LeaderElement = leader.PipeOrDuctElement,
                    DocName = leader.DocName ?? string.Empty,
                    ElementType = leader.ElementType ?? string.Empty,
                    PipeType = leader.PipeType ?? string.Empty,
                    ReferenceLevel = leader.Level,
                    PipeSizeFeet = leader.PipeSizeFeet,
                    SpecifiedDiameterFeet = leader.PipeDiameterFeet,
                    DuctWidthFeet = leader.DuctWidthFeet,
                    DuctHeightFeet = leader.DuctHeightFeet,
                    Axis = leader.Axis,
                    PipeAngle = leader.PipeAngle,
                    Number = leader.Number
                };

                foreach (var candidate in cluster)
                {
                    if (candidate.HostFloorElement != null)
                    {
                        result.MergedFloorIds.Add(candidate.HostFloorElement.Id);
                    }
                }

                if (cluster.Count == 1)
                {
                    result.TotalThicknessFeet = leader.SingleFloorThicknessFeet;
                    result.PlacementCenter = leader.IntersectionCenter;

                    if (leader.Level != null)
                    {
                        result.DeviationFeet = leader.IntersectionCenter.Z - leader.Level.ProjectElevation;
                    }
                    return result;
                }

                double maxTopZ = cluster.Max(c => Math.Max(c.EntryZ, c.ExitZ));
                double minBottomZ = cluster.Min(c => Math.Min(c.EntryZ, c.ExitZ));

                result.TotalThicknessFeet = maxTopZ - minBottomZ;

                double centerZ = (maxTopZ + minBottomZ) / 2.0;
                double centerX = cluster.Average(c => c.IntersectionCenter.X);
                double centerY = cluster.Average(c => c.IntersectionCenter.Y);

                result.PlacementCenter = new XYZ(centerX, centerY, centerZ);

                if (leader.Level != null)
                {
                    result.DeviationFeet = centerZ - leader.Level.ProjectElevation;
                }

                return result;
            }
        }
    }
}