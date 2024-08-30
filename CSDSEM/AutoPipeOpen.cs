using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CSDSEM
{
    [Transaction(TransactionMode.Manual)]
    class AutoPipeOpen : IExternalCommand
    {
        private class BeamsWallsPipes
        {
            public ElementId beamWallId { get; set; } // 樑牆Id
            public List<List<Solid>> insSolidList = new List<List<Solid>>(); // 儲存Instance Solid
            public List<List<Solid>> symSolidList = new List<List<Solid>>(); // 儲存Symbol Solid
            public double beamWallAngle { get; set; } // 樑牆旋轉的角度
            public ElementId pipeId { get; set; } // 管
            public List<Face> insFace = new List<Face>(); // Instance面
            public List<Face> symFace = new List<Face>(); // Symbol面
            public List<XYZ> intersectXYZ = new List<XYZ>(); // 樑牆的交集點
            public double outerDiameter { get; set; } // 外徑_double
            public string outerDiameterString { get; set; } // 外徑_string            
            public double pipeLength { get; set; } // 與樑交錯的管道長度
            public double pipeAngle { get; set; } // 管角度
            public XYZ vector { get; set; } // 向量
            public ElementId pipeOpenId { get; set; } // 開口
        }
        List<BeamsWallsPipes> BWPList = new List<BeamsWallsPipes>(); // 自動開口
        List<BeamsWallsPipes> newBWPList = new List<BeamsWallsPipes>(); // 交集到外參管道後的自動開口資訊

        private static List<Face> IntersectFaces = new List<Face>(); // 樑與管交集的面
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
            Document doc = uidoc.Document;

            BWPList = new List<BeamsWallsPipes>(); // 自動開口(重新執行時清空)
            newBWPList = new List<BeamsWallsPipes>(); // 自動開口(重新執行時清空)

            // 彈跳視窗, 選擇欲過濾的品類
            PipeOpeningForm pipeOpeningForm = new PipeOpeningForm();
            pipeOpeningForm.ShowDialog();
            IList<string> filteredList = pipeOpeningForm.FilteredList;
            if (filteredList.Count != 0)
            {
                // 找到文檔中所有的樑牆
                ElementCategoryFilter beamsFilter = new ElementCategoryFilter(BuiltInCategory.OST_StructuralFraming); // 過濾樑
                ElementCategoryFilter wallsFilter = new ElementCategoryFilter(BuiltInCategory.OST_Walls); // 過濾牆
                IList<Element> beamsOrWalls = null;
                if (filteredList.Count.Equals(2))
                {
                    LogicalOrFilter wallsAndBeamsFilter = new LogicalOrFilter(wallsFilter, beamsFilter);
                    beamsOrWalls = new FilteredElementCollector(doc, doc.ActiveView.Id).WherePasses(wallsAndBeamsFilter).WhereElementIsNotElementType().ToElements();
                }
                else if(filteredList.Count.Equals(1) && filteredList[0].Equals("樑"))
                {
                    beamsOrWalls = new FilteredElementCollector(doc, doc.ActiveView.Id).WherePasses(beamsFilter).WhereElementIsNotElementType().ToElements();
                }
                else if (filteredList.Count.Equals(1) && filteredList[0].Equals("牆"))
                {
                    beamsOrWalls = new FilteredElementCollector(doc, doc.ActiveView.Id).WherePasses(wallsFilter).WhereElementIsNotElementType().ToElements();
                }
                foreach (Element beamOrWall in beamsOrWalls)
                {
                    try
                    {
                        BeamsWallsPipes BWP = new BeamsWallsPipes();
                        BWP.beamWallId = beamOrWall.Id; // 樑牆Id
                        LocationCurve lc = beamOrWall.Location as LocationCurve;
                        Line line = lc.Curve as Line;
                        BWP.vector = line.Direction; // 向量
                        double beamWallAngle = PointRotation(line.Tessellate()[0], line.Tessellate()[1]);
                        BWP.beamWallAngle = beamWallAngle; // 樑牆旋轉的角度

                        Options opt = new Options();
                        opt.ComputeReferences = true;
                        opt.DetailLevel = doc.ActiveView.DetailLevel;
                        GeometryElement geomElem = beamOrWall.get_Geometry(opt);
                        // 儲存當前專案所有樑牆的Solid
                        foreach (GeometryObject geomObj in geomElem)
                        {
                            List<Solid> insSolids = null;
                            List<Solid> symSolids = null;
                            insSolids = GetSolids(geomObj);
                            symSolids = GetSymbolSolids(geomObj);
                            if (!insSolids.Count.Equals(0) && !symSolids.Count.Equals(0))
                            {
                                BWP.insSolidList.Add(insSolids);  // 儲存Instance Solid
                                BWP.symSolidList.Add(symSolids);  // 儲存Symbol Solid
                                BWPList.Add(BWP);
                            }
                        }
                    }
                    catch(NullReferenceException)
                    {

                    }                    
                }

                // 查詢外部連結交集到Solid的管道
                ElementClassFilter revitLinkInsFilter = new ElementClassFilter(typeof(RevitLinkInstance));
                FilteredElementCollector revitLinkInsColl = new FilteredElementCollector(doc);
                List<RevitLinkInstance> revitLinks = revitLinkInsColl.WherePasses(revitLinkInsFilter).Cast<RevitLinkInstance>().ToList();
                foreach (BeamsWallsPipes BWP in BWPList)
                {
                    int i = 0;
                    foreach (List<Solid> insSolids in BWP.insSolidList)
                    {
                        List<Solid> symSolids = BWP.symSolidList[i];
                        int j = 0;
                        foreach (Solid insSolid in insSolids)
                        {
                            Solid symSolid = symSolids[j];
                            GetIntersectingLinkedElementIds(insSolid, symSolid, revitLinks, BWP); // 取得外部參考Solid交錯的管道
                            j++;
                        }
                        i++;
                    }
                }

                string info = "自動開口完成";
                using (Transaction trans = new Transaction(doc, "自動開口"))
                {
                    trans.Start();
                    FamilySymbol fs = FindFS(doc); // 找到FamilySymbol
                    foreach (BeamsWallsPipes BWP in newBWPList)
                    {
                        FamilyInstance pipeOpen = null;
                        try
                        {
                            pipeOpen = doc.Create.NewFamilyInstance(BWP.symFace[0], BWP.intersectXYZ[0], BWP.vector, fs);
                            BWP.pipeOpenId = pipeOpen.Id;
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            info = "部分管道未全數穿過樑或牆";
                        }
                        catch (Autodesk.Revit.Exceptions.ArgumentNullException)
                        {
                            info = "未正確載入自動開口族群";
                        }
                        catch (Exception ex)
                        {
                            info = ex.Message + "\n" + ex.ToString();
                        }
                    }
                    if (newBWPList.Count > 0)
                    {
                        // 修改開口角度、長度、直徑
                        EditOpenAngle(doc, newBWPList);
                    }
                    trans.Commit();
                }

                TaskDialog.Show("Revit", info);
            }             

            return Result.Succeeded;
        }
        // 取得幾何圖形, Instance Solids(修改過Instance)
        public static List<Solid> GetSolids(GeometryObject geomObj)
        {
            List<Solid> solids = new List<Solid>();
            if (geomObj is Solid)
            {
                Solid solid = (Solid)geomObj;
                if (solid.Faces.Size > 0 && solid.Volume > 0)
                {
                    solids.Add((Solid)geomObj);
                }
            }
            else if (geomObj is GeometryInstance)
            {
                GeometryElement geometryElement = (geomObj as GeometryInstance).GetInstanceGeometry();
                foreach (GeometryObject o in geometryElement)
                {
                    solids.AddRange(GetSolids(o));
                    //GetSolids(o);
                }
            }
            else if (geomObj is GeometryElement)
            {
                GeometryElement geometryElement2 = (GeometryElement)geomObj;
                foreach (GeometryObject o in geometryElement2)
                {
                    solids.AddRange(GetSolids(o));
                    //GetSolids(o);
                }
            }
            return solids;
        }
        // 取得幾何圖形, Symbol Solids(未修改過Instance)
        public static List<Solid> GetSymbolSolids(GeometryObject geomObj)
        {
            List<Solid> solids = new List<Solid>();
            if (geomObj is Solid)
            {
                Solid solid = (Solid)geomObj;
                if (solid.Faces.Size > 0 && solid.Volume > 0)
                {
                    solids.Add((Solid)geomObj);
                }
            }
            if (geomObj is GeometryInstance)
            {
                GeometryElement geomElem = (geomObj as GeometryInstance).GetSymbolGeometry();
                foreach (GeometryObject o in geomElem)
                {
                    solids.AddRange(GetSymbolSolids(o));
                    //GetSymbolSolids(o);
                }
            }
            else if (geomObj is GeometryElement)
            {
                GeometryElement geomElem2 = (GeometryElement)geomObj;
                foreach (GeometryObject geomObj2 in geomElem2)
                {
                    solids.AddRange(GetSymbolSolids(geomObj2));
                    //GetSymbolSolids(geomObj2);
                }
            }
            return solids;
        }
        // 取得外部參考Solid交錯的管道
        private void GetIntersectingLinkedElementIds(Solid insSolid, Solid symSolid, IList<RevitLinkInstance> links, BeamsWallsPipes BWP)
        {
            foreach (RevitLinkInstance i in links)
            {
                try
                {
                    ElementIntersectsSolidFilter intersectSolidFilter = new ElementIntersectsSolidFilter(insSolid);
                    FilteredElementCollector pipeColl = new FilteredElementCollector(i.GetLinkDocument());
                    IList<Element> elems = pipeColl.WherePasses(intersectSolidFilter).WhereElementIsNotElementType().ToElements();
                    foreach (Element elem in elems)
                    {
                        if (elem is Pipe)
                        {
                            Pipe pipe = elem as Pipe;
                            BeamsWallsPipes newBWP = new BeamsWallsPipes();
                            Curve pipeCurve = (pipe.Location as LocationCurve).Curve;
                            double pipeAngle = PointRotation(pipeCurve.Tessellate()[0], pipeCurve.Tessellate()[1]); // 計算管道旋轉角度
                            Parameter para = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER);// 管道的"外徑"
                            double outerDiameter = para.AsDouble();
                            string outerDiameterString = para.AsValueString();

                            newBWP.beamWallId = BWP.beamWallId; // 樑牆Id
                            newBWP.beamWallAngle = BWP.beamWallAngle; // 樑牆旋轉的角度
                            newBWP.insSolidList = BWP.insSolidList;  // 儲存Instance Solid
                            newBWP.symSolidList = BWP.symSolidList;  // 儲存Symbol Solid
                            newBWP = FindFaceIntersectLine(insSolid, symSolid, pipeCurve, BWP, newBWP); // 找到線與面交集點
                            newBWP.pipeId = pipe.Id; // 管
                            newBWP.pipeAngle = pipeAngle; // 角度
                            newBWP.outerDiameter = outerDiameter; // 外徑_double
                            newBWP.outerDiameterString = outerDiameterString; // 外徑_string
                            newBWPList.Add(newBWP);
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.ArgumentNullException)
                {

                }
            }
        }
        // 找到線與面交集點
        private BeamsWallsPipes FindFaceIntersectLine(Solid insSolid, Solid symSolid, Curve curve, BeamsWallsPipes BWP, BeamsWallsPipes newBWP)
        {
            List<XYZ> xyzs = new List<XYZ>();
            IntersectFaces = new List<Face>();
            int i = 0;
            foreach (Face insFace in insSolid.Faces)
            {
                int j = 0;
                Face symFace = null;
                foreach (Face face in symSolid.Faces)
                {
                    if (j.Equals(i))
                    {
                        symFace = face;
                        break;
                    }
                    j++;
                }
                // 設置交集結果
                IntersectionResultArray intersectionR = new IntersectionResultArray();
                // 比較面與曲線的交集結果
                SetComparisonResult comparisonR = insFace.Intersect(curve, out intersectionR);
                // 設置交集點
                XYZ intersectionResult = null;

                // 相交
                if (SetComparisonResult.Disjoint != comparisonR)
                {
                    try
                    {
                        if (!intersectionR.IsEmpty)
                        {
                            IntersectFaces.Add(insFace);
                            intersectionResult = intersectionR.get_Item(0).XYZPoint;
                            xyzs.Add(intersectionResult);

                            // 儲存所有交集資訊
                            newBWP.intersectXYZ.Add(intersectionResult); // 樑牆的交集點
                            PlanarFace insPF = insFace as PlanarFace;
                            PlanarFace symPF = symFace as PlanarFace;
                            //newBWP.vector = insPF.XVector; // 向量
                            newBWP.vector = BWP.vector; // 向量
                            if (insPF != null)
                            {
                                newBWP.insFace.Add(insPF); // Instance面
                                newBWP.symFace.Add(symPF); // Symbol面
                            }
                            if (xyzs.Count >= 2)
                            {
                                Line line = Line.CreateBound(new XYZ(xyzs[0].X, xyzs[0].Y, xyzs[0].Z), new XYZ(xyzs[1].X, xyzs[1].Y, xyzs[1].Z));
                                newBWP.pipeLength = line.Length; // 與樑交錯的管道長度
                            }
                        }
                    }
                    catch (NullReferenceException)
                    {

                    }
                }
                i++;
            }

            return newBWP;
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
        // 找到FamilySymbol
        private FamilySymbol FindFS(Document doc)
        {
            // 找到套管FamilySymbol
            IList<FamilySymbol> familySymbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
            FamilySymbol fs = (from x in familySymbols
                               where x.Name.Equals("自動開口") && x.FamilyName.Equals("自動開口")
                               select x).FirstOrDefault();
            // 如果FamilySymbol尚未啟動, 必須啟用才能使用
            if (fs != null)
            {
                if (!fs.IsActive)
                {
                    fs.Activate();
                    doc.Regenerate();
                }
            }

            return fs;
        }
        // 修改開口角度、長度、直徑
        private void EditOpenAngle(Document doc, List<BeamsWallsPipes> beamsWallsPipesList)
        {
            // 讀取圖面上所有的開口(一般模型)
            ICollection<FamilyInstance> pipeOpens = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_GenericModel)
                                                    .WhereElementIsNotElementType().Cast<FamilyInstance>().ToList();
            // 修改開口角度為90 與 長度=管與樑交錯距離*3
            foreach (BeamsWallsPipes bwpValue in beamsWallsPipesList)
            {
                FamilyInstance pipeOpen = (from x in pipeOpens
                                           where x.Id.Equals(bwpValue.pipeOpenId)
                                           select x).FirstOrDefault();
                if (pipeOpen is FamilyInstance && pipeOpen != null)
                {
                    try
                    {
                        Parameter para = pipeOpen.LookupParameter("傾斜角度");
                        para.Set((bwpValue.pipeAngle - bwpValue.beamWallAngle) / (180 / Math.PI));
                        para = pipeOpen.LookupParameter("管直徑");
                        para.Set(bwpValue.outerDiameter);
                        para = pipeOpen.LookupParameter("開口長度");
                        para.Set(bwpValue.pipeLength * 3); // 公分轉換英呎需 * 30.4801
                        para = pipeOpen.LookupParameter("開孔放大距離");
                        // 單位轉換
                        double diameter = RevitAPI.ConvertFromInternalUnits(bwpValue.outerDiameter, "millimeters");
                        double openSize = OpenSize(diameter);
                        double openingDistance = RevitAPI.ConvertToInternalUnits((openSize - diameter), "millimeters") / 2;
                        para.Set(openingDistance);
                    }
                    catch (NullReferenceException)
                    {

                    }
                }
            }
        }
        // 尺寸比對後開口
        private double OpenSize(double radius)
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
    }
}
