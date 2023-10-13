using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CSDSEM
{
    [Transaction(TransactionMode.Manual)]
    public class LinkOpening : IExternalCommand
    {
        // 使用外掛前, 現有的Opening數量, 排除更新參數
        private List<ElementId> startOpenings = new List<ElementId>();
        // 所有的原開口的座標點
        private List<XYZ> openingXYZs = new List<XYZ>();
        // 新增的開口Id
        private List<int> newOpeningIds = new List<int>();
        // 樑牆板資訊
        private class OpeningInfo
        {
            public string docName = string.Empty; // 來自於哪個專案
            public Element element { get; set; } // 收集樑牆資料
            public string type { get; set; } // 品類
            public double length { get; set; } // 長度
            public double width { get; set; } // 寬度
            public double height { get; set; } // 高度
            public double thickness { get; set; } // 厚度
            public double number { get; set; } // 編號
            public double beamWallAngle { get; set; } // 樑牆旋轉的角度
            public Solid solid = null; // 樑牆Solid
            public Level level { get; set; } // 樓層
            public List<CrushElemInfo> crushElemInfos = new List<CrushElemInfo>(); // 與該樑牆干涉管道的開口資訊
        }
        // 干涉管資訊
        private class CrushElemInfo
        {
            public string docName = string.Empty; // 來自於哪個專案
            public Element pipeOrDuct = null; // BoundingBox, 干涉的管與風管
            public Solid pipeOrDuctSolid = null; // 干涉的管與風管Solid
            public string type = string.Empty; // 品類
            public string pipeType = string.Empty; // 系統類型
            public double insulationThickness { get; set; } // 絕緣體厚度
            public string hostType = string.Empty; // 干涉的主體品類
            public double size { get; set; } // 管直徑
            public double diameter { get; set; } // 管直徑
            public double ductWight { get; set; } // 風管寬度
            public double ductHeight { get; set; } // 風管高度
            public double bottomElevation { get; set; } // 底部高程
            public double thickness { get; set; } // 開口厚度
            public Level level { get; set; } // 參考樓層
            public List<Face> insfaces = new List<Face>(); // 接觸到的兩個面
            public List<XYZ> insXYZs = new List<XYZ>(); // 接觸到的兩個面的交集點
            public List<XYZ> xyzs = new List<XYZ>(); // 元件擺放點
            public Line axis { get; set; } // 軸心
            public double pipeAngle { get; set; } // 管角度
            public List<Element> pipeOpens = new List<Element>(); // 儲存所有新增開口
            public string useFS = string.Empty; // 使用的族群
            public double deviation { get; set; } // 偏移
            public double number { get; set; } // 編號
        }
        // Link Model 座標轉換
        private class ElementTransform
        {
            public List<Element> elements = new List<Element>();
            public Transform transform { get; set; }
        }
        private static List<Level> docLevels = new List<Level>(); // Document內所有的Level
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
            Document doc = uidoc.Document;

            DateTime timeStart = DateTime.Now; // 計時開始 取得目前時間

            // 收集現有所有開口
            IList<ElementFilter> startOpeningFilters = new List<ElementFilter>(); // 清空過濾器  
            ElementCategoryFilter pipeOpenFilter = new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory); // 管道開口
            ElementCategoryFilter ductOpenFilter = new ElementCategoryFilter(BuiltInCategory.OST_DuctAccessory); // 風管開口
            ElementCategoryFilter cableTrayOpenFilter = new ElementCategoryFilter(BuiltInCategory.OST_CableTrayFitting); // 電纜架開口
            startOpeningFilters.Add(pipeOpenFilter);
            startOpeningFilters.Add(ductOpenFilter);
            startOpeningFilters.Add(cableTrayOpenFilter);
            LogicalOrFilter PDCFilter = new LogicalOrFilter(startOpeningFilters);
            startOpenings = new FilteredElementCollector(doc).WherePasses(PDCFilter).WhereElementIsNotElementType().ToElementIds().ToList();
            
            // 所有的原開口的座標點
            foreach(ElementId startOpening in startOpenings)
            {
                FamilyInstance opening = doc.GetElement(startOpening) as FamilyInstance;
                LocationPoint lp = opening.Location as LocationPoint;
                openingXYZs.Add(lp.Point);
            }

            // 讀取所有Doucment的Level
            docLevels = new FilteredElementCollector(doc).OfClass(typeof(Level)).WhereElementIsNotElementType().Cast<Level>().ToList();
            // 儲存擁有管道與風管的RevitLink
            List<RevitLinkInstance> pipeDuctLinkDocs = new List<RevitLinkInstance>();
            // 儲存專案與Link的管、風管，Link的Element儲存轉換座標的Solid
            IList<ElementFilter> pipeDuctFilters = new List<ElementFilter>(); // 清空過濾器  
            ElementCategoryFilter pipeFilter = new ElementCategoryFilter(BuiltInCategory.OST_PipeCurves); // 管道
            ElementCategoryFilter pipeFittingFilter = new ElementCategoryFilter(BuiltInCategory.OST_PipeFitting); // 管道附件
            ElementCategoryFilter ductFilter = new ElementCategoryFilter(BuiltInCategory.OST_DuctCurves); // 風管
            ElementCategoryFilter ductAccessoryFilter = new ElementCategoryFilter(BuiltInCategory.OST_DuctAccessory); // 風管附件
            ElementCategoryFilter cableTrayFilter = new ElementCategoryFilter(BuiltInCategory.OST_CableTray); // 電纜架
            ElementCategoryFilter cableTrayFittingFilter = new ElementCategoryFilter(BuiltInCategory.OST_CableTrayFitting); // 電纜架附件
            pipeDuctFilters.Add(pipeFilter);
            pipeDuctFilters.Add(pipeFittingFilter);
            pipeDuctFilters.Add(ductFilter);
            pipeDuctFilters.Add(ductAccessoryFilter);
            pipeDuctFilters.Add(cableTrayFilter);
            pipeDuctFilters.Add(cableTrayFittingFilter);
            LogicalOrFilter pipeOrDuctFilter = new LogicalOrFilter(pipeDuctFilters);
            // 儲存所有RevitLink擁有管道與風管的Document
            IList<RevitLinkInstance> rvtInss = new FilteredElementCollector(doc, doc.ActiveView.Id).OfClass(typeof(RevitLinkInstance)).WhereElementIsNotElementType().Cast<RevitLinkInstance>().ToList();
            foreach (RevitLinkInstance rvtIns in rvtInss)
            {
                try
                {
                    IList<Element> pipeOrBeamList = new FilteredElementCollector(rvtIns.GetLinkDocument()).WherePasses(pipeOrDuctFilter).WhereElementIsNotElementType().ToElements();
                    //pipeOrBeamList = pipeOrBeamList.Where(x => x.Id.IntegerValue.Equals(1588824)).ToList(); // Test
                    if (pipeOrBeamList.Count > 0)
                    {
                        pipeDuctLinkDocs.Add(rvtIns);
                    }
                }
                catch (Autodesk.Revit.Exceptions.ArgumentNullException)
                {

                }
            }

            List<OpeningInfo> openingInfoList = new List<OpeningInfo>(); // 儲存樑牆板資訊
            // 儲存樑牆的RevitLink
            IList<ElementFilter> elementFilters = new List<ElementFilter>();
            ElementCategoryFilter wallFilter = new ElementCategoryFilter(BuiltInCategory.OST_Walls); // 牆
            ElementCategoryFilter beamFilter = new ElementCategoryFilter(BuiltInCategory.OST_StructuralFraming); // 樑
            ElementCategoryFilter floorFilter = new ElementCategoryFilter(BuiltInCategory.OST_Floors); // 樓板
            elementFilters.Add(wallFilter);
            elementFilters.Add(beamFilter);
            elementFilters.Add(floorFilter);
            LogicalOrFilter wallBeamFilter = new LogicalOrFilter(elementFilters);
            // 查詢外部連結交集到Solid的管道
            ElementClassFilter revitLinkInsFilter = new ElementClassFilter(typeof(RevitLinkInstance));
            List<RevitLinkInstance> revitLinks = new FilteredElementCollector(doc, doc.ActiveView.Id).WherePasses(revitLinkInsFilter).Cast<RevitLinkInstance>().ToList();
            foreach (RevitLinkInstance revitLink in revitLinks)
            {
                try
                {
                    IList<Element> wallOrBeamElems = new FilteredElementCollector(revitLink.GetLinkDocument()).WherePasses(wallBeamFilter).WhereElementIsNotElementType().ToElements();
                    //wallOrBeamElems = wallOrBeamElems.Where(x => x.Id.IntegerValue.Equals(559157)).ToList(); // Test
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
                        if (!wallFamilyName.Equals("帷幕牆") && !wallTypeName.Contains("輕隔間") && !wallTypeName.Contains("琺瑯") && !wallTypeName.Contains("廁所隔牆")) // 如果是帷幕牆或輕隔間或琺瑯牆則不開口
                        {
                            Options opt = new Options();
                            opt.ComputeReferences = true;
                            opt.DetailLevel = doc.ActiveView.DetailLevel;
                            GeometryElement geomElem = elem.get_Geometry(opt);
                            // 儲存當前專案所有樑牆的Solid
                            foreach (GeometryObject geomObj in geomElem)
                            {
                                Solid solid = null;
                                solid = GetSymbolSolids(geomObj, revitLink, solid);
                                try
                                {
                                    if (solid.SurfaceArea != 0)
                                    {
                                        FindInputSolidBBElems(revitLink.GetLinkDocument(), elem, solid, pipeDuctLinkDocs, openingInfoList); // 透過BoundingBox找到與牆樑Solid干涉的管
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
                catch (Autodesk.Revit.Exceptions.ArgumentNullException)
                {

                }
            }

            // 自動開口
            TransactionGroup tranGrp1 = new TransactionGroup(doc, "自動開口");
            tranGrp1.Start();
            int amount = 0;
            using (Transaction trans = new Transaction(doc, "放置開口"))
            {
                // 關閉警示視窗
                FailureHandlingOptions options = trans.GetFailureHandlingOptions();
                MyPreProcessor preproccessor = new MyPreProcessor();
                options.SetClearAfterRollback(true);
                options.SetFailuresPreprocessor(preproccessor);
                trans.SetFailureHandlingOptions(options);
                trans.Start();
                List<FamilySymbol> openFSList = FindFS(doc); // 找到FamilySymbol
                foreach (OpeningInfo openingInfo in openingInfoList)
                {
                    try
                    {
                        foreach (CrushElemInfo crushElemInfo in openingInfo.crushElemInfos)
                        {
                            amount = PlaceOpening(doc, crushElemInfo, openFSList, amount);
                        }
                    }
                    catch (Exception)
                    {

                    }
                }
                doc.Regenerate();
                uidoc.RefreshActiveView();
                trans.Commit();
            }
            // 旋轉修改開口參數
            using (Transaction trans = new Transaction(doc, "旋轉修改開口參數"))
            {
                // 關閉警示視窗
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
            // 計算底部高程
            using(Transaction trans = new Transaction(doc, "計算底部高程"))
            {
                trans.Start();
                EditBottomElevation(doc);
                doc.Regenerate();
                uidoc.RefreshActiveView();
                trans.Commit();
            }
            List<int> deleteIds = new List<int>();
            // 查詢新增的開口修改位置後, 是否LocalPoint重複, 重複則移除
            using(Transaction trans = new Transaction(doc, "移除重疊開口"))
            {
                trans.Start();
                foreach (int id in newOpeningIds)
                {
                    ElementId elemId = new ElementId(id);
                    FamilyInstance newOpening = doc.GetElement(elemId) as FamilyInstance;
                    LocationPoint lp = newOpening.Location as LocationPoint;
                    XYZ xyz = lp.Point;
                    // 確認是否有原開口在同座標
                    bool trueOrFalse = false;
                    double xyzX = Math.Round(xyz.X, 8, MidpointRounding.AwayFromZero);
                    double xyzY = Math.Round(xyz.Y, 8, MidpointRounding.AwayFromZero);
                    double xyzZ = Math.Round(xyz.Z, 8, MidpointRounding.AwayFromZero);
                    foreach (XYZ openingXYZ in openingXYZs)
                    {
                        if (Math.Round(openingXYZ.X, 8, MidpointRounding.AwayFromZero).Equals(xyzX) &&
                           Math.Round(openingXYZ.Y, 8, MidpointRounding.AwayFromZero).Equals(xyzY) &&
                           Math.Round(openingXYZ.Z, 8, MidpointRounding.AwayFromZero).Equals(xyzZ))
                        {
                            trueOrFalse = true;
                            break;
                        }
                    }
                    // 刪除同座標的開口
                    if (trueOrFalse == true)
                    {
                        doc.Delete(elemId);
                        amount--;
                        // 找到newOpeningIds內的Id名稱刪除
                        deleteIds.Add(id);
                    }
                }
                doc.Regenerate();
                uidoc.RefreshActiveView();
                trans.Commit();
            }
            tranGrp1.Assimilate();

            DateTime timeEnd = DateTime.Now; // 計時結束 取得目前時間
            TimeSpan totalTime = timeEnd - timeStart;
            string newOpeningId = string.Empty;
            // 刪除重疊開口ID
            foreach(int id in deleteIds)
            {
                newOpeningIds.Remove(id);
            }
            int i = 1;
            foreach(int id in newOpeningIds)
            {
                newOpeningId += "\n" + i + ". " + id;
                i++;
            }
            TaskDialog.Show("Revit", "耗時：" + totalTime.Minutes + " 分 " + totalTime.Seconds + " 秒 " + "\n\n共放置 " + amount + " 個開口。\n"/* + newOpeningId*/);

            return Result.Succeeded;
        }
        // 取得幾何圖形, Symbol Solids(未修改過Instance)
        private static Solid GetSymbolSolids(GeometryObject geomObj, RevitLinkInstance revitLink, Solid solid)
        {
            if (geomObj is Solid)
            {
                solid = (Solid)geomObj;
                // GetTransform or GetTotalTransform or what?
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
                    try
                    {
                        if (solid.SurfaceArea > 0)
                        {
                            break;
                        }
                    }
                    catch (NullReferenceException)
                    {

                    }
                }
            }
            else if (geomObj is GeometryElement)
            {
                GeometryElement geomElem2 = (GeometryElement)geomObj;
                foreach (GeometryObject geomObj2 in geomElem2)
                {
                    solid = GetSymbolSolids(geomObj2, revitLink, solid);
                    if (solid.SurfaceArea > 0)
                    {
                        break;
                    }
                }
            }
            return solid;
        }
        // 透過BoundingBox找到與牆樑Solid干涉的管
        private static void FindInputSolidBBElems(Document revitLinkDoc, Element wallOrBeam, Solid solid, List<RevitLinkInstance> pipeDuctLinkDocs, List<OpeningInfo> openingInfoList)
        {
            // 轉換成專案座標
            // 取得轉換後 Solid 的 BoundingBox
            BoundingBoxXYZ bbox = solid.GetBoundingBox();
            // ComputeCentroid() 取得 Solid 的 centroid
            XYZ solidCentroid = solid.ComputeCentroid();
            // 用 Transform.Identity 新增一個 transform，Origin 設為上述的 centroid
            Transform transform = Transform.Identity;
            transform.Origin = solidCentroid;
            // 用新增的 transform.OfPoint() 轉換 Solid BoundingBox Min 及 Max
            XYZ solidMin = transform.OfPoint(bbox.Min);
            XYZ solidMax = transform.OfPoint(bbox.Max);
            List<ElementTransform> elementTransformList = new List<ElementTransform>();
            List<Element> interferenceElems = new List<Element>();
            // 擁有管道與風管的RevitLink模型
            foreach (RevitLinkInstance pipeDuctLinkDoc in pipeDuctLinkDocs)
            {
                ElementTransform elementTransform = new ElementTransform();
                elementTransform.transform = pipeDuctLinkDoc.GetTotalTransform();
                // 轉換成連結模型座標
                Transform linkTransform = pipeDuctLinkDoc.GetTotalTransform().Inverse; // 要將Solid min max轉換成Instance座標, LinkInstance的Transform必須Inverse
                XYZ linkSolidMin = linkTransform.OfPoint(solidMin);
                XYZ linkSolidMax = linkTransform.OfPoint(solidMax);
                // 轉換後的 min max 新建 Outline
                Outline linkOutline = new Outline(linkSolidMin, linkSolidMax);
                // 用 linkOutline 產生 BoundingBoxIntersectsFilter
                BoundingBoxIntersectsFilter linkBBFilter = new BoundingBoxIntersectsFilter(linkOutline);
                IList<Element> bbElems = new FilteredElementCollector(pipeDuctLinkDoc.GetLinkDocument()).WherePasses(linkBBFilter).ToElements();
                foreach (Element bbElem in bbElems)
                {
                    if (bbElem is Pipe || bbElem is Duct || bbElem is CableTray || bbElem is FamilyInstance)
                    {
                        if(bbElem is FamilyInstance)
                        {
                            if (bbElem.Category.Name.Equals("管配件") || bbElem.Category.Name.Equals("管附件"))
                            {
                                FamilyInstance familyInstance = bbElem as FamilyInstance;
                                string fsName = familyInstance.Symbol.Family.Name;
                                //if (fsName.Contains("彎頭-對焊-碳鋼"))
                                //{
                                    elementTransform.elements.Add(bbElem);
                                    interferenceElems.Add(bbElem);
                                //}
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
                                FamilyInstance familyInstance = bbElem as FamilyInstance;
                                string fsName = familyInstance.Symbol.Family.Name;
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
                    SaveElemData(revitLinkDoc, wallOrBeam, solid, elemTransform.elements, elemTransform.transform, openingInfoList);
                }
            }
        }
        // 儲存OpeningInfo資料
        private static void SaveElemData(Document revitLinkDoc, Element wallOrBeam, Solid solid, List<Element> interferenceElems, Transform linkTransform, List<OpeningInfo> openingInfoList)
        {
            OpeningInfo openingInfo = new OpeningInfo();
            ElementId levelElemId = null;
            Parameter thicknessPara = null;
            if (wallOrBeam is Wall)
            {
                try
                {
                    openingInfo.type = "Wall"; // 品類
                    levelElemId = wallOrBeam.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT).AsElementId();
                    openingInfo.length = wallOrBeam.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble(); // 長度

                    List<WallType> wallTypelList = new FilteredElementCollector(revitLinkDoc).OfClass(typeof(WallType)).OfCategory(BuiltInCategory.OST_Walls).Cast<WallType>().ToList();
                    string wallName = wallOrBeam.Name; // 牆名稱
                    Parameter wallTypePara = wallOrBeam.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM);
                    string wallTypeName = wallTypePara.AsValueString(); // 牆類型名稱
                    WallType wallType = (from x in wallTypelList
                                         where x.Name.Equals(wallName) && x.FamilyName.Equals(wallTypeName)
                                         select x).FirstOrDefault();
                    thicknessPara = wallType.get_Parameter(BuiltInParameter.WALL_ATTR_WIDTH_PARAM);
                }
                catch (Exception ex)
                {
                    string error = wallOrBeam.Id + "\n" + levelElemId + "\n" + ex.Message + "\n" + ex.ToString();
                }
            }
            else if (wallOrBeam is BeamSystem || wallOrBeam is FamilyInstance)
            {
                try
                {
                    openingInfo.type = "Beam"; // 品類
                    levelElemId = wallOrBeam.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM).AsElementId();
                    openingInfo.length = wallOrBeam.get_Parameter(BuiltInParameter.INSTANCE_LENGTH_PARAM).AsDouble(); // 長度

                    List<FamilySymbol> familySymbolList = new FilteredElementCollector(revitLinkDoc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_StructuralFraming).Cast<FamilySymbol>().ToList();
                    string beamName = wallOrBeam.Name; // 樑名稱
                    Parameter beamFamilyName = wallOrBeam.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM);
                    string beamFamily = beamFamilyName.AsValueString(); // 樑族群名稱
                    FamilySymbol beamFS = (from x in familySymbolList
                                           where x.Name.Equals(beamName) && x.FamilyName.Equals(beamFamily)
                                           select x).FirstOrDefault();
                    // 如果FamilySymbol尚未啟動, 必須啟用才能使用
                    if (beamFS != null)
                    {
                        if (!beamFS.IsActive)
                        {
                            beamFS.Activate();
                            revitLinkDoc.Regenerate();
                        }
                    }
                    thicknessPara = beamFS.get_Parameter(BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH);
                    if (thicknessPara == null)
                    {
                        thicknessPara = beamFS.LookupParameter("b");
                        if (thicknessPara == null)
                        {
                            thicknessPara = beamFS.LookupParameter("樑寬度");
                        }
                    }
                }
                catch (NullReferenceException ex)
                {
                    string error = wallOrBeam.Id + "\n" + levelElemId + "\n" + ex.Message + "\n" + ex.ToString();
                }
                catch(Exception ex)
                {
                    string error = wallOrBeam.Id + "\n" + levelElemId + "\n" + ex.Message + "\n" + ex.ToString();
                }
            }
            else if (wallOrBeam is Floor)
            {
                try
                {
                    openingInfo.type = "Floor"; // 品類
                    levelElemId = wallOrBeam.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM).AsElementId();
                    thicknessPara = wallOrBeam.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM);
                }
                catch (Exception ex)
                {
                    string error = wallOrBeam.Id + "\n" + levelElemId + "\n" + ex.Message + "\n" + ex.ToString();
                }
            }
            string[] docNames = wallOrBeam.Document.Title.Split('-');
            if(docNames.Length > 1)
            {
                openingInfo.docName = docNames[1]; // 專案名稱縮寫
            }
            openingInfo.element = wallOrBeam; // 收集樑牆板資料
            openingInfo.solid = solid; // 樑牆Solid
            if(wallOrBeam is Floor)
            {
                openingInfo.beamWallAngle = 0;
            }
            else
            {
                try
                {
                    LocationCurve lc = wallOrBeam.Location as LocationCurve;
                    Line line = lc.Curve as Line;
                    double beamWallAngle = PointRotation(line.Tessellate()[0], line.Tessellate()[1]);
                    openingInfo.beamWallAngle = beamWallAngle; // 樑牆旋轉的角度
                }
                catch (NullReferenceException) // 排除弧形牆
                {
                    openingInfo.beamWallAngle = 0; // 樑牆旋轉的角度
                }
                catch(Exception)
                {
                    
                }
            }
            // 找到專案與連結模型的相同Level, 找到該Level的高程, 留意Level取名
            Level docLevel = null;
            try
            {
                Level level = revitLinkDoc.GetElement(levelElemId) as Level;
                docLevel = (from x in docLevels
                            where x.Name.Contains(level.Name)
                            select x).FirstOrDefault();
                openingInfo.level = docLevel; // 樓層
            }
            catch (NullReferenceException)
            {

            }
            catch(Exception ex)
            {
                string error = ex.Message + "\n" + ex.ToString();
            }
            openingInfo.number = 0; // 編號
            foreach (Element interferenceElem in interferenceElems)
            {
                if (interferenceElem is Pipe || interferenceElem is Duct || interferenceElem is CableTray || interferenceElem is FamilyInstance)
                {
                    CrushElemInfo crushElemInfo = new CrushElemInfo();
                    // 解析專案路徑的檔名
                    string[] docName = interferenceElem.Document.Title.Split('-');
                    if(docName.Length > 1)
                    {
                        crushElemInfo.docName = docName[1]; // 專案名稱縮寫
                    }
                    else
                    {
                        crushElemInfo.docName = interferenceElem.Document.Title; // 專案名稱
                    }
                    crushElemInfo.pipeOrDuct = interferenceElem; // BoundingBox, 干涉的管與風管
                    crushElemInfo.hostType = openingInfo.type; // 干涉的主體品類
                    // 如果牆的底部約束Level查詢對應不到, Level則以管道樓層為主
                    if(docLevel == null)
                    {
                        try
                        {
                            string levelName = interferenceElem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM).AsValueString();
                            docLevel = (from x in docLevels
                                        where x.Name.Contains(levelName)
                                        select x).FirstOrDefault();
                        }
                        catch (NullReferenceException)
                        {
                            string levelName = interferenceElem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM).AsValueString();
                            docLevel = (from x in docLevels
                                        where x.Name.Contains(levelName)
                                        select x).FirstOrDefault();
                        }
                        catch (Exception ex)
                        {
                            string error = ex.Message + "\n" + ex.ToString();
                        }
                    }
                    crushElemInfo.level = docLevel; // 參考樓層
                    crushElemInfo.number = 0; // 編號
                    if (interferenceElem is FamilyInstance)
                    {
                        if (/*interferenceElem.Category.Name.Equals("管配件") || interferenceElem.Category.Name.Equals("管附件") ||*/
                            interferenceElem.Category.Name.Equals("風管附件") ||interferenceElem.Category.Name.Equals("電纜架配件"))
                        {
                            LocationPoint lp = interferenceElem.Location as LocationPoint;
                            XYZ centerPoint = lp.Point; // 中心點
                            XYZ bbXYZ1 = interferenceElem.get_BoundingBox(revitLinkDoc.ActiveView).Max;
                            XYZ bbXYZ2 = interferenceElem.get_BoundingBox(revitLinkDoc.ActiveView).Min;
                            Parameter diameterPara = null;
                            FamilyInstance familyInstance = interferenceElem as FamilyInstance;
                            string fsName = familyInstance.Symbol.Family.Name;
                            if (interferenceElem.Category.Name.Equals("管配件") || interferenceElem.Category.Name.Equals("管附件"))
                            {
                                crushElemInfo.type = "PipeFitting";
                                //if (fsName.Contains("彎頭-對焊-碳鋼"))
                                //{
                                try
                                {
                                    bool isInsulation = false; // 是否為保溫管
                                    double size = 0.0; // 直徑大小
                                    diameterPara = interferenceElem.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
                                    if (diameterPara != null)
                                    {
                                        crushElemInfo.pipeType = diameterPara.AsValueString(); // 系統類型
                                        try
                                        {
                                            double outerDiameter = interferenceElem.get_Parameter(BuiltInParameter.RBS_PIPE_SIZE_MAXIMUM).AsDouble(); // 機械_直徑
                                            //double outerDiameter = interferenceElem.LookupParameter("Nominal Diameter").AsDouble(); // 機械_直徑
                                            crushElemInfo.size = outerDiameter; // 管直徑
                                            // 如果絕緣體厚度大於0, 須加上絕緣體厚度開口
                                            double insulationThickness = interferenceElem.get_Parameter(BuiltInParameter.RBS_REFERENCE_INSULATION_THICKNESS).AsDouble();
                                            crushElemInfo.insulationThickness = insulationThickness; // 絕緣體厚度
                                            if (insulationThickness > 0)
                                            {
                                                isInsulation = true; // 是否為保溫管
                                                //outerDiameter += insulationThickness; // 管外徑 + 絕緣體厚度
                                            }
                                            outerDiameter = UnitUtils.ConvertFromInternalUnits(outerDiameter, DisplayUnitType.DUT_MILLIMETERS); // 機械_直徑
                                            size = outerDiameter;
                                            outerDiameter = SinoOpenSize(isInsulation, outerDiameter); // 尺寸比對後開口
                                            crushElemInfo.diameter = UnitUtils.ConvertToInternalUnits(outerDiameter, DisplayUnitType.DUT_MILLIMETERS); // 機械_直徑
                                        }
                                        catch(Exception ex)
                                        {
                                            string info = fsName + "\n" + ex.Message + "\n" + ex.ToString();
                                        }
                                    }
                                    else
                                    {
                                        diameterPara = interferenceElem.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM); // 機械_直徑
                                        crushElemInfo.size = diameterPara.AsDouble(); // 管直徑
                                        //string[] diameter = diameterPara.AsValueString().Split(new char[] { ' ' });
                                        //double diameterSize = Convert.ToDouble(diameter[0]);
                                        double diameterSize = UnitUtils.ConvertToInternalUnits(diameterPara.AsDouble(), DisplayUnitType.DUT_MILLIMETERS); // 機械_直徑
                                        size = diameterSize;
                                        //diameterSize = OpenSize(diameterSize); // 尺寸比對後開口
                                        diameterSize = SinoOpenSize(isInsulation, diameterSize); // 尺寸比對後開口
                                        crushElemInfo.diameter = UnitUtils.ConvertToInternalUnits(diameterSize, DisplayUnitType.DUT_MILLIMETERS); // 管直徑
                                    }
                                    // 厚度
                                    if (thicknessPara != null)
                                    {
                                        crushElemInfo.thickness = thicknessPara.AsDouble(); // 牆厚度
                                    }
                                    else
                                    {
                                        crushElemInfo.thickness = UnitUtils.ConvertToInternalUnits(100, DisplayUnitType.DUT_MILLIMETERS); // 風管附件長度
                                    }
                                    // 非保溫管且尺寸小於50, 不執行開口
                                    if (isInsulation == false && size < 50)
                                    {

                                    }
                                    else
                                    {
                                        // 如果長寬高都不等於0時, 找到風管附件X、Y的向量去檢查是否有接觸牆
                                        if (crushElemInfo.size != 0 && crushElemInfo.thickness != 0)
                                        {
                                            FindSolidIntersection(interferenceElem, solid, openingInfo, crushElemInfo);
                                        }
                                    }
                                }
                                catch (NullReferenceException)
                                {

                                }
                                catch (Exception)
                                {

                                }
                                // 如果長寬高都不等於0時, 找到風管附件X、Y的向量去檢查是否有接觸牆
                                if (crushElemInfo.ductHeight != 0 && crushElemInfo.ductWight != 0 && crushElemInfo.thickness != 0)
                                {
                                    FindSolidIntersection(interferenceElem, solid, openingInfo, crushElemInfo);
                                }
                                //}
                            }
                            else if(interferenceElem.Category.Name.Equals("風管附件"))
                            {
                                crushElemInfo.type = "DuctAccessory";
                                if (fsName.Contains("防火風門") || fsName.Contains("防火風門 - 矩形") || fsName.Contains("電動風門 - 矩形"))
                                {
                                    try
                                    {                                        
                                        crushElemInfo.ductHeight = interferenceElem.LookupParameter("風管高度").AsDouble(); // 高度
                                        crushElemInfo.ductWight = interferenceElem.LookupParameter("風管寬度").AsDouble(); // 寬度
                                        // 厚度
                                        if (thicknessPara != null)
                                        {
                                            crushElemInfo.thickness = thicknessPara.AsDouble(); // 牆厚度
                                        }
                                        else
                                        {
                                            crushElemInfo.thickness = interferenceElem.LookupParameter("風門長度").AsDouble(); // 風管附件長度
                                        }
                                    }
                                    // 未備註的風管族群不擺放開口, Ex. 消音箱
                                    catch (NullReferenceException)
                                    {

                                    }
                                    catch (Exception)
                                    {

                                    }
                                    // 如果長寬高都不等於0時, 找到風管附件X、Y的向量去檢查是否有接觸牆
                                    if (crushElemInfo.ductHeight != 0 && crushElemInfo.ductWight != 0 && crushElemInfo.thickness != 0)
                                    {
                                        FindSolidIntersection(interferenceElem, solid, openingInfo, crushElemInfo);
                                    }
                                }
                                else if (fsName.Contains("異徑順水三通"))
                                {
                                    try
                                    {
                                        // 長度
                                        diameterPara = interferenceElem.LookupParameter("最大尺寸");
                                        string diameter = diameterPara.AsValueString().Replace(" mm", "");
                                        double diameterSize = Convert.ToDouble(diameter); // 長
                                        crushElemInfo.thickness = UnitUtils.ConvertToInternalUnits(diameterSize, DisplayUnitType.DUT_MILLIMETERS);
                                        diameterSize = Convert.ToDouble(diameter); // 寬
                                        crushElemInfo.ductWight = UnitUtils.ConvertToInternalUnits(diameterSize, DisplayUnitType.DUT_MILLIMETERS);
                                        // 厚度
                                        if (thicknessPara != null)
                                        {
                                            crushElemInfo.thickness = thicknessPara.AsDouble(); // 牆厚度
                                        }
                                        else
                                        {
                                            crushElemInfo.ductHeight = UnitUtils.ConvertToInternalUnits(diameterSize, DisplayUnitType.DUT_MILLIMETERS); // 風管附件寬度 = 高度
                                        }
                                    }
                                    catch (Exception) { }
                                }
                            }
                            else if (interferenceElem.Category.Name.Equals("電纜架配件"))
                            {
                                crushElemInfo.type = "CableTrayFitting";
                                //if (fsName.Contains("防火風門 - 矩形") || fsName.Contains("電動風門 - 矩形"))
                                //{
                                try
                                {
                                    // 高度
                                    diameterPara = interferenceElem.LookupParameter("托盤高度");
                                    //string[] diameters = diameterPara.AsValueString().Split(' ');
                                    //string diameter = diameters[0];
                                    //double diameterSize = Convert.ToDouble(diameter) + 50;
                                    //crushElemInfo.ductHeight = UnitUtils.ConvertToInternalUnits(diameterSize, DisplayUnitType.DUT_MILLIMETERS);
                                    crushElemInfo.ductHeight = diameterPara.AsDouble() + UnitUtils.ConvertToInternalUnits(50, DisplayUnitType.DUT_MILLIMETERS);
                                    // 寬度
                                    diameterPara = interferenceElem.LookupParameter("托盤寬度 1");
                                    //diameters = diameterPara.AsValueString().Split(' ');
                                    //diameter = diameters[0];
                                    //diameterSize = Convert.ToDouble(diameter);
                                    //crushElemInfo.ductWight = UnitUtils.ConvertToInternalUnits(diameterSize, DisplayUnitType.DUT_MILLIMETERS);
                                    crushElemInfo.ductWight = diameterPara.AsDouble();
                                    // 厚度
                                    if (thicknessPara != null)
                                    {
                                        crushElemInfo.thickness = thicknessPara.AsDouble(); // 牆厚度
                                    }
                                    else
                                    {
                                        diameterPara = interferenceElem.LookupParameter("長度 1");
                                        //diameters = diameterPara.AsValueString().Split(' ');
                                        //diameter = diameters[0];
                                        //diameterSize = Convert.ToDouble(diameter);
                                        //crushElemInfo.thickness = UnitUtils.ConvertToInternalUnits(diameterSize, DisplayUnitType.DUT_MILLIMETERS); // 電纜架配件長度
                                        crushElemInfo.thickness = diameterPara.AsDouble(); // 電纜架配件長度
                                    }
                                }
                                // 未備註的電纜架族群不擺放開口
                                catch (NullReferenceException)
                                {

                                }
                                catch (Exception)
                                {

                                }
                                // 如果長寬高都不等於0時, 找到風管附件X、Y的向量去檢查是否有接觸牆
                                if (crushElemInfo.ductHeight != 0 && crushElemInfo.ductWight != 0 && crushElemInfo.thickness != 0)
                                {
                                    FindSolidIntersection(interferenceElem, solid, openingInfo, crushElemInfo);
                                }
                                //}
                            }
                        }
                    }
                    else
                    {
                        Curve pipeCurve = (interferenceElem.Location as LocationCurve).Curve.CreateTransformed(linkTransform);
                        bool isInsulation = false; // 是否為保溫管
                        double size = 0.0; // 直徑大小
                        if (interferenceElem is Pipe)
                        {
                            crushElemInfo.type = "Pipe";
                            Parameter diameterPara = interferenceElem.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
                            if (diameterPara != null)
                            {
                                crushElemInfo.pipeType = diameterPara.AsValueString(); // 系統類型
                                double outerDiameter = interferenceElem.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble(); // 機械_直徑
                                crushElemInfo.size = outerDiameter; // 管直徑
                                // 如果絕緣體厚度大於0, 須加上絕緣體厚度開口
                                double insulationThickness = interferenceElem.get_Parameter(BuiltInParameter.RBS_REFERENCE_INSULATION_THICKNESS).AsDouble();
                                crushElemInfo.insulationThickness = insulationThickness; // 絕緣體厚度
                                if (insulationThickness > 0)
                                {
                                    isInsulation = true; // 是否為保溫管
                                    //outerDiameter += insulationThickness; // 管外徑 + 絕緣體厚度
                                }
                                outerDiameter = UnitUtils.ConvertFromInternalUnits(outerDiameter, DisplayUnitType.DUT_MILLIMETERS); // 機械_直徑
                                size = outerDiameter;
                                outerDiameter = SinoOpenSize(isInsulation, outerDiameter); // 尺寸比對後開口
                                crushElemInfo.diameter = UnitUtils.ConvertToInternalUnits(outerDiameter, DisplayUnitType.DUT_MILLIMETERS); // 機械_直徑
                            }
                            else
                            {
                                diameterPara = interferenceElem.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM); // 機械_直徑
                                crushElemInfo.size = diameterPara.AsDouble(); // 管直徑
                                string[] diameter = diameterPara.AsValueString().Split(new char[] { ' ' });
                                double diameterSize = Convert.ToDouble(diameter[0]);
                                size = diameterSize;
                                //diameterSize = OpenSize(diameterSize); // 尺寸比對後開口
                                diameterSize = SinoOpenSize(isInsulation, diameterSize); // 尺寸比對後開口
                                crushElemInfo.diameter = UnitUtils.ConvertToInternalUnits(diameterSize, DisplayUnitType.DUT_MILLIMETERS); // 管直徑
                            }
                        }
                        else if (interferenceElem is Duct)
                        {
                            crushElemInfo.type = "Duct";
                            //Parameter diameterPara = interferenceElem.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
                            //string[] diameter = diameterPara.AsValueString().Split(new char[] { ' ' });
                            //double diameterSize = Convert.ToDouble(diameter[0]);
                            //double height = UnitUtils.ConvertFromInternalUnits(diameterPara.AsDouble(), DisplayUnitType.DUT_MILLIMETERS);
                            double height = interferenceElem.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM).AsDouble();
                            crushElemInfo.ductHeight = interferenceElem.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM).AsDouble(); // 高度
                            //diameterPara = interferenceElem.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                            //diameter = diameterPara.AsValueString().Split(new char[] { ' ' });
                            //diameterSize = Convert.ToDouble(diameter[0]);
                            double width = interferenceElem.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM).AsDouble();
                            crushElemInfo.ductWight = interferenceElem.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM).AsDouble(); // 寬度
                            size = UnitUtils.ConvertFromInternalUnits(width, DisplayUnitType.DUT_MILLIMETERS);
                        }
                        else if (interferenceElem is CableTray)
                        {
                            crushElemInfo.type = "CableTray";
                            Parameter diameterPara = interferenceElem.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM);
                            crushElemInfo.ductHeight = diameterPara.AsDouble() + UnitUtils.ConvertToInternalUnits(50, DisplayUnitType.DUT_MILLIMETERS); // 高度
                            diameterPara = interferenceElem.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM);
                            size = UnitUtils.ConvertFromInternalUnits(diameterPara.AsDouble(), DisplayUnitType.DUT_MILLIMETERS);
                            crushElemInfo.ductWight = diameterPara.AsDouble() + UnitUtils.ConvertToInternalUnits(50, DisplayUnitType.DUT_MILLIMETERS); // 寬度
                        }
                        if (thicknessPara != null)
                        {
                            crushElemInfo.thickness = thicknessPara.AsDouble();
                        }
                        else
                        {
                            crushElemInfo.thickness = 1;
                        }
                        // 非保溫管且尺寸小於50, 不執行開口
                        if (isInsulation == false && size < 50)
                        {
                            
                        }
                        else
                        {
                            FindFaceIntersectLine(solid, pipeCurve, openingInfo, crushElemInfo); // 找到線與面交集點
                        }
                    }
                }
            }
            if(openingInfo.crushElemInfos.Count > 0)
            {
                openingInfoList.Add(openingInfo);
            }
        }
        // 找到線與面交集點
        private static void FindFaceIntersectLine(Solid solid, Curve curve, OpeningInfo openingInfo, CrushElemInfo crushElemInfo)
        {
            XYZ startPoint = new XYZ();
            XYZ endPoint = new XYZ();
            int i = 1;
            foreach (Face face in solid.Faces)
            {
                // 設置交集結果
                IntersectionResultArray intersectionR = new IntersectionResultArray();
                // 比較面與曲線的交集結果
                SetComparisonResult comparisonR = face.Intersect(curve, out intersectionR);
                // 設置交集點
                XYZ intersectionResult = null;

                // 相交
                if (SetComparisonResult.Disjoint != comparisonR)
                {
                    try
                    {
                        if(intersectionR != null)
                        {
                            if (!intersectionR.IsEmpty)
                            {
                                int mod = i % 2;
                                crushElemInfo.insfaces.Add(face); // 接觸到的兩個面
                                intersectionResult = intersectionR.get_Item(0).XYZPoint;
                                crushElemInfo.insXYZs.Add(intersectionResult); // 接觸到的兩個面的交集點
                                if (mod == 1) // 碰到奇數面的座標為起點
                                {
                                    startPoint = intersectionResult;
                                }
                                else if (mod == 0) // 碰到偶數面的座標為終點
                                {
                                    endPoint = intersectionResult;
                                    XYZ insXYZ = new XYZ((startPoint.X + endPoint.X) / 2, (startPoint.Y + endPoint.Y) / 2, (startPoint.Z + endPoint.Z) / 2);
                                    // 元件擺放點
                                    if (openingInfo.element is Floor)
                                    {
                                        crushElemInfo.xyzs.Add(endPoint);
                                        // 找到與放置樓程高程的偏移
                                        double z = endPoint.Z;
                                        double elevation = crushElemInfo.level.get_Parameter(BuiltInParameter.LEVEL_ELEV).AsDouble();
                                        crushElemInfo.deviation = z - elevation; // 偏移
                                    }
                                    else
                                    {
                                        crushElemInfo.xyzs.Add(insXYZ);
                                        // 找到與放置樓程高程的偏移
                                        double z = insXYZ.Z;
                                        if (crushElemInfo.level != null) // 避免視圖無法對應到正確的Level
                                        {
                                            double elevation = crushElemInfo.level.get_Parameter(BuiltInParameter.LEVEL_ELEV).AsDouble();
                                            crushElemInfo.deviation = z - elevation; // 偏移
                                        }
                                        // 開口下方調整到電纜架下方50公分
                                        if (crushElemInfo.type.Equals("CableTray"))
                                        {
                                            // 開口的高度 = 250 / 2 = 125
                                            double openingHeight = 250 / 2;
                                            // 找到電纜架的高度 / 2
                                            string[] cableTrayPara = crushElemInfo.pipeOrDuct.LookupParameter("高度").AsValueString().Split(' ');
                                            double cableTrayHeight = Convert.ToDouble(cableTrayPara[0]) / 2;
                                            // 偏移至開口底部與電纜架底部距離50cm
                                            double deviation = openingHeight - (cableTrayHeight + 50); // 中心點到中心點所以
                                            double move = UnitUtils.ConvertToInternalUnits(deviation, DisplayUnitType.DUT_MILLIMETERS); // 偏移
                                            double elevation = crushElemInfo.level.get_Parameter(BuiltInParameter.LEVEL_ELEV).AsDouble();
                                            crushElemInfo.deviation = z - elevation + move; // 偏移
                                        }
                                    }
                                    crushElemInfo.axis = Line.CreateBound(insXYZ, new XYZ(insXYZ.X, insXYZ.Y, insXYZ.Z + 10)); // 軸心
                                    crushElemInfo.pipeAngle = PointRotation(startPoint, endPoint); // 管角度
                                    openingInfo.crushElemInfos.Add(crushElemInfo);
                                }
                                i++;
                            }
                        }
                    }
                    catch (NullReferenceException)
                    {

                    }
                }
            }
        }
        // 找到風管附件與Element衝突
        private static void FindSolidIntersection(Element interferenceElem, Solid solid, OpeningInfo openingInfo, CrushElemInfo crushElemInfo)
        {
            ICollection<ElementId> interferenceElems = new List<ElementId>();
            interferenceElems.Add(interferenceElem.Id);
            FilteredElementCollector collector = new FilteredElementCollector(interferenceElem.Document, interferenceElems);
            List<FamilyInstance> familyInstances = collector.OfClass(typeof(FamilyInstance)).WherePasses(new ElementIntersectsSolidFilter(solid)).WhereElementIsNotElementType().Cast<FamilyInstance>().ToList();
            foreach (FamilyInstance familyInstance in familyInstances)
            {
                LocationPoint lp = familyInstance.Location as LocationPoint;
                XYZ insXYZ = lp.Point;
                // 找到與放置樓程高程的偏移
                double z = insXYZ.Z;
                // 元件擺放點
                if (openingInfo.element is Floor)
                {
                    //LocationCurve lc = openingInfo.element.Location as LocationCurve;
                    //Line line = lc.Curve as Line;
                    //line.MakeUnbound(); // 延伸線段
                    //insXYZ = line.Project(insXYZ).XYZPoint; // 座標點與中心線的垂足點
                    //crushElemInfo.xyzs.Add(insXYZ);
                    crushElemInfo.xyzs.Add(insXYZ);
                    double elevation = crushElemInfo.level.get_Parameter(BuiltInParameter.LEVEL_ELEV).AsDouble();
                    crushElemInfo.deviation = z - elevation; // 偏移
                }
                else
                {
                    // 找到風管附件放置點與樑牆中心線的最近點擺放
                    try
                    {
                        LocationCurve lc = openingInfo.element.Location as LocationCurve;
                        Line line = lc.Curve as Line;
                        line.MakeUnbound(); // 延伸線段
                        insXYZ = line.Project(insXYZ).XYZPoint; // 座標點與中心線的垂足點
                        crushElemInfo.xyzs.Add(insXYZ);
                    }
                    catch (Exception)
                    {

                    }
                    if (crushElemInfo.level != null) // 避免視圖無法對應到正確的Level
                    {
                        double elevation = crushElemInfo.level.get_Parameter(BuiltInParameter.LEVEL_ELEV).AsDouble();
                        crushElemInfo.deviation = z - elevation; // 偏移
                    }
                }
                crushElemInfo.axis = Line.CreateBound(insXYZ, new XYZ(insXYZ.X, insXYZ.Y, insXYZ.Z + 10)); // 軸心
                crushElemInfo.pipeAngle = 90 - openingInfo.beamWallAngle; // 管角度
                if (crushElemInfo.xyzs.Count > 0)
                {
                    openingInfo.crushElemInfos.Add(crushElemInfo);
                }
            }
        }
        // 找到FamilySymbol
        private List<FamilySymbol> FindFS(Document doc)
        {
            // 找到套管FamilySymbol
            IList<FamilySymbol> familySymbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
            List<FamilySymbol> openFSList = (from x in familySymbols
                                             where x.FamilyName.Equals("矩形風管樓版開口") || x.FamilyName.Equals("矩形風管牆開口") || x.FamilyName.Equals("圓形水管樓版開口") ||
                                                   x.FamilyName.Equals("圓形水管牆開口") || x.FamilyName.Equals("電纜架樓版開口") || x.FamilyName.Equals("電纜架牆開口")
                                             select x).ToList();
            // 如果FamilySymbol尚未啟動, 必須啟用才能使用
            foreach (FamilySymbol openFS in openFSList)
            {
                if (openFS != null)
                {
                    if (!openFS.IsActive)
                    {
                        openFS.Activate();
                        doc.Regenerate();
                    }
                }
            }
            return openFSList;
        }
        // 尺寸比對後開口
        private static double OpenSize(double radius)
        {
            double[] openSize = new double[] { 13, 16, 20, 27, 35, 40, 50, 65, 80, 90, 100, 125, 150, 200, 250, 300, 350, 400, 450, 500, 600 };

            for (int i = 0; i < openSize.Length; i++)
            {
                try
                {
                    if (radius <= openSize[i])
                    {
                        radius = openSize[i + 1];
                        break;
                    }
                    else if (radius > openSize[openSize.Length - 2])
                    {
                        radius = openSize[openSize.Length - 1];
                        break;
                    }
                }
                catch (Exception)
                {

                }
            }

            return radius;
        }
        // 尺寸比對後開口(中興用)
        private static double SinoOpenSize(bool isInsulation, double radius)
        {
            // 保溫
            if (isInsulation == true)
            {
                if (radius < 15)
                {
                    radius = 80;
                }
                else if (radius >= 15 && radius <= 32)
                {
                    radius = 100;
                }
                else if (radius > 32 && radius <= 80)
                {
                    radius = 150;
                }
                else if (radius > 80 && radius <= 125)
                {
                    radius = 200;
                }
                else if (radius > 125 && radius <= 150)
                {
                    radius = 250;
                }
                else if (radius > 150 && radius <= 200)
                {
                    radius = 300;
                }
                else
                {
                    radius = 500;
                }
            }
            else
            {
                if(radius < 15)
                {
                    radius = 40;
                }
                else if(radius >= 15 && radius < 32)
                {
                    radius = 50;
                }
                else if (radius >= 32 && radius <= 50)
                {
                    radius = 80;
                }
                else if (radius > 50 && radius <= 65)
                {
                    radius = 100;
                }
                else if (radius > 65 && radius <= 80)
                {
                    radius = 125;
                }
                else if (radius > 80 && radius <= 125)
                {
                    radius = 150;
                }
                else if (radius > 125 && radius <= 150)
                {
                    radius = 200;
                }
                else if (radius > 150 && radius <= 200)
                {
                    radius = 250;
                }
                else
                {
                    radius = 300;
                }
            }

            return radius;
        }
        // 放置開口
        private int PlaceOpening(Document doc, CrushElemInfo crushElemInfo, List<FamilySymbol> openFSList, int amount)
        {
            string useFS = string.Empty;
            if (crushElemInfo.hostType.Equals("Wall") || crushElemInfo.hostType.Equals("Beam"))
            {
                if (crushElemInfo.type.Equals("Pipe") || crushElemInfo.type.Equals("PipeFitting"))
                {
                    useFS = "圓形水管牆開口";
                }
                else if (crushElemInfo.type.Equals("Duct") || crushElemInfo.type.Equals("DuctAccessory"))
                {
                    useFS = "矩形風管牆開口";
                }
                else if (crushElemInfo.type.Equals("CableTray") || crushElemInfo.type.Equals("CableTrayFitting"))
                {
                    useFS = "電纜架牆開口";
                }
            }
            else if (crushElemInfo.hostType.Equals("Floor"))
            {
                if (crushElemInfo.type.Equals("Pipe") || crushElemInfo.type.Equals("PipeFitting"))
                {
                    useFS = "圓形水管樓版開口";
                }
                else if (crushElemInfo.type.Equals("Duct") || crushElemInfo.type.Equals("DuctAccessory"))
                {
                    useFS = "矩形風管樓版開口";
                }
                else if (crushElemInfo.type.Equals("CableTray") || crushElemInfo.type.Equals("CableTrayFitting"))
                {
                    useFS = "電纜架樓版開口";
                }
            }
            crushElemInfo.useFS = useFS; // 使用的族群
            // 找到開口與連結所碰觸到Element的Level
            FamilySymbol openFS = (from x in openFSList
                                   where x.FamilyName.Equals(useFS)
                                   select x).FirstOrDefault();
            foreach (XYZ xyz in crushElemInfo.xyzs)
            {
                FamilyInstance pipeOpen = null;
                try
                {
                    // 確認是否有原開口在同座標
                    bool trueOrFalse = false;
                    double xyzX = Math.Round(xyz.X, 8, MidpointRounding.AwayFromZero);
                    double xyzY = Math.Round(xyz.Y, 8, MidpointRounding.AwayFromZero);
                    double xyzZ = Math.Round(xyz.Z, 8, MidpointRounding.AwayFromZero);
                    foreach (XYZ openingXYZ in openingXYZs)
                    {
                        if(Math.Round(openingXYZ.X, 8, MidpointRounding.AwayFromZero).Equals(xyzX) &&
                           Math.Round(openingXYZ.Y, 8, MidpointRounding.AwayFromZero).Equals(xyzY) &&
                           Math.Round(openingXYZ.Z, 8, MidpointRounding.AwayFromZero).Equals(xyzZ))
                        {
                            trueOrFalse = true;
                            break;
                        }
                    }
                    if (trueOrFalse == false)
                    {
                        pipeOpen = doc.Create.NewFamilyInstance(xyz, openFS, crushElemInfo.level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                        crushElemInfo.pipeOpens.Add(pipeOpen); // 儲存所有新增開口
                        newOpeningIds.Add(pipeOpen.Id.IntegerValue);
                        amount++;
                    }
                }
                catch(Exception ex)
                {
                    string str = ex.Message + "\n" + ex.ToString();
                }
            }
            return amount;
        }
        // 旋轉修改開口參數
        private void RotateEditOpening(Document doc, List<OpeningInfo> openingInfoList)
        {
            double a = 0;
            foreach (OpeningInfo openingInfo in openingInfoList)
            {
                foreach (CrushElemInfo crushElemInfo in openingInfo.crushElemInfos)
                {
                    foreach(Element pipeOpen in crushElemInfo.pipeOpens)
                    {
                        try
                        {
                            Parameter editPara = null;
                            if (crushElemInfo.useFS.Equals("圓形水管牆開口"))
                            {
                                editPara = pipeOpen.LookupParameter("水管直徑");
                                editPara.Set(crushElemInfo.size);
                                editPara = pipeOpen.LookupParameter("指定圓形套管直徑");
                                editPara.Set(crushElemInfo.diameter);
                                editPara = pipeOpen.LookupParameter("牆厚度");
                                editPara.Set(crushElemInfo.thickness);
                                editPara = pipeOpen.LookupParameter("圓形牆開口流水號");
                                editPara.Set(crushElemInfo.number);
                                ElementTransformUtils.RotateElement(doc, pipeOpen.Id, crushElemInfo.axis, crushElemInfo.pipeAngle * Math.PI / 180);
                            }
                            else if (crushElemInfo.useFS.Equals("圓形水管樓版開口"))
                            {
                                editPara = pipeOpen.LookupParameter("水管直徑");
                                editPara.Set(crushElemInfo.size);
                                editPara = pipeOpen.LookupParameter("指定圓形套管直徑");
                                editPara.Set(crushElemInfo.diameter);
                                editPara = pipeOpen.LookupParameter("樓版厚度");
                                editPara.Set(crushElemInfo.thickness);
                                editPara = pipeOpen.LookupParameter("圓形牆開口流水號");
                                editPara.Set(crushElemInfo.number);
                            }
                            else if (crushElemInfo.useFS.Equals("矩形風管牆開口"))
                            {
                                editPara = pipeOpen.LookupParameter("風管高度");
                                editPara.Set(crushElemInfo.ductHeight);
                                editPara = pipeOpen.LookupParameter("風管寬度");
                                editPara.Set(crushElemInfo.ductWight);
                                editPara = pipeOpen.LookupParameter("牆厚度");
                                editPara.Set(crushElemInfo.thickness);
                                editPara = pipeOpen.LookupParameter("矩形牆開口流水號");
                                editPara.Set(crushElemInfo.number);
                                ElementTransformUtils.RotateElement(doc, pipeOpen.Id, crushElemInfo.axis, crushElemInfo.pipeAngle * Math.PI / 180);
                            }
                            else if (crushElemInfo.useFS.Equals("矩形風管樓版開口"))
                            {
                                editPara = pipeOpen.LookupParameter("風管高度");
                                editPara.Set(crushElemInfo.ductHeight);
                                editPara = pipeOpen.LookupParameter("風管寬度");
                                editPara.Set(crushElemInfo.ductWight);
                                editPara = pipeOpen.LookupParameter("牆厚度");
                                editPara.Set(crushElemInfo.thickness);
                                editPara = pipeOpen.LookupParameter("矩形牆開口流水號");
                                editPara.Set(crushElemInfo.number);
                                //// 查詢開口與元件的旋轉角度
                                //double angle = OpeningRotate(doc, crushElemInfo);
                                //ElementTransformUtils.RotateElement(doc, pipeOpen.Id, crushElemInfo.axis, angle * Math.PI / 180);
                            }
                            else if (crushElemInfo.useFS.Equals("電纜架牆開口"))
                            {
                                editPara = pipeOpen.LookupParameter("電纜架高度");
                                editPara.Set(crushElemInfo.ductHeight);
                                editPara = pipeOpen.LookupParameter("電纜架寬度");
                                editPara.Set(crushElemInfo.ductWight);
                                editPara = pipeOpen.LookupParameter("牆厚度");
                                editPara.Set(crushElemInfo.thickness);
                                editPara = pipeOpen.LookupParameter("矩形牆開口流水號");
                                editPara.Set(crushElemInfo.number);
                                ElementTransformUtils.RotateElement(doc, pipeOpen.Id, crushElemInfo.axis, crushElemInfo.pipeAngle * Math.PI / 180);
                            }
                            else if (crushElemInfo.useFS.Equals("電纜架樓版開口"))
                            {
                                editPara = pipeOpen.LookupParameter("電纜架高度");
                                editPara.Set(crushElemInfo.ductHeight);
                                editPara = pipeOpen.LookupParameter("電纜架寬度");
                                editPara.Set(crushElemInfo.ductWight);
                                editPara = pipeOpen.LookupParameter("版厚度");
                                editPara.Set(crushElemInfo.thickness);
                                editPara = pipeOpen.LookupParameter("矩形牆開口流水號");
                                editPara.Set(crushElemInfo.number);
                            }
                            editPara = pipeOpen.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM); // 偏移
                            editPara.Set(crushElemInfo.deviation);
                            editPara = pipeOpen.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS); // 備註
                            //editPara.Set(crushElemInfo.docName); // 連結的專案名稱
                            editPara.Set(crushElemInfo.docName + "_" + crushElemInfo.pipeOrDuct.Id + "_" + openingInfo.docName + "_" + openingInfo.element.Id.ToString()); // 專案名稱+衝突的元件
                            string floor = pipeOpen.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM).AsValueString();
                            editPara = pipeOpen.LookupParameter("位置");
                            editPara.Set(floor);
                            a++;
                        }
                        catch (Exception ex)
                        {
                            string str = ex.Message + "\n" + ex.ToString();
                        }
                    }
                }
            }
        }
        // 計算底部高程並旋轉
        private void EditBottomElevation(Document doc)
        {
            IList<ElementFilter> pipeDuctFilters = new List<ElementFilter>(); // 清空過濾器  
            ElementCategoryFilter pipeFilter = new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory); // 管道開口
            ElementCategoryFilter ductFilter = new ElementCategoryFilter(BuiltInCategory.OST_DuctAccessory); // 風管開口
            ElementCategoryFilter cableTrayFilter = new ElementCategoryFilter(BuiltInCategory.OST_CableTrayFitting); // 電纜架開口
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
                    // 修改底部高程
                    double offset = opening.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM).AsDouble();
                    Parameter para = null;
                    if (opening.Name.Equals("圓形水管牆開口"))
                    {
                        double height = Convert.ToDouble(opening.LookupParameter("指定圓形套管直徑").AsDouble());
                        double sub = UnitUtils.ConvertFromInternalUnits(offset - (height / 2), DisplayUnitType.DUT_METERS);
                        string value = Math.Round(sub, 2, MidpointRounding.AwayFromZero).ToString();
                        para = opening.LookupParameter("圓形套管底部高程");
                        para.Set(value);
                    }
                    else if(opening.Name.Equals("矩形風管牆開口"))
                    {
                        double height = Convert.ToDouble(opening.LookupParameter("矩形開口高度").AsDouble());
                        double sub = UnitUtils.ConvertFromInternalUnits(offset - (height / 2), DisplayUnitType.DUT_METERS);
                        string value = Math.Round(sub, 2, MidpointRounding.AwayFromZero).ToString();
                        para = opening.LookupParameter("矩形開口底部高程");
                        para.Set(value);
                    }
                    else if (opening.Name.Equals("電纜架牆開口"))
                    {
                        double height = Convert.ToDouble(opening.LookupParameter("矩形開口高度").AsDouble());
                        double sub = UnitUtils.ConvertFromInternalUnits(offset - (height / 2), DisplayUnitType.DUT_METERS);
                        string value = Math.Round(sub, 2, MidpointRounding.AwayFromZero).ToString();
                        para = opening.LookupParameter("矩形開口底部高程");
                        para.Set(value);
                    }
                    else if (opening.Name.Contains("樓版開口"))
                    {
                        string value = "0";
                        para = opening.LookupParameter("矩形開口底部高程");
                        if(para == null)
                        {
                            para = opening.LookupParameter("圓形套管底部高程");
                        }
                        para.Set(value);
                        //// 查詢開口與元件的旋轉角度
                        //LocationPoint lp = OpeningRotate(doc, opening);
                        //LocationPoint fiLP = opening.Location as LocationPoint;
                        //Line axis = Line.CreateBound(fiLP.Point, new XYZ(fiLP.Point.X, fiLP.Point.Y, fiLP.Point.Z + 10));
                        //double angle = lp.Rotation;
                        //ElementTransformUtils.RotateElement(doc, opening.Id, axis, angle);
                    }
                    // 修改專業代碼
                    para = opening.LookupParameter("專業代碼");
                    string comment = opening.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString(); // 備註
                    if(comment.Contains("AD") || comment.Contains("AP") || comment.Contains("EP"))
                    {
                        para.Set("CQ824A-ECS");
                    }
                    else if (comment.Contains("EE"))
                    {
                        para.Set("CQ824A-E");
                    }
                    else if(comment.Contains("DS") || comment.Contains("FP") || comment.Contains("WS"))
                    {
                        para.Set("CQ824A-M");
                    }
                }
                catch (Exception)
                {

                }
            }
        }
        // 取得元件Solid
        private Solid GetSolids(GeometryObject geomObj, Solid solid)
        {
            if (geomObj is Solid)
            {
                solid = (Solid)geomObj;
            }
            if (geomObj is GeometryInstance)
            {
                GeometryElement geomElem = (geomObj as GeometryInstance).GetSymbolGeometry();
                foreach (GeometryObject o in geomElem)
                {
                    solid = GetSolids(o, solid);
                    if (solid.SurfaceArea > 0)
                    {
                        break;
                    }
                }
            }
            else if (geomObj is GeometryElement)
            {
                GeometryElement geomElem2 = (GeometryElement)geomObj;
                foreach (GeometryObject geomObj2 in geomElem2)
                {
                    solid = GetSolids(geomObj2, solid);
                    if (solid.SurfaceArea > 0)
                    {
                        break;
                    }
                }
            }
            return solid;
        }
        // 旋轉角度
        public static double PointRotation(XYZ pointA, XYZ pointB)
        {
            XYZ pA = new XYZ(pointA.X, pointA.Y, 0);
            XYZ pB = new XYZ(pointB.X, pointB.Y, 0);
            double Dx = pB.X - pA.X;
            double Dy = pB.Y - pA.Y;
            double DRoation = Math.Atan2(Dy, Dx);
            double WRotation = DRoation / Math.PI * 180;

            return WRotation;
        }
        // 關閉警示視窗
        public class MyPreProcessor : IFailuresPreprocessor
        {
            FailureProcessingResult IFailuresPreprocessor.PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                String transactionName = failuresAccessor.GetTransactionName();
                IList<FailureMessageAccessor> fmas = failuresAccessor.GetFailureMessages();
                if (fmas.Count == 0)
                {
                    return FailureProcessingResult.Continue;
                }
                if (transactionName.Equals("放置開口") || transactionName.Equals("旋轉修改開口參數"))
                {
                    //foreach (FailureMessageAccessor fma in fmas)
                    //{
                    //    for (int i = 0; i < fma.GetFailingElementIds().Count(); i++)
                    //    {

                    //    }
                    //}
                    //try
                    //{
                    //    //刪除重複元件
                    //    //failuresAccessor.DeleteElements(notRepeatDeleteIds);
                    //}
                    //catch (Exception ex)
                    //{
                    //    string error = ex.Message + "\n" + ex.ToString();
                    //}
                    failuresAccessor.DeleteAllWarnings(); // 刪除錯誤訊息
                }

                return FailureProcessingResult.Continue;
            }
        }
    }
}