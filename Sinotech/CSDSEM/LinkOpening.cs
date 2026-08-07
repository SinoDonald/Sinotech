using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static Sinotech.CSDSEM.MegedOpening;
using static Sinotech.CSDSEM.ProfessionalCodeForm;

namespace Sinotech.CSDSEM
{
    [Transaction(TransactionMode.Manual)]
    public class LinkOpening : IExternalCommand
    {
        private List<ElementId> startOpenings = new List<ElementId>();
        private List<XYZ> openingXYZs = new List<XYZ>();
        private List<int> newOpeningIds = new List<int>();

        /// <summary>
        /// 跨 Document 識別 Element 的唯一安全 Key (防範 Link Model Id 重複)
        /// </summary>
        private class UniqueElementKey : IEquatable<UniqueElementKey>
        {
            public string DocTitle { get; }
            public ElementId Id { get; }

            public UniqueElementKey(Element elem)
            {
                DocTitle = elem?.Document?.Title ?? string.Empty;
                Id = elem?.Id ?? ElementId.InvalidElementId;
            }

            public bool Equals(UniqueElementKey other)
            {
                if (other is null) return false;
                return string.Equals(DocTitle, other.DocTitle, StringComparison.OrdinalIgnoreCase) && Id == other.Id;
            }

            public override bool Equals(object obj) => Equals(obj as UniqueElementKey);
            public override int GetHashCode() => (DocTitle.GetHashCode() * 397) ^ Id.GetHashCode();
        }

        private class OpeningInfo
        {
            public string docName = string.Empty;
            public Element element { get; set; }
            public string type { get; set; }
            public double length { get; set; }
            public double width { get; set; }
            public double height { get; set; }
            public double thickness { get; set; }
            public double number { get; set; }
            public double beamWallAngle { get; set; }
            public Solid solid = null;
            public Level level { get; set; }
            public List<CrushElemInfo> crushElemInfos = new List<CrushElemInfo>();
        }

        private class CrushElemInfo
        {
            public string docName = string.Empty;
            public Element pipeOrDuct = null;
            public Solid pipeOrDuctSolid = null;
            public string type = string.Empty;
            public string pipeType = string.Empty;
            public double insulationThickness { get; set; }
            public string hostType = string.Empty;
            public double size { get; set; }
            public double diameter { get; set; }
            public double ductWight { get; set; }
            public double ductHeight { get; set; }
            public double bottomElevation { get; set; }
            public double thickness { get; set; }
            public Level level { get; set; }
            public List<Face> insfaces = new List<Face>();
            public List<XYZ> insXYZs = new List<XYZ>();
            public List<XYZ> xyzs = new List<XYZ>();
            public Line axis { get; set; }
            public double pipeAngle { get; set; }
            public List<Element> pipeOpens = new List<Element>();
            public string useFS = string.Empty;
            public double deviation { get; set; }
            public double number { get; set; }
            public string comment { get; set; } = string.Empty;
        }

        private class ElementTransform
        {
            public List<Element> elements = new List<Element>();
            public Transform transform { get; set; }
        }

        private static List<Level> docLevels = new List<Level>();
        List<LevelElevation> levelElevList = new List<LevelElevation>();
        double prjNS = 0.0;
        double prjWE = 0.0;
        double prjElev = 0.0;
        double elevationOffset = 0.0;
        int prjCode = 0;
        public static double unit_conversion = 304.8;
        public static double meter_conversion = 0.3048;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
            Document doc = uidoc.Document;

            List<string> docTokens = ParseFileNameTokens(doc);
            int prjCount = docTokens.Count;

            if (prjCount > 0)
            {
                try
                {
                    FindLevel findLevel = new FindLevel();
                    Tuple<List<LevelElevation>, LevelElevation, double> multiValue = findLevel.FindDocViewLevel(doc);
                    this.levelElevList = multiValue.Item1;

                    List<BasePoint> allPrjLocations = new FilteredElementCollector(doc).OfClass(typeof(BasePoint)).WhereElementIsNotElementType().Cast<BasePoint>().ToList();
                    List<BasePoint> prjLocations = allPrjLocations.Where(x => x.get_Parameter(BuiltInParameter.BASEPOINT_ANGLETON_PARAM) != null).ToList();
                    BasePoint prjLocation = prjLocations.Where(x => x.get_Parameter(BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM).AsDouble() ==
                                            prjLocations.Max(y => y.get_Parameter(BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM).AsDouble())).FirstOrDefault();
                    prjNS = prjLocation.get_Parameter(BuiltInParameter.BASEPOINT_NORTHSOUTH_PARAM).AsDouble() * meter_conversion;
                    prjWE = prjLocation.get_Parameter(BuiltInParameter.BASEPOINT_EASTWEST_PARAM).AsDouble() * meter_conversion;
                    prjElev = prjLocation.get_Parameter(BuiltInParameter.BASEPOINT_ELEVATION_PARAM).AsDouble() * meter_conversion;
                    try
                    {
                        string angleton = prjLocation.get_Parameter(BuiltInParameter.BASEPOINT_ANGLETON_PARAM).AsValueString();
                        if (angleton != null) { double angle = -Convert.ToDouble(angleton.Remove(angleton.Length - 1)); }
                    }
                    catch (Exception) { }

                    IList<ElementFilter> startOpeningFilters = new List<ElementFilter>();
                    startOpeningFilters.Add(new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory));
                    startOpeningFilters.Add(new ElementCategoryFilter(BuiltInCategory.OST_DuctAccessory));
                    startOpeningFilters.Add(new ElementCategoryFilter(BuiltInCategory.OST_CableTrayFitting));
                    LogicalOrFilter PDCFilter = new LogicalOrFilter(startOpeningFilters);
                    startOpenings = new FilteredElementCollector(doc).WherePasses(PDCFilter).WhereElementIsNotElementType().ToElementIds().ToList();

                    foreach (ElementId startOpening in startOpenings)
                    {
                        FamilyInstance opening = doc.GetElement(startOpening) as FamilyInstance;
                        LocationPoint lp = opening.Location as LocationPoint;
                        openingXYZs.Add(lp.Point);
                    }

                    docLevels = new FilteredElementCollector(doc).OfClass(typeof(Level)).WhereElementIsNotElementType().Cast<Level>().ToList();
                    List<RevitLinkInstance> pipeDuctLinkDocs = new List<RevitLinkInstance>();

                    IList<ElementFilter> pipeDuctFilters = new List<ElementFilter>();
                    pipeDuctFilters.Add(new ElementCategoryFilter(BuiltInCategory.OST_PipeCurves));
                    pipeDuctFilters.Add(new ElementCategoryFilter(BuiltInCategory.OST_PipeFitting));
                    pipeDuctFilters.Add(new ElementCategoryFilter(BuiltInCategory.OST_DuctCurves));
                    pipeDuctFilters.Add(new ElementCategoryFilter(BuiltInCategory.OST_DuctAccessory));
                    pipeDuctFilters.Add(new ElementCategoryFilter(BuiltInCategory.OST_CableTray));
                    pipeDuctFilters.Add(new ElementCategoryFilter(BuiltInCategory.OST_CableTrayFitting));
                    LogicalOrFilter pipeOrDuctFilter = new LogicalOrFilter(pipeDuctFilters);

                    IList<RevitLinkInstance> revitLinkInss = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).WhereElementIsNotElementType().Cast<RevitLinkInstance>().Where(x => x.GetLinkDocument() != null).ToList();
                    List<string> rvtLinkNames = revitLinkInss.Select(x => x.Name.Split(':')[0]).Distinct().ToList();
                    List<RevitLinkInstance> rvtLinkInsList = new List<RevitLinkInstance>();
                    foreach (string rvtLinkName in rvtLinkNames)
                    {
                        rvtLinkInsList.Add(revitLinkInss.Where(x => x.Name.Split(':')[0].Equals(rvtLinkName)).FirstOrDefault());
                    }

                    ProfessionalCodeForm professionalCodeForm = new ProfessionalCodeForm(rvtLinkInsList, prjCount);
                    professionalCodeForm.ShowDialog();

                    if (professionalCodeForm.trueOrFalse == true)
                    {
                        elevationOffset = professionalCodeForm.elevationOffset / unit_conversion;
                        List<RevitLinkInstance> chooseRevitLinks = rvtLinkInsList.Where(x => professionalCodeForm.prjNameAndCodes.Where(y => y.projectName.Equals(x.Name.Trim().Split(':')[0])).Count() > 0).ToList();

                        foreach (RevitLinkInstance rvtLinkIns in chooseRevitLinks)
                        {
                            IList<Element> pipeOrBeamList = new FilteredElementCollector(rvtLinkIns.GetLinkDocument()).WherePasses(pipeOrDuctFilter).WhereElementIsNotElementType().ToElements();
                            if (pipeOrBeamList.Count() > 0)
                            {
                                try { pipeDuctLinkDocs.Add(rvtLinkIns); }
                                catch (Autodesk.Revit.Exceptions.ArgumentNullException) { }
                            }
                        }

                        List<ProfessionalCode> combinePCodes = professionalCodeForm.combinePCodes;
                        prjCode = professionalCodeForm.prjCode;

                        DateTime timeStart = DateTime.Now;

                        // -------------------------------------------------------------------------
                        // 【核心功能 1 & 2】：檢索所有「落水頭」族群及其 Connectors 連結之管道
                        // -------------------------------------------------------------------------
                        HashSet<UniqueElementKey> floorDrainPipeKeys = CollectFloorDrainConnectedPipes(doc, pipeDuctLinkDocs);

                        List<OpeningInfo> openingInfoList = new List<OpeningInfo>();
                        IList<ElementFilter> elementFilters = new List<ElementFilter>();
                        elementFilters.Add(new ElementCategoryFilter(BuiltInCategory.OST_Walls));
                        elementFilters.Add(new ElementCategoryFilter(BuiltInCategory.OST_StructuralFraming));
                        elementFilters.Add(new ElementCategoryFilter(BuiltInCategory.OST_Floors));
                        LogicalOrFilter wallBeamFilter = new LogicalOrFilter(elementFilters);

                        foreach (RevitLinkInstance rvtLinkIns in rvtLinkInsList)
                        {
                            IList<Element> wallOrBeamElems = new FilteredElementCollector(rvtLinkIns.GetLinkDocument()).WherePasses(wallBeamFilter).WhereElementIsNotElementType().ToElements();
                            if (wallOrBeamElems.Count() > 0)
                            {
                                try
                                {
                                    foreach (Element elem in wallOrBeamElems)
                                    {
                                        string wallFamilyName = string.Empty;
                                        string wallTypeName = string.Empty;
                                        if (elem is Wall)
                                        {
                                            Wall wall = elem as Wall;
                                            wallFamilyName = wall.WallType.FamilyName;
                                            wallTypeName = wall.WallType.Name;
                                        }
                                        if (!wallFamilyName.Equals("帷幕牆") && !wallTypeName.Contains("輕隔間") && !wallTypeName.Contains("琺瑯") && !wallTypeName.Contains("廁所隔牆"))
                                        {
                                            Options opt = new Options();
                                            opt.ComputeReferences = true;
                                            opt.DetailLevel = doc.ActiveView.DetailLevel;
                                            GeometryElement geomElem = elem.get_Geometry(opt);

                                            foreach (GeometryObject geomObj in geomElem)
                                            {
                                                Solid solid = null;
                                                solid = GetSymbolSolids(geomObj, rvtLinkIns, solid);
                                                try
                                                {
                                                    if (solid.SurfaceArea != 0)
                                                    {
                                                        FindInputSolidBBElems(rvtLinkIns.GetLinkDocument(), elem, solid, pipeDuctLinkDocs, openingInfoList, professionalCodeForm.prjNameAndCodes);
                                                    }
                                                }
                                                catch (NullReferenceException)
                                                {
                                                    string error = elem.Id.ToString();
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Autodesk.Revit.Exceptions.ArgumentNullException) { }
                            }
                        }

                        OpeningMergeService mergeService = new OpeningMergeService(mergeThresholdMm: 250.0);
                        FloorOpeningMergeService floorMergeService = new FloorOpeningMergeService(maxMergeGapMm: 150.0);

                        List<FloorOpeningCandidate> globalFloorCandidates = new List<FloorOpeningCandidate>();

                        foreach (OpeningInfo openingInfo in openingInfoList)
                        {
                            var cableTrayCrushes = openingInfo.crushElemInfos
                                .Where(x => x.type.Equals("CableTray") || x.type.Equals("CableTrayFitting"))
                                .ToList();

                            if (cableTrayCrushes.Any())
                            {
                                List<CableTrayOpeningCandidate> candidates = new List<CableTrayOpeningCandidate>();
                                foreach (var crush in cableTrayCrushes)
                                {
                                    XYZ center = crush.xyzs.FirstOrDefault() ?? XYZ.Zero;
                                    Line axis = crush.axis ?? Line.CreateBound(center, new XYZ(center.X, center.Y, center.Z + 10));

                                    candidates.Add(new CableTrayOpeningCandidate
                                    {
                                        CableTrayElement = crush.pipeOrDuct,
                                        CableTrayId = crush.pipeOrDuct.Id,
                                        DocName = crush.docName,
                                        HostDocName = openingInfo.docName,
                                        HostElementId = openingInfo.element.Id,
                                        PipeType = crush.pipeType,
                                        OriginalWidthFeet = crush.ductWight,
                                        OriginalHeightFeet = crush.ductHeight,
                                        IntersectionCenter = center,
                                        Deviation = crush.deviation,
                                        Axis = axis,
                                        PipeAngle = crush.pipeAngle,
                                        WallThickness = crush.thickness
                                    });
                                }

                                List<MergedOpeningResult> mergedResults = mergeService.ProcessAndMergeCandidates(openingInfo.element, candidates);
                                openingInfo.crushElemInfos.RemoveAll(x => x.type.Equals("CableTray") || x.type.Equals("CableTrayFitting"));

                                foreach (var merged in mergedResults)
                                {
                                    CrushElemInfo mergedCrush = new CrushElemInfo
                                    {
                                        docName = merged.DocName,
                                        pipeOrDuct = merged.LeaderElement,
                                        type = "CableTray",
                                        hostType = openingInfo.type,
                                        level = openingInfo.level,
                                        ductWight = merged.CableTrayWidthFeet,
                                        ductHeight = merged.FinalOpeningHeightFeet,
                                        thickness = merged.WallThickness,
                                        xyzs = new List<XYZ> { merged.PlacementCenter },
                                        deviation = merged.DeviationFeet,
                                        axis = merged.Axis,
                                        pipeAngle = merged.PipeAngle,
                                        number = 0,
                                        useFS = openingInfo.type.Equals("Floor") ? "電纜架樓版開口" : "電纜架牆開口",
                                        comment = merged.GeneratedComment
                                    };

                                    openingInfo.crushElemInfos.Add(mergedCrush);
                                }
                            }

                            // 處理樓板交集點
                            if (openingInfo.type.Equals("Floor"))
                            {
                                var floorCrushes = openingInfo.crushElemInfos.ToList();
                                foreach (var crush in floorCrushes)
                                {
                                    // -------------------------------------------------------------
                                    // 【核心功能 3】：當管道與落水頭連結時，排除不進行樓板開口！
                                    // -------------------------------------------------------------
                                    if (crush.pipeOrDuct != null && floorDrainPipeKeys.Contains(new UniqueElementKey(crush.pipeOrDuct)))
                                    {
                                        continue; // 跳過落水頭連通管，不生成樓版開口！
                                    }

                                    XYZ originalCenter = crush.xyzs.FirstOrDefault() ?? XYZ.Zero;

                                    double floorOffsetFeet = 0.0;
                                    Parameter offsetParam = openingInfo.element.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                                    if (offsetParam != null)
                                    {
                                        floorOffsetFeet = offsetParam.AsDouble();
                                    }

                                    double entryZ = 0.0;
                                    double exitZ = 0.0;

                                    if (crush.insXYZs != null && crush.insXYZs.Count >= 2)
                                    {
                                        entryZ = crush.insXYZs.Max(p => p.Z);
                                        exitZ = crush.insXYZs.Min(p => p.Z);
                                    }
                                    else
                                    {
                                        double floorTopZ = 0.0;
                                        if (openingInfo.level != null)
                                        {
                                            floorTopZ = openingInfo.level.ProjectElevation;
                                        }

                                        floorTopZ += floorOffsetFeet;
                                        floorTopZ += elevationOffset;

                                        entryZ = floorTopZ;
                                        exitZ = floorTopZ - openingInfo.thickness;
                                    }

                                    double trueCenterZ = (entryZ + exitZ) / 2.0;
                                    XYZ preciseCenter = new XYZ(originalCenter.X, originalCenter.Y, trueCenterZ);

                                    globalFloorCandidates.Add(new FloorOpeningCandidate
                                    {
                                        PipeOrDuctElement = crush.pipeOrDuct,
                                        HostFloorElement = openingInfo.element,
                                        DocName = crush.docName,
                                        ElementType = crush.type,
                                        PipeType = crush.pipeType,
                                        Level = crush.level,
                                        PipeSizeFeet = crush.size,
                                        PipeDiameterFeet = crush.diameter,
                                        DuctWidthFeet = crush.ductWight,
                                        DuctHeightFeet = crush.ductHeight,
                                        InsulationThicknessFeet = crush.insulationThickness,
                                        EntryZ = entryZ,
                                        ExitZ = exitZ,
                                        IntersectionCenter = preciseCenter,
                                        SingleFloorThicknessFeet = crush.thickness,
                                        FloorHeightOffsetFeet = floorOffsetFeet,
                                        Axis = crush.axis,
                                        PipeAngle = crush.pipeAngle,
                                        Number = crush.number
                                    });
                                }
                                openingInfo.crushElemInfos.Clear();
                            }
                        }

                        // 執行全域多樓層合併
                        List<MergedFloorOpeningResult> mergedFloorResults = floorMergeService.ProcessAndMergeFloorOpenings(globalFloorCandidates);

                        foreach (var merged in mergedFloorResults)
                        {
                            var targetOpeningInfo = openingInfoList.FirstOrDefault(o => o.element.Id == merged.LeaderFloorElement.Id);
                            if (targetOpeningInfo != null)
                            {
                                CrushElemInfo mergedCrush = new CrushElemInfo
                                {
                                    docName = merged.DocName,
                                    pipeOrDuct = merged.LeaderElement,
                                    type = merged.ElementType,
                                    hostType = "Floor",
                                    level = merged.ReferenceLevel,
                                    size = merged.PipeSizeFeet,
                                    diameter = merged.SpecifiedDiameterFeet,
                                    ductWight = merged.DuctWidthFeet,
                                    ductHeight = merged.DuctHeightFeet,
                                    thickness = merged.TotalThicknessFeet,
                                    xyzs = new List<XYZ> { merged.PlacementCenter },
                                    deviation = merged.DeviationFeet,
                                    axis = merged.Axis,
                                    pipeAngle = merged.PipeAngle,
                                    number = merged.Number,
                                    comment = $"{merged.DocName}_{(merged.LeaderElement != null ? merged.LeaderElement.Id.ToString() : "0")}_{targetOpeningInfo.docName}_{targetOpeningInfo.element.Id}"
                                };

                                targetOpeningInfo.crushElemInfos.Add(mergedCrush);
                            }
                        }

                        TransactionGroup tranGrp1 = new TransactionGroup(doc, "自動開口");
                        tranGrp1.Start();
                        int amount = 0;
                        using (Transaction trans = new Transaction(doc, "放置開口"))
                        {
                            FailureHandlingOptions options = trans.GetFailureHandlingOptions();
                            MyPreProcessor preproccessor = new MyPreProcessor();
                            options.SetClearAfterRollback(true);
                            options.SetFailuresPreprocessor(preproccessor);
                            trans.SetFailureHandlingOptions(options);
                            trans.Start();
                            List<FamilySymbol> openFSList = FindFS(doc);
                            foreach (OpeningInfo openingInfo in openingInfoList)
                            {
                                try
                                {
                                    foreach (CrushElemInfo crushElemInfo in openingInfo.crushElemInfos)
                                    {
                                        amount = PlaceOpening(doc, crushElemInfo, openFSList, amount);
                                    }
                                }
                                catch (Exception) { }
                            }
                            doc.Regenerate();
                            uidoc.RefreshActiveView();
                            trans.Commit();
                        }

                        using (Transaction trans = new Transaction(doc, "旋轉修改開口參數"))
                        {
                            FailureHandlingOptions options = trans.GetFailureHandlingOptions();
                            MyPreProcessor preproccessor = new MyPreProcessor();
                            options.SetClearAfterRollback(true);
                            options.SetFailuresPreprocessor(preproccessor);
                            trans.SetFailureHandlingOptions(options);
                            trans.Start();
                            RotateEditOpening(doc, openingInfoList);
                            doc.Regenerate();
                            uidoc.RefreshActiveView();
                            trans.Commit();
                        }

                        using (Transaction trans = new Transaction(doc, "計算底部高程"))
                        {
                            trans.Start();
                            EditBottomElevation(doc, combinePCodes);
                            doc.Regenerate();
                            uidoc.RefreshActiveView();
                            trans.Commit();
                        }

                        List<int> deleteIds = new List<int>();
                        using (Transaction trans = new Transaction(doc, "移除重疊開口"))
                        {
                            trans.Start();
                            foreach (int id in newOpeningIds)
                            {
                                try
                                {
                                    ElementId elemId = new ElementId(Convert.ToInt64(id.ToString()));
                                    FamilyInstance newOpening = doc.GetElement(elemId) as FamilyInstance;
                                    LocationPoint lp = newOpening.Location as LocationPoint;
                                    XYZ xyz = lp.Point;

                                    bool isDuplicate = openingXYZs.Any(p => p.IsAlmostEqualTo(xyz, 0.005));

                                    if (isDuplicate)
                                    {
                                        doc.Delete(elemId);
                                        amount--;
                                        deleteIds.Add(id);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    amount--;
                                    string error = ex.Message + "\n" + ex.ToString();
                                }
                            }
                            doc.Regenerate();
                            uidoc.RefreshActiveView();
                            trans.Commit();
                        }
                        tranGrp1.Assimilate();

                        DateTime timeEnd = DateTime.Now;
                        TimeSpan totalTime = timeEnd - timeStart;
                        foreach (int id in deleteIds)
                        {
                            newOpeningIds.Remove(id);
                        }
                        TaskDialog.Show("Revit", "耗時：" + totalTime.Minutes + " 分 " + totalTime.Seconds + " 秒 " + "\n\n共放置 " + amount + " 個開口。\n");
                    }
                }
                catch (Exception ex) { TaskDialog.Show("Revit", ex.Message + "\n" + ex.ToString()); }
            }

            return Result.Succeeded;
        }

        /// <summary>
        /// 【核心服務】：搜尋所有專案（含 Link）中名稱含 "落水頭" 的族群，並經由 MEP Connector 追蹤所有連結管道
        /// </summary>
        private HashSet<UniqueElementKey> CollectFloorDrainConnectedPipes(Document hostDoc, List<RevitLinkInstance> pipeDuctLinkDocs)
        {
            var connectedPipeKeys = new HashSet<UniqueElementKey>();

            // 1. 建立需要搜尋的所有 Document 清單 (含本機 Model 與連結 Models)
            var docsToSearch = new List<Document> { hostDoc };
            foreach (var link in pipeDuctLinkDocs)
            {
                var linkDoc = link.GetLinkDocument();
                if (linkDoc != null && !docsToSearch.Any(d => d.Title.Equals(linkDoc.Title)))
                {
                    docsToSearch.Add(linkDoc);
                }
            }

            // 2. 遍歷每個 Document 尋找落水頭並追蹤 Connectors
            foreach (var targetDoc in docsToSearch)
            {
                try
                {
                    // 收集該模型中所有 FamilyInstance (衛浴設備/機械設備)
                    IList<ElementFilter> drainCategories = new List<ElementFilter>
                    {
                        new ElementCategoryFilter(BuiltInCategory.OST_PlumbingFixtures),
                        new ElementCategoryFilter(BuiltInCategory.OST_MechanicalEquipment)
                    };
                    LogicalOrFilter drainFilter = new LogicalOrFilter(drainCategories);

                    var familyInstances = new FilteredElementCollector(targetDoc)
                        .WherePasses(drainFilter)
                        .WhereElementIsNotElementType()
                        .Cast<FamilyInstance>()
                        .ToList();

                    // 篩選出 Family.Name 或 Symbol.Name 包含 "落水頭" 的物件
                    var floorDrains = familyInstances.Where(fi =>
                        (fi.Symbol?.Family?.Name != null && fi.Symbol.Family.Name.Contains("落水頭")) ||
                        (fi.Symbol?.Name != null && fi.Symbol.Name.Contains("落水頭"))
                    ).ToList();

                    foreach (var drain in floorDrains)
                    {
                        // 讀取 MEP ConnectorManager
                        ConnectorManager connMgr = drain.MEPModel?.ConnectorManager;
                        if (connMgr == null) continue;

                        foreach (Connector conn in connMgr.Connectors)
                        {
                            if (conn == null || !conn.IsConnected) continue;

                            // 存取連結至此 Connector 的其他元件
                            foreach (Connector refConn in conn.AllRefs)
                            {
                                if (refConn == null || refConn.Owner == null) continue;

                                Element ownerElem = refConn.Owner;

                                // 若為管道 (Pipe, Duct, CableTray) 或管配件，加入白名單
                                if (ownerElem is Pipe || ownerElem is Duct || ownerElem is CableTray || ownerElem is FamilyInstance)
                                {
                                    connectedPipeKeys.Add(new UniqueElementKey(ownerElem));

                                    // 進一步追蹤第二階管線 (如落水頭 -> 垂直立管 -> 橫支管)
                                    TraverseNextConnectors(ownerElem, connectedPipeKeys);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 紀錄警示並持續處理其他 Document
                    string err = targetDoc.Title + ": " + ex.Message;
                }
            }

            return connectedPipeKeys;
        }

        /// <summary>
        /// 廣度搜尋（BFS）：沿著 MEP 管道 Connector 網路向外追蹤一階延伸管道
        /// </summary>
        private void TraverseNextConnectors(Element elem, HashSet<UniqueElementKey> keySet)
        {
            ConnectorManager mgr = null;
            if (elem is MEPCurve mepCurve)
            {
                mgr = mepCurve.ConnectorManager;
            }
            else if (elem is FamilyInstance fi && fi.MEPModel != null)
            {
                mgr = fi.MEPModel.ConnectorManager;
            }

            if (mgr == null) return;

            foreach (Connector conn in mgr.Connectors)
            {
                if (conn == null || !conn.IsConnected) continue;

                foreach (Connector refConn in conn.AllRefs)
                {
                    if (refConn == null || refConn.Owner == null) continue;

                    Element nextElem = refConn.Owner;
                    var key = new UniqueElementKey(nextElem);

                    if (!keySet.Contains(key) && (nextElem is Pipe || nextElem is Duct || nextElem is CableTray))
                    {
                        keySet.Add(key);
                    }
                }
            }
        }

        private static List<string> ParseFileNameTokens(Document doc)
        {
            if (doc == null) return new List<string>();
            string rawPath = !string.IsNullOrEmpty(doc.PathName) ? doc.PathName : doc.Title;
            string rawFileName = Path.GetFileNameWithoutExtension(rawPath);

            if (string.IsNullOrWhiteSpace(rawFileName)) return new List<string>();

            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char invalidChar in invalidChars)
            {
                rawFileName = rawFileName.Replace(invalidChar, ' ');
            }

            string[] tokens = Regex.Split(rawFileName.Trim(), @"[\-_.\s#]+");
            return tokens.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        }

        private Solid GetSymbolSolids(GeometryObject geomObj, RevitLinkInstance revitLink, Solid solid)
        {
            if (geomObj is Solid)
            {
                solid = (Solid)geomObj;
                Transform transform = revitLink.GetTotalTransform().Inverse;
                if (!transform.AlmostEqual(Transform.CreateTranslation(new XYZ(0, 0, 0))))
                {
                    solid = SolidUtils.CreateTransformed(solid, transform);
                }
            }
            if (geomObj is GeometryInstance)
            {
                GeometryElement geomElem = (geomObj as GeometryInstance).GetSymbolGeometry();
                foreach (GeometryObject o in geomElem)
                {
                    solid = GetSymbolSolids(o, revitLink, solid);
                    try { if (solid.SurfaceArea > 0) break; } catch (NullReferenceException) { }
                }
            }
            else if (geomObj is GeometryElement)
            {
                GeometryElement geomElem2 = (GeometryElement)geomObj;
                foreach (GeometryObject geomObj2 in geomElem2)
                {
                    solid = GetSymbolSolids(geomObj2, revitLink, solid);
                    if (solid.SurfaceArea > 0) break;
                }
            }
            return solid;
        }

        private void FindInputSolidBBElems(Document revitLinkDoc, Element wallOrBeam, Solid solid, List<RevitLinkInstance> pipeDuctLinkDocs, List<OpeningInfo> openingInfoList, List<PrjNameAndCode> prjNameAndCodes)
        {
            try
            {
                BoundingBoxXYZ bbox = solid.GetBoundingBox();
                XYZ solidCentroid = solid.ComputeCentroid();
                Transform transform = Transform.Identity;
                transform.Origin = solidCentroid;
                XYZ solidMin = transform.OfPoint(bbox.Min);
                XYZ solidMax = transform.OfPoint(bbox.Max);
                List<ElementTransform> elementTransformList = new List<ElementTransform>();
                List<Element> interferenceElems = new List<Element>();

                foreach (RevitLinkInstance pipeDuctLinkDoc in pipeDuctLinkDocs)
                {
                    ElementTransform elementTransform = new ElementTransform();
                    elementTransform.transform = pipeDuctLinkDoc.GetTotalTransform();
                    Transform linkTransform = pipeDuctLinkDoc.GetTotalTransform().Inverse;
                    XYZ linkSolidMin = linkTransform.OfPoint(solidMin);
                    XYZ linkSolidMax = linkTransform.OfPoint(solidMax);
                    Outline linkOutline = new Outline(linkSolidMin, linkSolidMax);
                    BoundingBoxIntersectsFilter linkBBFilter = new BoundingBoxIntersectsFilter(linkOutline);
                    IList<Element> bbElems = new FilteredElementCollector(pipeDuctLinkDoc.GetLinkDocument()).WherePasses(linkBBFilter).ToElements();
                    foreach (Element bbElem in bbElems)
                    {
                        if (bbElem is Pipe || bbElem is Duct || bbElem is CableTray || bbElem is FamilyInstance)
                        {
                            if (bbElem is FamilyInstance)
                            {
                                if (bbElem.Category.Name.Equals("管配件") || bbElem.Category.Name.Equals("管附件"))
                                {
                                    elementTransform.elements.Add(bbElem);
                                    interferenceElems.Add(bbElem);
                                }
                                else if (bbElem.Category.Name.Equals("風管附件"))
                                {
                                    FamilyInstance familyInstance = bbElem as FamilyInstance;
                                    string fsName = familyInstance.Symbol.Family.Name;
                                    if (fsName.Contains("防火風門") || fsName.Contains("防火風門 - 矩形") || fsName.Contains("電動風門 - 矩形") || fsName.Contains("隧道風門 - 矩形") || fsName.Contains("異徑順水三通"))
                                    {
                                        elementTransform.elements.Add(bbElem);
                                        interferenceElems.Add(bbElem);
                                    }
                                }
                                else if (bbElem.Category.Name.Equals("電纜架配件"))
                                {
                                    elementTransform.elements.Add(bbElem);
                                    interferenceElems.Add(bbElem);
                                }
                            }
                            else
                            {
                                elementTransform.elements.Add(bbElem);
                                interferenceElems.Add(bbElem);
                            }
                        }
                    }
                    if (elementTransform.elements.Count != 0)
                    {
                        elementTransformList.Add(elementTransform);
                    }
                }
                if (elementTransformList.Count != 0)
                {
                    foreach (ElementTransform elemTransform in elementTransformList)
                    {
                        SaveElemData(revitLinkDoc, wallOrBeam, solid, elemTransform.elements, elemTransform.transform, openingInfoList, prjNameAndCodes);
                    }
                }
            }
            catch (Exception ex) { string error = wallOrBeam.Id + "\n" + ex.Message + "\n" + ex.ToString(); }
        }

        private void SaveElemData(Document revitLinkDoc, Element wallOrBeam, Solid solid, List<Element> interferenceElems, Transform linkTransform, List<OpeningInfo> openingInfoList, List<PrjNameAndCode> prjNameAndCodes)
        {
            OpeningInfo openingInfo = new OpeningInfo();
            ElementId levelElemId = null;
            Parameter thicknessPara = null;
            if (wallOrBeam is Wall)
            {
                try
                {
                    openingInfo.type = "Wall";
                    levelElemId = wallOrBeam.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT).AsElementId();
                    openingInfo.length = wallOrBeam.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble();

                    List<WallType> wallTypelList = new FilteredElementCollector(revitLinkDoc).OfClass(typeof(WallType)).OfCategory(BuiltInCategory.OST_Walls).Cast<WallType>().ToList();
                    string wallName = wallOrBeam.Name;
                    Parameter wallTypePara = wallOrBeam.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM);
                    string wallTypeName = wallTypePara.AsValueString();
                    WallType wallType = (from x in wallTypelList
                                         where x.Name.Equals(wallName) && x.FamilyName.Equals(wallTypeName)
                                         select x).FirstOrDefault();
                    thicknessPara = wallType.get_Parameter(BuiltInParameter.WALL_ATTR_WIDTH_PARAM);
                }
                catch (Exception ex) { string error = wallOrBeam.Id + "\n" + levelElemId + "\n" + ex.Message; }
            }
            else if (wallOrBeam is BeamSystem || wallOrBeam is FamilyInstance)
            {
                try
                {
                    openingInfo.type = "Beam";
                    levelElemId = wallOrBeam.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM).AsElementId();
                    openingInfo.length = wallOrBeam.get_Parameter(BuiltInParameter.INSTANCE_LENGTH_PARAM).AsDouble();

                    List<FamilySymbol> familySymbolList = new FilteredElementCollector(revitLinkDoc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_StructuralFraming).Cast<FamilySymbol>().ToList();
                    string beamName = wallOrBeam.Name;
                    Parameter beamFamilyName = wallOrBeam.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM);
                    string beamFamily = beamFamilyName.AsValueString();
                    FamilySymbol beamFS = (from x in familySymbolList
                                           where x.Name.Equals(beamName) && x.FamilyName.Equals(beamFamily)
                                           select x).FirstOrDefault();
                    if (beamFS != null && !beamFS.IsActive)
                    {
                        beamFS.Activate();
                        revitLinkDoc.Regenerate();
                    }
                    thicknessPara = beamFS.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH) ?? beamFS.LookupParameter("b") ?? beamFS.LookupParameter("樑寬度");
                }
                catch (Exception ex) { string error = wallOrBeam.Id + "\n" + levelElemId + "\n" + ex.Message; }
            }
            else if (wallOrBeam is Floor)
            {
                try
                {
                    openingInfo.type = "Floor";
                    levelElemId = wallOrBeam.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM).AsElementId();
                    thicknessPara = wallOrBeam.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
                }
                catch (Exception ex) { string error = wallOrBeam.Id + "\n" + levelElemId + "\n" + ex.Message; }
            }

            string docTitle = wallOrBeam.Document.Title;
            try
            {
                PrjNameAndCode prjNameAndCode = prjNameAndCodes.Where(x => x.projectName.Contains(docTitle)).FirstOrDefault();
                if (prjNameAndCode != null)
                {
                    openingInfo.docName = prjNameAndCode.professionalCode;
                }
                else
                {
                    List<string> docTokens = ParseFileNameTokens(wallOrBeam.Document);
                    if (docTokens.Count > prjCode) openingInfo.docName = docTokens[prjCode];
                }
            }
            catch (Exception ex) { string error = ex.Message; }

            openingInfo.element = wallOrBeam;
            openingInfo.solid = solid;
            if (wallOrBeam is Floor)
            {
                openingInfo.beamWallAngle = 0;
            }
            else
            {
                try
                {
                    LocationCurve lc = wallOrBeam.Location as LocationCurve;
                    Line line = lc.Curve as Line;
                    openingInfo.beamWallAngle = PointRotation(line.Tessellate()[0], line.Tessellate()[1]);
                }
                catch (Exception) { openingInfo.beamWallAngle = 0; }
            }

            Level docLevel = null;
            try
            {
                Level level = revitLinkDoc.GetElement(levelElemId) as Level;
                docLevel = (from x in docLevels where x.Name.Contains(level.Name) select x).FirstOrDefault();
                openingInfo.level = docLevel;
            }
            catch (Exception) { }

            openingInfo.number = 0;
            foreach (Element interferenceElem in interferenceElems)
            {
                if (interferenceElem is Pipe || interferenceElem is Duct || interferenceElem is CableTray || interferenceElem is FamilyInstance)
                {
                    CrushElemInfo crushElemInfo = new CrushElemInfo();
                    docTitle = interferenceElem.Document.Title;
                    try
                    {
                        PrjNameAndCode prjNameAndCode = prjNameAndCodes.Where(x => x.projectName.Contains(docTitle)).FirstOrDefault();
                        if (prjNameAndCode != null)
                        {
                            crushElemInfo.docName = prjNameAndCode.professionalCode;
                        }
                        else
                        {
                            List<string> docTokens = ParseFileNameTokens(interferenceElem.Document);
                            crushElemInfo.docName = docTokens.Count > prjCode ? docTokens[prjCode] : interferenceElem.Document.Title;
                        }
                    }
                    catch (Exception ex) { string error = ex.Message; }

                    crushElemInfo.pipeOrDuct = interferenceElem;
                    crushElemInfo.hostType = openingInfo.type;

                    if (docLevel == null)
                    {
                        try
                        {
                            string levelName = interferenceElem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM).AsValueString();
                            docLevel = (from x in docLevels where x.Name.Contains(levelName) select x).FirstOrDefault();
                        }
                        catch (NullReferenceException)
                        {
                            string levelName = interferenceElem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM).AsValueString();
                            docLevel = (from x in docLevels where x.Name.Contains(levelName) select x).FirstOrDefault();
                        }
                        catch (Exception ex) { string error = ex.Message; }
                    }
                    crushElemInfo.level = docLevel;
                    crushElemInfo.number = 0;

                    if (interferenceElem is FamilyInstance)
                    {
                        if (interferenceElem.Category.Name.Equals("風管附件") || interferenceElem.Category.Name.Equals("電纜架配件"))
                        {
                            LocationPoint lp = interferenceElem.Location as LocationPoint;
                            Parameter diameterPara = null;
                            FamilyInstance familyInstance = interferenceElem as FamilyInstance;
                            string fsName = familyInstance.Symbol.Family.Name;

                            if (interferenceElem.Category.Name.Equals("管配件") || interferenceElem.Category.Name.Equals("管附件"))
                            {
                                crushElemInfo.type = "PipeFitting";
                                try
                                {
                                    bool isInsulation = false;
                                    double size = 0.0;
                                    diameterPara = interferenceElem.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
                                    if (diameterPara != null)
                                    {
                                        crushElemInfo.pipeType = diameterPara.AsValueString();
                                        double outerDiameter = interferenceElem.get_Parameter(BuiltInParameter.RBS_PIPE_SIZE_MAXIMUM).AsDouble();
                                        crushElemInfo.size = outerDiameter;
                                        double insulationThickness = interferenceElem.get_Parameter(BuiltInParameter.RBS_REFERENCE_INSULATION_THICKNESS).AsDouble();
                                        crushElemInfo.insulationThickness = insulationThickness;
                                        if (insulationThickness > 0) isInsulation = true;
                                        outerDiameter = outerDiameter * unit_conversion;
                                        size = outerDiameter;
                                        outerDiameter = SinoOpenSize(isInsulation, outerDiameter);
                                        crushElemInfo.diameter = outerDiameter / unit_conversion;
                                    }
                                    else
                                    {
                                        diameterPara = interferenceElem.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                                        crushElemInfo.size = diameterPara.AsDouble();
                                        double diameterSize = diameterPara.AsDouble() / unit_conversion;
                                        size = diameterSize;
                                        diameterSize = SinoOpenSize(isInsulation, diameterSize);
                                        crushElemInfo.diameter = diameterSize / unit_conversion;
                                    }
                                    crushElemInfo.thickness = thicknessPara != null ? thicknessPara.AsDouble() : 100 / unit_conversion;

                                    if (!(isInsulation == false && size < 50))
                                    {
                                        if (crushElemInfo.size != 0 && crushElemInfo.thickness != 0)
                                        {
                                            FindSolidIntersection(interferenceElem, solid, openingInfo, crushElemInfo, linkTransform);
                                        }
                                    }
                                }
                                catch (Exception) { }

                                if (crushElemInfo.ductHeight != 0 && crushElemInfo.ductWight != 0 && crushElemInfo.thickness != 0)
                                {
                                    FindSolidIntersection(interferenceElem, solid, openingInfo, crushElemInfo, linkTransform);
                                }
                            }
                            else if (interferenceElem.Category.Name.Equals("風管附件"))
                            {
                                crushElemInfo.type = "DuctAccessory";
                                if (fsName.Contains("防火風門") || fsName.Contains("防火風門 - 矩形") || fsName.Contains("電動風門 - 矩形"))
                                {
                                    try
                                    {
                                        crushElemInfo.ductHeight = GetFirstParameterValue(interferenceElem, "風管高度");
                                        crushElemInfo.ductWight = GetFirstParameterValue(interferenceElem, "風管寬度");
                                        crushElemInfo.thickness = thicknessPara != null ? thicknessPara.AsDouble() : GetFirstParameterValue(interferenceElem, "風門長度");
                                    }
                                    catch (Exception) { }

                                    if (crushElemInfo.ductHeight != 0 && crushElemInfo.ductWight != 0 && crushElemInfo.thickness != 0)
                                    {
                                        FindSolidIntersection(interferenceElem, solid, openingInfo, crushElemInfo, linkTransform);
                                    }
                                }
                                else if (fsName.Contains("異徑順水三通"))
                                {
                                    try
                                    {
                                        diameterPara = interferenceElem.LookupParameter("最大尺寸");
                                        string diameter = diameterPara.AsValueString().Replace(" mm", "");
                                        double diameterSize = Convert.ToDouble(diameter);
                                        crushElemInfo.thickness = diameterSize / unit_conversion;
                                        crushElemInfo.ductWight = diameterSize / unit_conversion;
                                        if (thicknessPara != null) crushElemInfo.thickness = thicknessPara.AsDouble();
                                        else crushElemInfo.ductHeight = diameterSize / unit_conversion;
                                    }
                                    catch (Exception) { }
                                }
                            }
                            else if (interferenceElem.Category.Name.Equals("電纜架配件"))
                            {
                                crushElemInfo.type = "CableTrayFitting";
                                try
                                {
                                    double heightValue = GetFirstParameterValue(interferenceElem, "托盤高度");
                                    crushElemInfo.ductHeight = heightValue > 0 ? heightValue + (50.0 / unit_conversion) : 0.0;
                                    crushElemInfo.ductWight = GetFirstParameterValue(interferenceElem, "托盤寬度");
                                    crushElemInfo.thickness = GetFirstParameterValue(interferenceElem, "長度");
                                }
                                catch (Exception) { }

                                if (crushElemInfo.ductHeight != 0 && crushElemInfo.ductWight != 0 && crushElemInfo.thickness != 0)
                                {
                                    FindSolidIntersection(interferenceElem, solid, openingInfo, crushElemInfo, linkTransform);
                                }
                            }
                        }
                    }
                    else
                    {
                        Curve pipeCurve = (interferenceElem.Location as LocationCurve).Curve.CreateTransformed(linkTransform);
                        bool isInsulation = false;
                        double size = 0.0;
                        if (interferenceElem is Pipe)
                        {
                            crushElemInfo.type = "Pipe";
                            Parameter diameterPara = interferenceElem.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
                            if (diameterPara != null)
                            {
                                crushElemInfo.pipeType = diameterPara.AsValueString();
                                double outerDiameter = interferenceElem.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();
                                crushElemInfo.size = outerDiameter;
                                double insulationThickness = interferenceElem.get_Parameter(BuiltInParameter.RBS_REFERENCE_INSULATION_THICKNESS).AsDouble();
                                crushElemInfo.insulationThickness = insulationThickness;
                                if (insulationThickness > 0) isInsulation = true;
                                outerDiameter = outerDiameter * unit_conversion;
                                size = outerDiameter;
                                outerDiameter = SinoOpenSize(isInsulation, outerDiameter);
                                crushElemInfo.diameter = outerDiameter / unit_conversion;
                            }
                            else
                            {
                                diameterPara = interferenceElem.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                                crushElemInfo.size = diameterPara.AsDouble();
                                string[] diameter = diameterPara.AsValueString().Split(new char[] { ' ' });
                                double diameterSize = Convert.ToDouble(diameter[0]);
                                size = diameterSize;
                                diameterSize = SinoOpenSize(isInsulation, diameterSize);
                                crushElemInfo.diameter = diameterSize / unit_conversion;
                            }
                        }
                        else if (interferenceElem is Duct)
                        {
                            crushElemInfo.type = "Duct";
                            double height = interferenceElem.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM).AsDouble();
                            crushElemInfo.ductHeight = height;
                            double width = interferenceElem.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM).AsDouble();
                            crushElemInfo.ductWight = width;
                            size = width * unit_conversion;
                        }
                        else if (interferenceElem is CableTray)
                        {
                            crushElemInfo.type = "CableTray";
                            Parameter diameterPara = interferenceElem.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);
                            crushElemInfo.ductHeight = diameterPara.AsDouble() + 50 / unit_conversion;
                            diameterPara = interferenceElem.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);
                            size = diameterPara.AsDouble() * unit_conversion;
                            crushElemInfo.ductWight = diameterPara.AsDouble();
                        }

                        crushElemInfo.thickness = thicknessPara != null ? thicknessPara.AsDouble() : 1;

                        if (!(isInsulation == false && size < 50))
                        {
                            FindFaceIntersectLine(solid, pipeCurve, openingInfo, crushElemInfo, linkTransform);
                        }
                    }
                }
            }
            if (openingInfo.crushElemInfos.Count > 0)
            {
                openingInfoList.Add(openingInfo);
            }
        }

        private double GetFirstParameterValue(Element elem, string keyword)
        {
            List<Parameter> matchingParams = elem.Parameters
                .Cast<Parameter>()
                .Where(p => p.Definition != null && p.Definition.Name.Contains(keyword))
                .ToList();

            Parameter firstValidParam = matchingParams.FirstOrDefault(p => p.HasValue);
            return firstValidParam != null ? firstValidParam.AsDouble() : 0.0;
        }

        private void FindFaceIntersectLine(Solid solid, Curve curve, OpeningInfo openingInfo, CrushElemInfo crushElemInfo, Transform linkTransform)
        {
            XYZ startPoint = new XYZ();
            XYZ endPoint = new XYZ();
            int i = 1;
            foreach (Face face in solid.Faces)
            {
                IntersectionResultArray intersectionR = new IntersectionResultArray();
                SetComparisonResult comparisonR = face.Intersect(curve, out intersectionR);
                XYZ intersectionResult = null;

                if (SetComparisonResult.Disjoint != comparisonR)
                {
                    try
                    {
                        if (intersectionR != null && !intersectionR.IsEmpty)
                        {
                            int mod = i % 2;
                            crushElemInfo.insfaces.Add(face);
                            intersectionResult = new XYZ((intersectionR.get_Item(0).XYZPoint.X), (intersectionR.get_Item(0).XYZPoint.Y), (intersectionR.get_Item(0).XYZPoint.Z) + elevationOffset);
                            crushElemInfo.insXYZs.Add(intersectionResult);

                            if (mod == 1)
                            {
                                startPoint = intersectionResult;
                            }
                            else if (mod == 0)
                            {
                                endPoint = intersectionResult;
                                XYZ insXYZ = new XYZ((startPoint.X + endPoint.X) / 2, (startPoint.Y + endPoint.Y) / 2, (startPoint.Z + endPoint.Z) / 2);

                                if (openingInfo.element is Floor)
                                {
                                    crushElemInfo.xyzs.Add(endPoint);
                                    double z = endPoint.Z;
                                    double elevation = crushElemInfo.level.get_Parameter(BuiltInParameter.LEVEL_ELEV).AsDouble();
                                    crushElemInfo.deviation = z - elevation;
                                }
                                else
                                {
                                    crushElemInfo.xyzs.Add(insXYZ);
                                    double z = insXYZ.Z;
                                    if (crushElemInfo.level != null)
                                    {
                                        double elevation = crushElemInfo.level.get_Parameter(BuiltInParameter.LEVEL_ELEV).AsDouble();
                                        crushElemInfo.deviation = z - elevation;
                                    }
                                }
                                crushElemInfo.axis = Line.CreateBound(insXYZ, new XYZ(insXYZ.X, insXYZ.Y, insXYZ.Z + 10));
                                crushElemInfo.pipeAngle = PointRotation(startPoint, endPoint);

                                if (crushElemInfo.pipeOrDuct != null && openingInfo.element != null)
                                {
                                    crushElemInfo.comment = $"{crushElemInfo.docName}_{crushElemInfo.pipeOrDuct.Id}_{openingInfo.docName}_{openingInfo.element.Id}";
                                }

                                openingInfo.crushElemInfos.Add(crushElemInfo);
                            }
                            i++;
                        }
                    }
                    catch (NullReferenceException) { }
                }
            }
        }

        private void FindSolidIntersection(Element interferenceElem, Solid solid, OpeningInfo openingInfo, CrushElemInfo crushElemInfo, Transform transform)
        {
            ICollection<ElementId> interferenceElems = new List<ElementId> { interferenceElem.Id };
            if (!transform.AlmostEqual(Transform.CreateTranslation(new XYZ(0, 0, 0))))
            {
                solid = SolidUtils.CreateTransformed(solid, transform.Inverse);
            }
            IList<Element> elems = new FilteredElementCollector(interferenceElem.Document, interferenceElems).WherePasses(new ElementIntersectsSolidFilter(solid)).WhereElementIsNotElementType().ToList();
            foreach (Element elem in elems)
            {
                try
                {
                    LocationPoint lp = elem.Location as LocationPoint;
                    XYZ insXYZ = new XYZ();
                    if (lp != null)
                    {
                        insXYZ = new XYZ((lp.Point.X + transform.Origin.X), (lp.Point.Y + transform.Origin.Y), (lp.Point.Z + transform.Origin.Z) + elevationOffset);
                    }
                    else
                    {
                        LocationCurve lc = elem.Location as LocationCurve;
                        XYZ lp1 = lc.Curve.Tessellate()[0];
                        XYZ lp2 = lc.Curve.Tessellate()[1];
                        insXYZ = new XYZ((lp1.X + lp2.X) / 2 + transform.Origin.X, (lp1.Y + lp2.Y) / 2 + transform.Origin.Y, (lp1.Z + lp2.Z) / 2 + transform.Origin.Z + elevationOffset);
                    }

                    double z = insXYZ.Z;
                    if (openingInfo.element is Floor)
                    {
                        crushElemInfo.xyzs.Add(insXYZ);
                        double elevation = crushElemInfo.level.get_Parameter(BuiltInParameter.LEVEL_ELEV).AsDouble();
                        crushElemInfo.deviation = z - elevation;
                    }
                    else
                    {
                        try
                        {
                            LocationCurve lc = openingInfo.element.Location as LocationCurve;
                            Line line = lc.Curve as Line;
                            line.MakeUnbound();
                            insXYZ = line.Project(insXYZ).XYZPoint;
                            crushElemInfo.xyzs.Add(insXYZ);
                        }
                        catch (Exception) { }

                        if (crushElemInfo.level != null)
                        {
                            double elevation = crushElemInfo.level.get_Parameter(BuiltInParameter.LEVEL_ELEV).AsDouble();
                            crushElemInfo.deviation = z - elevation;
                        }
                    }
                    crushElemInfo.axis = Line.CreateBound(insXYZ, new XYZ(insXYZ.X, insXYZ.Y, insXYZ.Z + 10));
                    crushElemInfo.pipeAngle = openingInfo.beamWallAngle - 90;

                    if (crushElemInfo.xyzs.Count > 0)
                    {
                        crushElemInfo.comment = $"{crushElemInfo.docName}_{interferenceElem.Id}_{openingInfo.docName}_{openingInfo.element.Id}";
                        openingInfo.crushElemInfos.Add(crushElemInfo);
                    }
                }
                catch (Exception) { }
            }
        }

        private List<FamilySymbol> FindFS(Document doc)
        {
            IList<FamilySymbol> familySymbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
            List<FamilySymbol> openFSList = (from x in familySymbols
                                             where x.FamilyName.Equals("矩形風管樓版開口") || x.FamilyName.Equals("矩形風管牆開口") || x.FamilyName.Equals("圓形水管樓版開口") ||
                                                   x.FamilyName.Equals("圓形水管牆開口") || x.FamilyName.Equals("電纜架樓版開口") || x.FamilyName.Equals("電纜架牆開口")
                                             select x).ToList();

            foreach (FamilySymbol openFS in openFSList)
            {
                if (openFS != null && !openFS.IsActive)
                {
                    openFS.Activate();
                    doc.Regenerate();
                }
            }
            return openFSList;
        }

        private static double OpenSize(double radius)
        {
            double[] openSize = new double[] { 13, 16, 20, 27, 35, 40, 50, 65, 80, 90, 100, 125, 150, 200, 250, 300, 350, 400, 450, 500, 600 };
            for (int i = 0; i < openSize.Length; i++)
            {
                try
                {
                    if (radius <= openSize[i]) { radius = openSize[i + 1]; break; }
                    else if (radius > openSize[openSize.Length - 2]) { radius = openSize[openSize.Length - 1]; break; }
                }
                catch (Exception) { }
            }
            return radius;
        }

        private static double SinoOpenSize(bool isInsulation, double radius)
        {
            if (isInsulation)
            {
                if (radius < 15) radius = 80;
                else if (radius >= 15 && radius <= 32) radius = 100;
                else if (radius > 32 && radius <= 80) radius = 150;
                else if (radius > 80 && radius <= 125) radius = 200;
                else if (radius > 125 && radius <= 150) radius = 250;
                else if (radius > 150 && radius <= 200) radius = 300;
                else radius = 500;
            }
            else
            {
                if (radius < 15) radius = 40;
                else if (radius >= 15 && radius < 32) radius = 50;
                else if (radius >= 32 && radius <= 50) radius = 80;
                else if (radius > 50 && radius <= 65) radius = 100;
                else if (radius > 65 && radius <= 80) radius = 125;
                else if (radius > 80 && radius <= 125) radius = 150;
                else if (radius > 125 && radius <= 150) radius = 200;
                else if (radius > 150 && radius <= 200) radius = 250;
                else radius = 300;
            }
            return radius;
        }

        private int PlaceOpening(Document doc, CrushElemInfo crushElemInfo, List<FamilySymbol> openFSList, int amount)
        {
            string useFS = string.Empty;
            if (crushElemInfo.hostType.Equals("Wall") || crushElemInfo.hostType.Equals("Beam"))
            {
                if (crushElemInfo.type.Equals("Pipe") || crushElemInfo.type.Equals("PipeFitting")) { useFS = "圓形水管牆開口"; }
                else if (crushElemInfo.type.Equals("Duct") || crushElemInfo.type.Equals("DuctAccessory")) { useFS = "矩形風管牆開口"; }
                else if (crushElemInfo.type.Equals("CableTray") || crushElemInfo.type.Equals("CableTrayFitting")) { useFS = "電纜架牆開口"; }
            }
            else if (crushElemInfo.hostType.Equals("Floor"))
            {
                if (crushElemInfo.type.Equals("Pipe") || crushElemInfo.type.Equals("PipeFitting")) { useFS = "圓形水管樓版開口"; }
                else if (crushElemInfo.type.Equals("Duct") || crushElemInfo.type.Equals("DuctAccessory")) { useFS = "矩形風管樓版開口"; }
                else if (crushElemInfo.type.Equals("CableTray") || crushElemInfo.type.Equals("CableTrayFitting")) { useFS = "電纜架樓版開口"; }
            }
            crushElemInfo.useFS = useFS;
            FamilySymbol openFS = openFSList.Where(x => x.FamilyName.Equals(useFS)).FirstOrDefault();

            foreach (XYZ xyz in crushElemInfo.xyzs)
            {
                try
                {
                    bool isDuplicate = openingXYZs.Any(p => p.IsAlmostEqualTo(xyz, 0.005));

                    if (!isDuplicate)
                    {
                        FamilyInstance pipeOpen = doc.Create.NewFamilyInstance(xyz, openFS, crushElemInfo.level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                        crushElemInfo.pipeOpens.Add(pipeOpen);
                        newOpeningIds.Add((int)pipeOpen.Id.Value);
                        amount++;
                    }
                }
                catch (Exception ex) { string str = ex.Message; }
            }
            return amount;
        }

        private void RotateEditOpening(Document doc, List<OpeningInfo> openingInfoList)
        {
            foreach (OpeningInfo openingInfo in openingInfoList)
            {
                foreach (CrushElemInfo crushElemInfo in openingInfo.crushElemInfos)
                {
                    foreach (Element pipeOpen in crushElemInfo.pipeOpens)
                    {
                        try
                        {
                            Parameter editPara = null;
                            if (crushElemInfo.useFS.Equals("圓形水管牆開口"))
                            {
                                editPara = pipeOpen.LookupParameter("水管直徑"); editPara?.Set(crushElemInfo.size);
                                editPara = pipeOpen.LookupParameter("指定圓形套管直徑"); editPara?.Set(crushElemInfo.diameter);
                                editPara = pipeOpen.LookupParameter("牆厚度"); editPara?.Set(crushElemInfo.thickness);
                                editPara = pipeOpen.LookupParameter("圓形牆開口流水號"); editPara?.Set(crushElemInfo.number);
                                if (crushElemInfo.axis != null) ElementTransformUtils.RotateElement(doc, pipeOpen.Id, crushElemInfo.axis, crushElemInfo.pipeAngle * Math.PI / 180);
                            }
                            else if (crushElemInfo.useFS.Equals("圓形水管樓版開口"))
                            {
                                editPara = pipeOpen.LookupParameter("水管直徑"); editPara?.Set(crushElemInfo.size);
                                editPara = pipeOpen.LookupParameter("指定圓形套管直徑"); editPara?.Set(crushElemInfo.diameter);
                                editPara = pipeOpen.LookupParameter("樓版厚度"); editPara?.Set(crushElemInfo.thickness);
                                editPara = pipeOpen.LookupParameter("圓形牆開口流水號"); editPara?.Set(crushElemInfo.number);
                            }
                            else if (crushElemInfo.useFS.Equals("矩形風管牆開口"))
                            {
                                editPara = pipeOpen.LookupParameter("風管高度"); editPara?.Set(crushElemInfo.ductHeight);
                                editPara = pipeOpen.LookupParameter("風管寬度"); editPara?.Set(crushElemInfo.ductWight);
                                editPara = pipeOpen.LookupParameter("牆厚度"); editPara?.Set(crushElemInfo.thickness);
                                editPara = pipeOpen.LookupParameter("矩形牆開口流水號"); editPara?.Set(crushElemInfo.number);
                                if (crushElemInfo.axis != null) ElementTransformUtils.RotateElement(doc, pipeOpen.Id, crushElemInfo.axis, crushElemInfo.pipeAngle * Math.PI / 180);
                            }
                            else if (crushElemInfo.useFS.Equals("矩形風管樓版開口"))
                            {
                                editPara = pipeOpen.LookupParameter("風管高度"); editPara?.Set(crushElemInfo.ductHeight);
                                editPara = pipeOpen.LookupParameter("風管寬度"); editPara?.Set(crushElemInfo.ductWight);
                                editPara = pipeOpen.LookupParameter("牆厚度"); editPara?.Set(crushElemInfo.thickness);
                                editPara = pipeOpen.LookupParameter("矩形牆開口流水號"); editPara?.Set(crushElemInfo.number);
                            }
                            else if (crushElemInfo.useFS.Equals("電纜架牆開口"))
                            {
                                editPara = pipeOpen.LookupParameter("電纜架高度"); if (editPara != null && !editPara.IsReadOnly) editPara.Set(crushElemInfo.ductHeight);
                                editPara = pipeOpen.LookupParameter("電纜架寬度"); if (editPara != null && !editPara.IsReadOnly) editPara.Set(crushElemInfo.ductWight);
                                editPara = pipeOpen.LookupParameter("牆厚度"); if (editPara != null && !editPara.IsReadOnly) editPara.Set(crushElemInfo.thickness);
                                editPara = pipeOpen.LookupParameter("矩形牆開口流水號"); if (editPara != null && !editPara.IsReadOnly) editPara.Set(crushElemInfo.number);

                                if (crushElemInfo.axis != null)
                                {
                                    ElementTransformUtils.RotateElement(doc, pipeOpen.Id, crushElemInfo.axis, crushElemInfo.pipeAngle * Math.PI / 180);
                                }
                            }
                            else if (crushElemInfo.useFS.Equals("電纜架樓版開口"))
                            {
                                editPara = pipeOpen.LookupParameter("電纜架高度"); if (editPara != null && !editPara.IsReadOnly) editPara.Set(crushElemInfo.ductHeight);
                                editPara = pipeOpen.LookupParameter("電纜架寬度"); if (editPara != null && !editPara.IsReadOnly) editPara.Set(crushElemInfo.ductWight);
                                editPara = pipeOpen.LookupParameter("版厚度"); if (editPara != null && !editPara.IsReadOnly) editPara.Set(crushElemInfo.thickness);
                                editPara = pipeOpen.LookupParameter("矩形牆開口流水號"); if (editPara != null && !editPara.IsReadOnly) editPara.Set(crushElemInfo.number);
                            }

                            editPara = pipeOpen.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                            if (editPara != null && !editPara.IsReadOnly)
                            {
                                editPara.Set(crushElemInfo.deviation);
                            }

                            editPara = pipeOpen.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            if (editPara != null && !editPara.IsReadOnly)
                            {
                                string finalComment = crushElemInfo.comment;

                                if (string.IsNullOrEmpty(finalComment))
                                {
                                    string pipeIdStr = crushElemInfo.pipeOrDuct != null ? crushElemInfo.pipeOrDuct.Id.ToString() : "0";
                                    string hostIdStr = openingInfo.element != null ? openingInfo.element.Id.ToString() : "0";
                                    finalComment = $"{crushElemInfo.docName}_{pipeIdStr}_{openingInfo.docName}_{hostIdStr}";
                                }

                                editPara.Set(finalComment);
                            }

                            if (pipeOpen.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM) is Parameter levelPara)
                            {
                                string floorName = levelPara.AsValueString();
                                editPara = pipeOpen.LookupParameter("位置");
                                if (editPara != null && !editPara.IsReadOnly)
                                {
                                    editPara.Set(floorName);
                                }
                            }
                        }
                        catch (Exception ex) { string str = ex.Message; }
                    }
                }
            }
        }

        private void EditBottomElevation(Document doc, List<ProfessionalCode> combinePCodes)
        {
            IList<ElementFilter> pipeDuctFilters = new List<ElementFilter>();
            ElementCategoryFilter pipeFilter = new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory);
            ElementCategoryFilter ductFilter = new ElementCategoryFilter(BuiltInCategory.OST_DuctAccessory);
            ElementCategoryFilter cableTrayFilter = new ElementCategoryFilter(BuiltInCategory.OST_CableTrayFitting);
            pipeDuctFilters.Add(pipeFilter);
            pipeDuctFilters.Add(ductFilter);
            pipeDuctFilters.Add(cableTrayFilter);
            LogicalOrFilter pipeOrDuctFilter = new LogicalOrFilter(pipeDuctFilters);
            List<FamilyInstance> openings = new FilteredElementCollector(doc).WherePasses(pipeOrDuctFilter).WhereElementIsNotElementType().Cast<FamilyInstance>().ToList();
            if (startOpenings.Count > 0)
            {
                openings = new FilteredElementCollector(doc).WherePasses(pipeOrDuctFilter).Excluding(startOpenings).WhereElementIsNotElementType().Cast<FamilyInstance>().ToList();
            }

            foreach (FamilyInstance opening in openings)
            {
                try
                {
                    double offset = 0.0;
                    try { offset = opening.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM).AsDouble(); }
                    catch (Exception) { offset = opening.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM).AsDouble(); }
                    Parameter para = null;

                    if (opening.Name.Equals("圓形水管牆開口"))
                    {
                        double height = Convert.ToDouble(opening.LookupParameter("指定圓形套管直徑").AsDouble());
                        double sub = (offset - (height / 2)) * unit_conversion;
                        string value = Math.Round(sub, 2, MidpointRounding.AwayFromZero).ToString();
                        para = opening.LookupParameter("圓形套管底部高程");
                        para?.Set(value);
                    }
                    else if (opening.Name.Equals("矩形風管牆開口") || opening.Name.Equals("電纜架牆開口"))
                    {
                        double height = Convert.ToDouble(opening.LookupParameter("矩形開口高度").AsDouble());
                        double sub = (offset - (height / 2)) * unit_conversion;
                        string value = Math.Round(sub, 2, MidpointRounding.AwayFromZero).ToString();
                        para = opening.LookupParameter("矩形開口底部高程");
                        para?.Set(value);
                    }
                    else if (opening.Name.Contains("樓版開口"))
                    {
                        string value = "0";
                        para = opening.LookupParameter("矩形開口底部高程") ?? opening.LookupParameter("圓形套管底部高程");
                        para?.Set(value);
                    }

                    para = opening.LookupParameter("專業代碼");
                    string comment = opening.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
                    if (!string.IsNullOrEmpty(comment))
                    {
                        try
                        {
                            string pipeCode = comment.Split('_')[0];
                            ProfessionalCode combinePCode = combinePCodes.Where(x => x.comments.Any(y => pipeCode.Contains(y))).FirstOrDefault();
                            if (combinePCode != null)
                            {
                                para?.Set(combinePCode.professionalCode);
                            }
                        }
                        catch (Exception ex) { string error = ex.Message; }
                    }
                }
                catch (Exception ex) { string error = ex.Message; }
            }
        }

        private Solid GetSolids(GeometryObject geomObj, Solid solid)
        {
            if (geomObj is Solid) solid = (Solid)geomObj;
            if (geomObj is GeometryInstance)
            {
                GeometryElement geomElem = (geomObj as GeometryInstance).GetSymbolGeometry();
                foreach (GeometryObject o in geomElem)
                {
                    solid = GetSolids(o, solid);
                    if (solid.SurfaceArea > 0) break;
                }
            }
            else if (geomObj is GeometryElement)
            {
                GeometryElement geomElem2 = (GeometryElement)geomObj;
                foreach (GeometryObject geomObj2 in geomElem2)
                {
                    solid = GetSolids(geomObj2, solid);
                    if (solid.SurfaceArea > 0) break;
                }
            }
            return solid;
        }

        public static double PointRotation(XYZ pointA, XYZ pointB)
        {
            XYZ pA = new XYZ(pointA.X, pointA.Y, 0);
            XYZ pB = new XYZ(pointB.X, pointB.Y, 0);
            double Dx = pB.X - pA.X;
            double Dy = pB.Y - pA.Y;
            return Math.Atan2(Dy, Dx) / Math.PI * 180;
        }

        public class MyPreProcessor : IFailuresPreprocessor
        {
            FailureProcessingResult IFailuresPreprocessor.PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                String transactionName = failuresAccessor.GetTransactionName();
                IList<FailureMessageAccessor> fmas = failuresAccessor.GetFailureMessages();
                if (fmas.Count == 0) { return FailureProcessingResult.Continue; }
                if (transactionName.Equals("放置開口") || transactionName.Equals("旋轉修改開口參數"))
                {
                    failuresAccessor.DeleteAllWarnings();
                }
                return FailureProcessingResult.Continue;
            }
        }
    }
}