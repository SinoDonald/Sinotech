using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Sinotech_2020
{
    [Transaction(TransactionMode.Manual)]
    public class AutoColumn : IExternalCommand
    {
        /// <summary>
        /// 儲存CADLink的名稱、長、寬
        /// </summary>
        public class CADLinkValue
        {
            // CAD Type (PolyLine or Line or Arc or Solid)
            public string type { get; set; }
            // 中心點
            public XYZ center { get; set; }
            // 軸心
            public Line axis { get; set; }
            // 角度
            public double angle { get; set; }
            // 名稱
            public string name { get; set; }
            // 長
            public double length { get; set; }
            // 寬
            public double width { get; set; }
            // 半徑
            public double radius { get; set; }
        }
        public List<CADLinkValue> cadLinkValueList = new List<CADLinkValue>(); // 讀取CAD內所有線條資訊
        public static IEnumerable columnNames = null; // 所有載入的柱名稱
        public static bool trueOrFalse = false; // 視窗是否有正確選擇
        public static string familyName = "M_混凝土-矩形-柱";
        ElementId startFIid = null;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            //TaskDialog.Show("Revit", "Debug");

            //// 點選dwg
            //Reference pickDWG = uidoc.Selection.PickObject(Autodesk.Revit.UI.Selection.ObjectType.Element, new CADLinkTypeSelectionFilter());
            //Element elem = doc.GetElement(pickDWG);
            // 讀取圖面上的dwg圖
            FilteredElementCollector dwgCollector = new FilteredElementCollector(doc, doc.ActiveView.Id);
            ICollection<Element> dwgElems = dwgCollector.OfClass(typeof(ImportInstance)).WhereElementIsNotElementType().ToElements();
            Element elem = dwgElems.FirstOrDefault();
            
            if(elem != null)
            {
                cadLinkValueList = SaveCADLinkValue(uidoc, elem); // 讀取幾何圖形, 儲存所有的CAD連結資訊
                try
                {
                    FamilyInstance familyInstance = FindFamilyInstance(doc); // 找到柱的FamilyInstance
                    if (trueOrFalse == true && familyInstance != null) // 如果有選擇柱類型
                    {
                        CreateFamilySymbol(doc, familyInstance, cadLinkValueList); // 新增FamilySymbol
                        AutoCreateColumns(doc, familyInstance); // 自動翻柱
                        using (Transaction transDel = new Transaction(doc, "刪除起始元件"))
                        {
                            transDel.Start();
                            try
                            {
                                doc.Delete(startFIid); // 刪除起始擺放的FamilyInstance元件
                            }
                            catch (Autodesk.Revit.Exceptions.ArgumentNullException)
                            {

                            }
                            transDel.Commit();
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ioEx)
                {
                    TaskDialog.Show("Revit", "請選擇正確的平面視圖" + "\n" + ioEx.Message);
                }
            }
            else
            {
                TaskDialog.Show("Error", "請連結CAD檔, 並指定「柱」圖層");
            }

            return Result.Succeeded;
        }
        // 找到柱的FamilyInstance
        private FamilyInstance FindFamilyInstance(Document doc)
        {
            FamilyInstance familyInstance = null;
            FamilySymbol columnFS = null;
            columnNames = null;
            List<string> cloumnsName = new List<string>();
            Level level = doc.ActiveView.GenLevel; // 擺放的Level

            // 找到柱與結構柱的所有類型
            ElementCategoryFilter sColumnsFilter = new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns);
            ElementCategoryFilter columnsFilter = new ElementCategoryFilter(BuiltInCategory.OST_Columns);
            LogicalOrFilter logicalFilter = new LogicalOrFilter(sColumnsFilter, columnsFilter);
            FilteredElementCollector sColumnsCollector = new FilteredElementCollector(doc);
            ICollection<FamilySymbol> familySymbols = sColumnsCollector.WherePasses(logicalFilter).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
            // 讀取所有的柱類型, 供使用者選擇
            columnNames = (from x in familySymbols
                           select x.FamilyName).Distinct();
            // 彈跳視窗選擇柱類型
            ColNamesForm colNamesForm = new ColNamesForm();
            colNamesForm.ShowDialog();

            if(trueOrFalse == true && !familyName.Equals("")) // 如果有選擇柱類型
            {
                // 使用者選擇的柱
                columnFS = (from x in familySymbols
                            where x.FamilyName.Equals(familyName)
                            select x).FirstOrDefault();
                using (Transaction transFS = new Transaction(doc, "啟動族群類型"))
                {
                    transFS.Start();
                    // 如果FamilySymbol尚未啟動, 必須啟用才能使用使用
                    if (!columnFS.IsActive)
                    {
                        columnFS.Activate();
                        doc.Regenerate();
                    }
                    familyInstance = doc.Create.NewFamilyInstance(new XYZ(0, 0, 0), columnFS, level, StructuralType.Column);
                    this.startFIid = familyInstance.Id;
                    transFS.Commit();
                }
            }
            return familyInstance;
        }        
        // 新增FamilySymbol
        private static void CreateFamilySymbol(Document doc, FamilyInstance familyInstance, List<CADLinkValue> cadLinkValueList)
        {
            // 獲取相關的Family
            Family columnFamily = familyInstance.Symbol.Family;
            FamilySymbol columnSymbol = familyInstance.Symbol;
            // 獲取Family document
            Document familyDoc = doc.EditFamily(columnFamily);
            FamilyManager familyManager = familyDoc.FamilyManager;
            using (Transaction transFS = new Transaction(familyDoc, "新增類型"))
            {
                transFS.Start();
                List<string> typeList = cadLinkValueList.Select(x => x.type).Distinct().ToList();
                foreach (string type in typeList)
                {
                    List<CADLinkValue> cadLinkValue = cadLinkValueList.Where(x => x.type.Equals(type)).ToList();
                    if (type.Equals("PolyLine") || type.Equals("Line"))
                    {
                        List<string> names = cadLinkValue.Select(x => x.name).Distinct().ToList();
                        foreach (string name in names)
                        {
                            try
                            {
                                string[] nameSplit = name.Replace(" ", "").Replace("mm", "").Split('x');
                                double length = Convert.ToDouble(nameSplit[0]);
                                double width = Convert.ToDouble(nameSplit[1]);
                                string newTypeName = length + " x " + width + "mm";

                                // 新增與編輯FamilyTypes
                                FamilyType newFamilyType = familyManager.NewType(newTypeName);
                                if (newFamilyType != null)
                                {
                                    // 查詢'b'和'h'的參數與設置
                                    FamilyParameter familyParamB = familyManager.get_Parameter("b");
                                    FamilyParameter familyParamH = familyManager.get_Parameter("h");
                                    if (null != familyParamH && null != familyParamB)
                                    {
                                        if (null != familyParamB)
                                        {
                                            double bLength = UnitUtils.ConvertToInternalUnits(length, DisplayUnitType.DUT_MILLIMETERS);
                                            familyManager.Set(familyParamB, bLength);
                                        }
                                        if (null != familyParamH)
                                        {
                                            double hWidth = UnitUtils.ConvertToInternalUnits(width, DisplayUnitType.DUT_MILLIMETERS);
                                            familyManager.Set(familyParamH, hWidth);
                                        }
                                    }
                                }
                
                                familyDoc = doc.EditFamily(columnFamily); // 取得族群編輯                                
                                columnFamily = familyDoc.LoadFamily(doc, new LoadOpts()); // 將更新項目回傳到Revit Document中
                                //UpdateRevitItems(doc, columnFamily, newTypeName, familyInstance);
                            }
                            catch (Autodesk.Revit.Exceptions.ArgumentException) // FamilyType名稱重複
                            {
                                familyDoc = doc.EditFamily(columnFamily); // 取得族群編輯
                                columnFamily = familyDoc.LoadFamily(doc, new LoadOpts()); // 將更新項目回傳到Revit Document中
                            }
                        }
                    }
                }
                transFS.Commit();
            }
        }
        // 自動翻柱
        private void AutoCreateColumns(Document doc, FamilyInstance familyInstance)
        {
            // 執行交易, 將柱放置在視圖上
            using (Transaction trans = new Transaction(doc, "自動翻柱"))
            {
                Level level = doc.ActiveView.GenLevel;
                trans.Start();
                foreach(var value in cadLinkValueList)
                {
                    XYZ center = value.center; // 中心點
                    Line axis = value.axis; // 軸心
                    double angle = value.angle; // 角度
                    try
                    {
                        FilteredElementCollector sColumnsCollector = new FilteredElementCollector(doc);
                        ICollection<FamilySymbol> familySymbols = sColumnsCollector.OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
                        FamilySymbol columnFS = (from x in familySymbols
                                                 where x.FamilyName.Equals(familyName) && x.Name.Equals(value.length + " x " + value.width + "mm")
                                                 select x).FirstOrDefault();
                        //如果FamilySymbol尚未啟動, 必須啟用才能使用使用
                        if (!columnFS.IsActive)
                        {
                            columnFS.Activate();
                            doc.Regenerate();
                        }

                        FamilyInstance colunmFI = doc.Create.NewFamilyInstance(center, columnFS, level, StructuralType.Column); // 生成柱
                        if (angle != 0)
                        {
                            ElementTransformUtils.RotateElement(doc, colunmFI.Id, axis, angle * Math.PI / 180.0); // 旋轉柱
                        }
                    }
                    catch (Exception)
                    {

                    }
                }
                trans.Commit();
            }
        }
        // 更新Revit項目, Family有一個新的類型
        class LoadOpts : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
        // 更新Revit Items
        private static void UpdateRevitItems(Document doc, Family family, string newTypeName, FamilyInstance familyInstance)
        {
            if (null != family)
            {
                // 找到新的Family Type後, 分配給FamilyInstance
                ISet<ElementId> familySymbolIds = family.GetFamilySymbolIds();
                foreach (ElementId id in familySymbolIds)
                {
                    FamilySymbol familySymbol = family.Document.GetElement(id) as FamilySymbol;
                    if ((null != familySymbol) && familySymbol.Name == newTypeName)
                    {
                        using (Transaction changeSymbol = new Transaction(doc, "更換類型配置"))
                        {
                            changeSymbol.Start();
                            familyInstance.Symbol = familySymbol;
                            changeSymbol.Commit();
                        }
                        break;
                    }
                }
            }
        }
        // 旋轉角度
        private static double PointRotation(XYZ p1, XYZ p2)
        {
            XYZ pA = new XYZ(p1.X, p1.Y, 0);
            XYZ pB = new XYZ(p2.X, p2.Y, 0);
            double Dx = pB.X - pA.X;
            double Dy = pB.Y - pA.Y;
            double DRoation = Math.Atan2(Dy, Dx);
            double WRotation = DRoation / Math.PI * 180;

            return WRotation;
        }
        // 僅能點選ImportInstance
        private class CADLinkTypeSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return (elem is ImportInstance);
            }
            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }
        /// <summary>
        /// 讀取幾何圖形, 儲存所有的PolyLine與Arc
        /// </summary>
        /// <param name="uidoc"></param>
        /// <param name="elem"></param>
        /// <returns></returns>
        public static List<CADLinkValue> SaveCADLinkValue(UIDocument uidoc, Element elem)
        {
            List<CADLinkValue> cadLinkValueList = new List<CADLinkValue>(); // 儲存所有CADLink的長寬

            Options opt = new Options();
            opt.View = uidoc.ActiveView;
            opt.ComputeReferences = true;
            GeometryElement geomElem = elem.get_Geometry(opt);
            foreach (GeometryObject geoObj in geomElem)
            {
                if (geoObj is GeometryInstance)
                {
                    GeometryInstance geoIns = geoObj as GeometryInstance;
                    IEnumerator<GeometryObject> geoList = geoIns.GetInstanceGeometry().GetEnumerator();
                    geoList.Reset();
                    while (geoList.MoveNext())
                    {
                        XYZ center = new XYZ(); // 中心點
                        Line axis = null; // 軸心
                        double angle = 0.0; // 角度
                        double radius = 0.0; // 半徑

                        CADLinkValue cadLinkValue = new CADLinkValue();
                        if (geoList.Current is PolyLine || geoList.Current is Line)
                        {
                            PolyLine polyLine = geoList.Current as PolyLine;
                            Line line = geoList.Current as Line;
                            IList<XYZ> xyzList = new List<XYZ>();
                            if (polyLine != null)
                            {
                                xyzList = polyLine.GetCoordinates();
                            }
                            else
                            {
                                xyzList = line.Tessellate();
                            }
                            if (xyzList.Count >= 4)
                            {
                                center = new XYZ((xyzList[0].X + xyzList[1].X + xyzList[2].X + xyzList[3].X) / 4,
                                                    (xyzList[0].Y + xyzList[1].Y + xyzList[2].Y + xyzList[3].Y) / 4,
                                                    (xyzList[0].Z + xyzList[1].Z + xyzList[2].Z + xyzList[3].Z) / 4);
                                axis = Line.CreateBound(center, new XYZ(center.X, center.Y, center.Z + 1)); // 軸心                                
                                angle = PointRotation(xyzList[0], xyzList[1]); // 角度
                            }
                            double length = Math.Round(Line.CreateBound(xyzList[0], xyzList[1]).Length / 0.0328);
                            double width = Math.Round(Line.CreateBound(xyzList[1], xyzList[2]).Length / 0.0328);
                            cadLinkValue.type = "PolyLine"; // Type
                            cadLinkValue.center = center; // 中心點
                            cadLinkValue.axis = axis; // 軸心
                            cadLinkValue.angle = angle; // 角度                                                        
                            cadLinkValue.name = length * 10 + " x " + width * 10 + "mm"; // 名稱                            
                            cadLinkValue.length = length * 10; // 長
                            cadLinkValue.width = width * 10; // 寬
                        }
                        //else if (geoList.Current is Line)
                        //{
                        //    Line line = geoList.Current as Line;
                        //    IList<XYZ> xyzList = line.Tessellate();
                        //    if (xyzList.Count >= 4)
                        //    {
                        //        center = new XYZ((xyzList[0].X + xyzList[1].X + xyzList[2].X + xyzList[3].X) / 4,
                        //                            (xyzList[0].Y + xyzList[1].Y + xyzList[2].Y + xyzList[3].Y) / 4,
                        //                            (xyzList[0].Z + xyzList[1].Z + xyzList[2].Z + xyzList[3].Z) / 4);
                        //        axis = Line.CreateBound(center, new XYZ(center.X, center.Y, center.Z + 1)); // 軸心                                
                        //        angle = PointRotation(xyzList[0], xyzList[1]); // 角度
                        //    }
                        //    double length = Math.Round(Line.CreateBound(xyzList[0], xyzList[1]).Length / 0.0328);
                        //    double width = Math.Round(Line.CreateBound(xyzList[1], xyzList[2]).Length / 0.0328);
                        //    cadLinkValue.type = "Line"; // Type
                        //    cadLinkValue.center = center; // 中心點
                        //    cadLinkValue.axis = axis; // 軸心
                        //    cadLinkValue.angle = angle; // 角度                                                        
                        //    cadLinkValue.name = length * 10 + " x " + width * 10 + "mm"; // 名稱                            
                        //    cadLinkValue.length = length * 10; // 長
                        //    cadLinkValue.width = width * 10; // 寬
                        //}
                        else if (geoList.Current is Arc)
                        {
                            Arc arc = geoList.Current as Arc;
                            center = arc.Center;
                            axis = Line.CreateBound(center, new XYZ(center.X, center.Y, center.Z + 1));
                            angle = 0.0;
                            radius = arc.Radius;
                            cadLinkValue.type = "Arc"; // 型別                            
                            cadLinkValue.axis = axis; // 軸心                            
                            cadLinkValue.angle = angle; // 角度                            
                            cadLinkValue.center = center; // 中心點                            
                            cadLinkValue.name = Math.Round(radius * 2 / 0.032808 * 10) + "mm"; // 名稱                            
                            cadLinkValue.radius = Math.Round(radius, 5); // 半徑
                        }
                        else if (geoList.Current is Solid)
                        {
                            Solid solid = geoList.Current as Solid;
                            if (solid.Faces.Size > 0 && solid.Volume > 0)
                            {
                                cadLinkValue.type = "Solid";
                            }
                        }
                        else if (geoList.Current is Ellipse)
                        {
                            Ellipse ellipse = geoList.Current as Ellipse;
                            cadLinkValue.type = "Ellipse";
                        }
                        else if (geoList.Current is NurbSpline)
                        {
                            NurbSpline nurbSpline = geoList.Current as NurbSpline;
                            cadLinkValue.type = "NurbSpline";
                        }
                        // 如果下一層依然為GeometryInstance, 則return讀取線條資訊 <-- 還沒寫好..
                        else if (geoList.Current is GeometryInstance)
                        {
                            geoIns = geoList.Current as GeometryInstance;
                            if (geoIns != null)
                            {
                                GeometryElement geoElem = geoIns.GetSymbolGeometry();
                                geoList = geoElem.GetEnumerator();
                                geoList.MoveNext();
                                if (geoList is GeometryInstance)
                                {

                                }
                            }
                        }
                        if (cadLinkValue.type != null)
                        {
                            cadLinkValueList.Add(cadLinkValue);
                        }
                    }
                }
            }

            return cadLinkValueList;
        }
    }
}
