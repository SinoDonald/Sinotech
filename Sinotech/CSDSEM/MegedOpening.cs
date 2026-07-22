using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sinotech.CSDSEM
{
    public class MegedOpening
    {
        /// <summary>
        /// 電纜架開口合併專用數據承載類別
        /// </summary>
        public class CableTrayOpeningCandidate
        {
            public Element CableTrayElement { get; set; }
            public string DocName { get; set; }
            public string PipeType { get; set; }

            /// <summary>
            /// 電纜架原始寬度 (單位: Feet)
            /// </summary>
            public double OriginalWidthFeet { get; set; }

            /// <summary>
            /// 電纜架原始高度 (單位: Feet)
            /// </summary>
            public double OriginalHeightFeet { get; set; }

            /// <summary>
            /// 穿牆交點中心 (單位: Feet XYZ)
            /// </summary>
            public XYZ IntersectionCenter { get; set; }

            /// <summary>
            /// 距離樓層的高程偏移 (單位: Feet)
            /// </summary>
            public double Deviation { get; set; }

            public Line Axis { get; set; }
            public double PipeAngle { get; set; }
            public double WallThickness { get; set; }
        }

        /// <summary>
        /// 負責處理開口幾何分群與合併核心邏輯的服務類別
        /// </summary>
        public class OpeningMergeService
        {
            private readonly double _unitConversion = 304.8; // 英呎轉公釐
            private readonly double _mergeThresholdMm;       // 合併淨距離閾值 (公釐)
            private readonly double _toleranceMm = 10.0;     // 幾何比對容許誤差 (公釐)

            /// <summary>
            /// 建構子
            /// </summary>
            /// <param name="mergeThresholdMm">鄰近開口合併的淨間距閾值 (單位: mm，預設 300mm)</param>
            public OpeningMergeService(double mergeThresholdMm = 300.0)
            {
                _mergeThresholdMm = mergeThresholdMm;
            }

            /// <summary>
            /// 核心方法：針對單一牆體/樑體內的電纜架穿孔進行高精確度鄰近分群與合併計算
            /// </summary>
            public List<MergedOpeningResult> ProcessAndMergeCandidates(Element hostElement, List<CableTrayOpeningCandidate> candidates)
            {
                var finalResults = new List<MergedOpeningResult>();

                if (candidates == null || !candidates.Any())
                    return finalResults;

                // 1. 依據電纜架的「寬度」進行第一層嚴格分群 (同寬度的才進行相疊合併)
                var widthGroups = candidates.GroupBy(c => Math.Round(c.OriginalWidthFeet * _unitConversion, 1));

                foreach (var widthGroup in widthGroups)
                {
                    // 2. 依垂直高程 Z 軸進行排序
                    var sortedCandidates = widthGroup.OrderBy(c => c.IntersectionCenter.Z).ToList();
                    var currentCluster = new List<CableTrayOpeningCandidate>();

                    for (int i = 0; i < sortedCandidates.Count; i++)
                    {
                        var current = sortedCandidates[i];

                        if (!currentCluster.Any())
                        {
                            currentCluster.Add(current);
                            continue;
                        }

                        var previous = currentCluster.Last();

                        // 3. 計算垂直淨間距 (Clear Distance)
                        double prevTopMm = (previous.IntersectionCenter.Z + (previous.OriginalHeightFeet / 2.0)) * _unitConversion;
                        double currBottomMm = (current.IntersectionCenter.Z - (current.OriginalHeightFeet / 2.0)) * _unitConversion;
                        double clearDistanceMm = currBottomMm - prevTopMm;

                        // 4. 平面水平位置檢驗 (避免斜向錯開誤判)
                        double horizontalShiftMm = Math.Abs(previous.IntersectionCenter.X - current.IntersectionCenter.X) * _unitConversion;
                        double depthShiftMm = Math.Abs(previous.IntersectionCenter.Y - current.IntersectionCenter.Y) * _unitConversion;

                        bool isVerticallyStacked = horizontalShiftMm < _toleranceMm && depthShiftMm < _toleranceMm;
                        bool isWithinThreshold = clearDistanceMm <= _mergeThresholdMm;

                        if (isVerticallyStacked && isWithinThreshold)
                        {
                            currentCluster.Add(current);
                        }
                        else
                        {
                            finalResults.Add(CalculateMergedGeometry(hostElement, currentCluster));
                            currentCluster = new List<CableTrayOpeningCandidate> { current };
                        }
                    }

                    if (currentCluster.Any())
                    {
                        finalResults.Add(CalculateMergedGeometry(hostElement, currentCluster));
                    }
                }

                return finalResults;
            }

            /// <summary>
            /// 計算最終合併幾何數據
            /// </summary>
            private MergedOpeningResult CalculateMergedGeometry(Element host, List<CableTrayOpeningCandidate> cluster)
            {
                var leader = cluster.First();
                var result = new MergedOpeningResult
                {
                    DocName = leader.DocName,
                    PipeType = leader.PipeType,
                    Axis = leader.Axis,
                    PipeAngle = leader.PipeAngle,
                    WallThickness = leader.WallThickness,
                    CableTrayWidthFeet = leader.OriginalWidthFeet // 關鍵：精確保存電纜架寬度
                };

                if (cluster.Count == 1)
                {
                    // 單一管件：不需合併，保持原電纜架高寬
                    result.FinalOpeningWidthFeet = leader.OriginalWidthFeet;
                    result.FinalOpeningHeightFeet = leader.OriginalHeightFeet; // 保持原高
                    result.PlacementCenter = leader.IntersectionCenter;
                    result.DeviationFeet = leader.Deviation;
                    return result;
                }

                // 多管合併：計算最上頂部與最下底部
                double maxTopFeet = cluster.Max(c => c.IntersectionCenter.Z + (c.OriginalHeightFeet / 2.0));
                double minBottomFeet = cluster.Min(c => c.IntersectionCenter.Z - (c.OriginalHeightFeet / 2.0));

                // 合併開口總高度 = 最上部頂部 - 最下部底部
                double totalHeightFeet = maxTopFeet - minBottomFeet;
                result.FinalOpeningHeightFeet = totalHeightFeet; // 寫入合併後的總高度

                // 中心點 = (最上 + 最下) / 2
                double centerZ = (maxTopFeet + minBottomFeet) / 2.0;
                double centerX = cluster.Average(c => c.IntersectionCenter.X);
                double centerY = cluster.Average(c => c.IntersectionCenter.Y);

                result.PlacementCenter = new XYZ(centerX, centerY, centerZ);

                // 計算中心點相對於樓層高程之偏移
                if (host.Document.GetElement(host.LevelId) is Level baseLevel)
                {
                    result.DeviationFeet = centerZ - baseLevel.ProjectElevation;
                }
                else
                {
                    result.DeviationFeet = leader.Deviation;
                }

                return result;
            }
        }

        /// <summary>
        /// 合併結果結構
        /// </summary>
        public class MergedOpeningResult
        {
            public string DocName { get; set; }
            public string PipeType { get; set; }
            public XYZ PlacementCenter { get; set; }
            public double CableTrayWidthFeet { get; set; }       // 電纜架原始寬度 (Feet)
            public double FinalOpeningWidthFeet { get; set; }    // 開口最終寬度 (Feet)
            public double FinalOpeningHeightFeet { get; set; }   // 開口最終高度 (Feet)
            public double WallThickness { get; set; }
            public double DeviationFeet { get; set; }
            public Line Axis { get; set; }
            public double PipeAngle { get; set; }
        }
        /// <summary>
        /// 管道穿樓版開口候選資料結構 (支援 Pipe, Duct, CableTray)
        /// </summary>
        public class FloorOpeningCandidate
        {
            public Element PipeOrDuctElement { get; set; }
            public Element HostFloorElement { get; set; }
            public string DocName { get; set; } = string.Empty;
            public string ElementType { get; set; } = string.Empty; // "Pipe", "Duct", "CableTray"
            public string PipeType { get; set; } = string.Empty;    // 系統類型
            public Level Level { get; set; }        // 參考樓層

            // 管件原始尺寸 (單位: Feet)
            public double PipeSizeFeet { get; set; }          // 水管直徑 / 風管寬度 / 電纜架寬度
            public double PipeDiameterFeet { get; set; }      // 開口指定直徑 / 套管直徑
            public double DuctWidthFeet { get; set; }         // 風管/電纜架 寬度
            public double DuctHeightFeet { get; set; }        // 風管/電纜架 高度
            public double InsulationThicknessFeet { get; set; }// 保溫層厚度

            // 貫穿幾何資訊 (單位: Feet WCS)
            public double EntryZ { get; set; }    // 進入樓板頂部 Z 坐標
            public double ExitZ { get; set; }     // 離開樓板底部 Z 坐標
            public XYZ IntersectionCenter { get; set; } // 幾何中心點
            public double SingleFloorThicknessFeet { get; set; } // 單一樓板厚度

            public Line Axis { get; set; }
            public double PipeAngle { get; set; }
            public double Number { get; set; }
        }

        /// <summary>
        /// 經聚類合併計算後的最終樓版開口結果
        /// </summary>
        public class MergedFloorOpeningResult
        {
            public string DocName { get; set; } = string.Empty;
            public string ElementType { get; set; } = string.Empty; // "Pipe", "Duct", "CableTray"
            public string PipeType { get; set; } = string.Empty;
            public Level ReferenceLevel { get; set; }

            // 最終開口幾何尺寸 (單位: Feet)
            public double PipeSizeFeet { get; set; }
            public double SpecifiedDiameterFeet { get; set; }
            public double DuctWidthFeet { get; set; }
            public double DuctHeightFeet { get; set; }
            public double TotalThicknessFeet { get; set; } // 合併後的總樓板/地坪厚度

            // 最終放置位置
            public XYZ PlacementCenter { get; set; }
            public double DeviationFeet { get; set; } // 相對於 ReferenceLevel 的高程偏移
            public Line Axis { get; set; }
            public double PipeAngle { get; set; }
            public double Number { get; set; }

            public List<ElementId> MergedFloorIds { get; set; } = new List<ElementId>();
        }

        /// <summary>
        /// 安全的管道識別分群鍵 (避免 NullReferenceException 與跨 Link 檔案 Id 重複)
        /// </summary>
        internal class SafePipeGroupKey : IEquatable<SafePipeGroupKey>
        {
            public string DocName { get; }
            public ElementId ElementId { get; }
            public string ElementType { get; }
            public string PipeType { get; }
            // 當 ElementId 為空時，使用平面二維座標作為備用特徵碼 (精度取至 1mm)
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
                    // 將英呎座標轉為公釐整數以利 Hash 比對 (1 feet = 304.8 mm)
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

                // 若兩者均有有效的 ElementId，優先使用 Id + DocName 判定
                if (ElementId != ElementId.InvalidElementId && other.ElementId != ElementId.InvalidElementId)
                {
                    return isSameDoc && ElementId == other.ElementId;
                }

                // 若任一 ElementId 為空，則退回使用平面座標 X,Y 進行微米級判定
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

        /// <summary>
        /// 樓版開口合併服務：解決地坪+RC樓板多層緊貼開孔問題，並防止跨樓層誤合併
        /// </summary>
        public class FloorOpeningMergeService
        {
            private readonly double _unitConversion = 304.8; // 英呎轉公釐
            private readonly double _maxMergeGapMm;          // 允許合併的最大樓板淨間距 (預設 150mm)
            private readonly double _horizontalShiftToleranceMm = 50.0; // 水平貫穿點容許誤差 (預設 50mm)

            /// <summary>
            /// 建構子
            /// </summary>
            /// <param name="maxMergeGapMm">地坪與RC樓板之間允許合併的最大淨距離 (單位: mm，超過此距離視為不同樓層不合併)</param>
            public FloorOpeningMergeService(double maxMergeGapMm = 150.0)
            {
                _maxMergeGapMm = maxMergeGapMm;
            }

            /// <summary>
            /// 核心方法：將同一管道穿過的所有樓版貫穿點進行一維幾何聚類與厚度累加
            /// </summary>
            /// <param name="candidates">同一管道穿過模型中所有樓版的貫穿候選紀錄</param>
            /// <returns>合併後的精準樓版開口結果</returns>
            public List<MergedFloorOpeningResult> ProcessAndMergeFloorOpenings(List<FloorOpeningCandidate> candidates)
            {
                var results = new List<MergedFloorOpeningResult>();

                // 防禦性程式設計：無效資料安全攔截
                if (candidates == null || !candidates.Any())
                    return results;

                // 1. 安全過濾並使用 SafePipeGroupKey 進行強健分群 (徹底修復 NullReferenceException)
                var validCandidates = candidates.Where(c => c != null && c.IntersectionCenter != null).ToList();
                var pipeGroups = validCandidates.GroupBy(c => new SafePipeGroupKey(c));

                foreach (var pipeGroup in pipeGroups)
                {
                    // 2. 將同一管道穿過的所有樓板，按 Z 軸高度 (由低到高) 排序
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

                        // 3. 計算兩樓板間的淨垂直間距 (Clear Gap Z)
                        double prevTopZFeet = Math.Max(previous.EntryZ, previous.ExitZ);
                        double currBottomZFeet = Math.Min(current.EntryZ, current.ExitZ);

                        // 淨距離 = 當前樓板底部 - 上一個樓板頂部
                        double gapMm = (currBottomZFeet - prevTopZFeet) * _unitConversion;

                        // 4. 水平位移檢查 (確保是同一垂直管)
                        double horizontalShiftMm = new XYZ(
                            previous.IntersectionCenter.X - current.IntersectionCenter.X,
                            previous.IntersectionCenter.Y - current.IntersectionCenter.Y,
                            0).GetLength() * _unitConversion;

                        // 5. 判定邊界條件：淨間距小於閾值 (如 150mm) 且水平未嚴重偏離
                        bool isCloseGap = gapMm <= _maxMergeGapMm;
                        bool isAligned = horizontalShiftMm <= _horizontalShiftToleranceMm;

                        if (isCloseGap && isAligned)
                        {
                            // 判定為地坪與RC板緊貼，納入同一個開口合併群組
                            currentCluster.Add(current);
                        }
                        else
                        {
                            // 判定為跨樓層或距離過遠，結算當前群組，並開啟新群組
                            results.Add(CalculateMergedFloorGeometry(currentCluster));
                            currentCluster = new List<FloorOpeningCandidate> { current };
                        }
                    }

                    // 結算最後一個群組
                    if (currentCluster.Any())
                    {
                        results.Add(CalculateMergedFloorGeometry(currentCluster));
                    }
                }

                return results;
            }

            /// <summary>
            /// 計算多層樓板 (如 地坪 + RC板) 合併後的最終厚度與中心點
            /// </summary>
            private MergedFloorOpeningResult CalculateMergedFloorGeometry(List<FloorOpeningCandidate> cluster)
            {
                var leader = cluster.First();
                var result = new MergedFloorOpeningResult
                {
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
                    // 單一樓板，無需合併
                    result.TotalThicknessFeet = leader.SingleFloorThicknessFeet;
                    result.PlacementCenter = leader.IntersectionCenter;

                    if (leader.Level != null)
                    {
                        result.DeviationFeet = leader.IntersectionCenter.Z - leader.Level.ProjectElevation;
                    }
                    return result;
                }

                // 多層樓板合併計算 (地坪 + RC 樓版)
                double maxTopZ = cluster.Max(c => Math.Max(c.EntryZ, c.ExitZ));
                double minBottomZ = cluster.Min(c => Math.Min(c.EntryZ, c.ExitZ));

                // 合併總厚度 = 最頂端地坪面 - 最底端RC板底面
                result.TotalThicknessFeet = maxTopZ - minBottomZ;

                // 幾何中心點 Z
                double centerZ = (maxTopZ + minBottomZ) / 2.0;
                double centerX = cluster.Average(c => c.IntersectionCenter.X);
                double centerY = cluster.Average(c => c.IntersectionCenter.Y);

                result.PlacementCenter = new XYZ(centerX, centerY, centerZ);

                // 計算相對樓層偏移
                if (leader.Level != null)
                {
                    result.DeviationFeet = centerZ - leader.Level.ProjectElevation;
                }

                return result;
            }
        }
    }
}
