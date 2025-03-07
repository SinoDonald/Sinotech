using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyInstanceLock
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LockOne : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            Reference pickOne = uidoc.Selection.PickObject(ObjectType.Element, new FamilyInstanceFilter());
            FamilyInstance familyInstance = doc.GetElement(pickOne) as FamilyInstance;
            FamilySymbol familySymbol = familyInstance.Symbol;

            //Family family = familyInstance.Symbol.Family;            
            //Document familyDoc = doc.EditFamily(family);
            //FamilyManager familyManager = familyDoc.FamilyManager;
            //using (Transaction trans = new Transaction(familyDoc, "元件保護"))
            //{
            //    trans.Start();
            //    string familyPath = @"D:\Donald的檔案\中興工程\專案\SinoStation\Model\族群\獨立式資訊板(SinoBIM-第1版).rfa";
            //    LoadFamily(familyDoc, familyPath);
            //    trans.Commit();
            //}

            List<FamilyInstance> familyInstances = new List<FamilyInstance>();
            try
            {
                familyInstances = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>()
                                  .Where(x => x.Symbol.Family.Id.ToString().Equals(familySymbol.Family.Id.ToString()) && x.Symbol.Id.Equals(familySymbol.Id)).ToList();
                                //.Where(x => RevitAPI.NewElementId(x.Symbol.Family.Id.ToString()).Equals(RevitAPI.NewElementId(familySymbol.Family.Id.ToString())) && x.Symbol.Id.Equals(familySymbol.Id)).ToList();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("獲取同族群元件失敗", ex.Message + "\n" + familySymbol.FamilyName + "：" + familySymbol.Name);
            }

            TransactionGroup tranGrp1 = new TransactionGroup(doc, "元件保護");
            tranGrp1.Start();
            List<Parameter> paraList = new List<Parameter>();
            foreach (Parameter para in familySymbol.Parameters) { paraList.Add(para); }
            //CreateSharedParameter(doc, familySymbol, paraList); // 新增共用參數

            using (Transaction trans = new Transaction(doc, "建立模型"))
            {
                trans.Start();
                if (familySymbol == null) { }
                else
                {
                    DirectShape directShape = DirectShape.CreateElement(doc, familyInstance.Category.Id);
                    directShape.ApplicationId = "Donald";
                    directShape.ApplicationDataId = "Sinotech";

                    List<Solid> solids = GetSolids(doc, familyInstance);
                    List<GeometryObject> resultList = new List<GeometryObject>();
                    foreach (Solid solid in solids) { resultList.Add(solid); }
                    directShape.SetShape(resultList);
                    directShape.Name = doc.GetElement(familySymbol.Id).Name;
                    List<Dimension> dimensionList = GetDimension(doc, familyInstance);
                    foreach(Dimension dimension in dimensionList) { }
                    doc.Delete(familyInstance.Id);
                    //SetParameterFromOriginalElement(directShape, familyInstance); // 修改參數
                    //SetPropertyValueFromParameters(directShape, familySymbol, paraList);
                }
                trans.Commit();
            }

            tranGrp1.Assimilate();

            return Result.Succeeded;
        }
        public class FamilyLoadOptions : IFamilyLoadOptions
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
        public void LoadFamily(Document doc, string familyPath)
        {
            Family loadedFamily = null;
            var success = doc.LoadFamily(familyPath, new FamilyLoadOptions(), out loadedFamily);
            if (!success)
            {
                throw new Exception("Failed to load family.");
            }
        }

        // 新增共用參數
        private void CreateSharedParameter(Document doc, FamilySymbol familySymbol, List<Parameter> paraList)
        {
            string directoryName = Path.GetDirectoryName(doc.PathName);
            doc.Application.SharedParametersFilename = Path.Combine(directoryName, "SinotechSharedParameter.txt");
            using (Transaction trans = new Transaction(doc, "新增共用參數"))
            {
                trans.Start();
                DefinitionFile definitionFile = doc.Application.OpenSharedParameterFile();
                if (definitionFile == null)
                {
                    if (File.Exists(doc.Application.SharedParametersFilename))
                    {
                        File.Delete(doc.Application.SharedParametersFilename);
                    }
                    File.Create(doc.Application.SharedParametersFilename).Close();
                    definitionFile = doc.Application.OpenSharedParameterFile();
                }
                DefinitionGroup definitionGroup = definitionFile.Groups.get_Item("Sinotech");
                if (definitionGroup == null)
                {
                    definitionGroup = definitionFile.Groups.Create("Sinotech");
                }
                foreach (Parameter para in paraList)
                {
                    string text = para.Definition.Name.Contains("Type") ? ("_" + para.Definition.Name) : (familySymbol.FamilyName + "_" + para.Definition.Name);
                    if (definitionGroup.Definitions.get_Item(text) == null)
                    {
                        //ExternalDefinitionCreationOptions externalDefinitionCreationOptions = RevitAPI.GetExternalDefinitionOptions(text, para);
                        //ExternalDefinitionCreationOptions externalDefinitionCreationOptions = new ExternalDefinitionCreationOptions(text, para.Definition.ParameterType); // 2020
                        ForgeTypeId forgeTypeId = para.Definition.GetDataType();
                        ExternalDefinitionCreationOptions externalDefinitionCreationOptions = new ExternalDefinitionCreationOptions(text, forgeTypeId); // 2024
                        try { definitionGroup.Definitions.Create(externalDefinitionCreationOptions); }
                        catch(Autodesk.Revit.Exceptions.InvalidOperationException ex) { string error = ex.Message + "\n" + ex.ToString(); }
                    }
                    Definition definition = definitionGroup.Definitions.get_Item(text);
                    Category category = familySymbol.Category;
                    CategorySet categorySet = doc.Application.Create.NewCategorySet();
                    DefinitionBindingMapIterator definitionBindingMapIterator = doc.ParameterBindings.ForwardIterator();
                    while (definitionBindingMapIterator.MoveNext())
                    {
                        Definition key = definitionBindingMapIterator.Key;
                        ElementBinding elementBinding = (ElementBinding)definitionBindingMapIterator.Current;
                        if (text == key.Name)
                        {
                            IEnumerator enumerator2 = elementBinding.Categories.GetEnumerator();
                            //using (IEnumerator enumerator2 = elementBinding.Categories.GetEnumerator())
                            //{
                            while (enumerator2.MoveNext())
                            {
                                Category category2 = (Category)enumerator2.Current;
                                categorySet.Insert(category2);
                            }
                            break;
                            //}
                        }
                    }
                    categorySet.Insert(category);
                    InstanceBinding instanceBinding = doc.Application.Create.NewInstanceBinding(categorySet);
                    if (categorySet.Size > 1)
                    {
                        doc.ParameterBindings.ReInsert(definition, instanceBinding);
                    }
                    else
                    {
                        doc.ParameterBindings.Insert(definition, instanceBinding);
                    }
                }
                trans.Commit();
            }
        }
        // 修改參數
        public bool SetParameterFromOriginalElement(Element newElem, Element originalElem)
        {
            bool result;
            try
            {
                foreach (Parameter parameter in originalElem.Parameters)
                {
                    Parameter para = newElem.LookupParameter(parameter.Definition.Name);
                    if (para != null && !para.IsReadOnly)
                    {
                        if (parameter.StorageType == StorageType.Double) { para.Set(parameter.AsDouble()); }
                        else if (parameter.StorageType == StorageType.ElementId) { para.Set(parameter.AsElementId()); }
                        else if (parameter.StorageType == StorageType.Integer) { para.Set(parameter.AsInteger()); }
                        else if (parameter.StorageType == StorageType.String) { para.Set(parameter.AsString()); }
                    }
                }
                result = true;
            }
            catch (Exception ex) { string error = "修改參數失敗" + ex.Message + "\n" + ex.ToString(); result = false; }
            return result;
        }
        // 修改族群參數
        public bool SetPropertyValueFromParameters(Element newElem, FamilySymbol familySymbol, List<Parameter> paraList)
        {
            bool result;
            try
            {
                foreach (Parameter parameter in paraList)
                {
                    Parameter para = newElem.LookupParameter(parameter.Definition.Name.Contains("Type") ? ("_" + parameter.Definition.Name) : (familySymbol.FamilyName + "_" + parameter.Definition.Name));
                    if (para != null && !para.IsReadOnly)
                    {
                        if (parameter.StorageType == StorageType.Double) { para.Set(parameter.AsDouble()); }
                        else if (parameter.StorageType == StorageType.ElementId) { para.Set(parameter.AsElementId()); }
                        else if (parameter.StorageType == StorageType.Integer) { para.Set(parameter.AsInteger()); }
                        else if (parameter.StorageType == StorageType.String) { para.Set(parameter.AsString()); }
                    }
                }
                result = true;
            }
            catch (Exception ex) { string error = "修改參數失敗" + ex.Message + "\n" + ex.ToString(); result = false; }
            return result;
        }
        /// <summary>
        /// 儲存所有元件的Solid
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="elem"></param>
        /// <returns></returns>
        private List<Solid> GetSolids(Document doc, Element elem)
        {
            List<Solid> solidList = new List<Solid>();

            // 1.讀取Geometry Option
            Options options = new Options();
            //options.View = doc.GetElement(room.Level.FindAssociatedPlanViewId()) as Autodesk.Revit.DB.View;
            options.DetailLevel = ((doc.ActiveView != null) ? doc.ActiveView.DetailLevel : ViewDetailLevel.Medium);
            options.ComputeReferences = true;
            options.IncludeNonVisibleObjects = true;
            // 得到幾何元素
            GeometryElement geomElem = elem.get_Geometry(options);
            List<Solid> solids = GeometrySolids(geomElem);
            foreach (Solid solid in solids)
            {
                solidList.Add(solid);
            }

            return solidList;
        }
        /// <summary>
        /// 取得元件的Solid
        /// </summary>
        /// <param name="geoObj"></param>
        /// <returns></returns>
        private List<Solid> GeometrySolids(GeometryObject geoObj)
        {
            List<Solid> solids = new List<Solid>();
            if (geoObj is Solid)
            {
                Solid solid = (Solid)geoObj;
                if (solid.Faces.Size > 0/* && solid.Volume > 0*/) { solids.Add(solid); }
            }
            if (geoObj is GeometryInstance)
            {
                GeometryInstance geoIns = geoObj as GeometryInstance;
                GeometryElement geometryElement = (geoObj as GeometryInstance).GetSymbolGeometry(geoIns.Transform); // 座標轉換
                foreach (GeometryObject o in geometryElement) { solids.AddRange(GeometrySolids(o)); }
            }
            else if (geoObj is GeometryElement)
            {
                GeometryElement geometryElement2 = (GeometryElement)geoObj;
                foreach (GeometryObject o in geometryElement2) { solids.AddRange(GeometrySolids(o)); }
            }
            return solids;
        }

        public List<Dimension> GetDimension(Document doc, Element selectedElem)
        {
            List<Dimension> dimensions = new List<Dimension>();
            List<Dimension> dimensionList = new FilteredElementCollector(doc).OfClass(typeof(Dimension)).Cast<Dimension>().ToList<Dimension>();
            try
            {
                foreach (Dimension dimension in dimensionList)
                {
                    IEnumerator enumerator2 = dimension.References.GetEnumerator();
                    //using (IEnumerator enumerator2 = current.References.GetEnumerator())
                    //{
                    while (enumerator2.MoveNext())
                    {
                        if (((Reference)enumerator2.Current).ElementId.ToString() == selectedElem.Id.ToString())
                        //if (((Reference)enumerator2.Current).ElementId.IntegerValue == selectedElem.Id.IntegerValue)
                        //if (RevitAPI.NewElementId(((Reference)enumerator2.Current).ElementId.ToString()) == RevitAPI.NewElementId(selectedElem.Id.ToString()))
                        {
                            dimensions.Add(dimension);
                            break;
                        }
                    }
                    //}
                }
            }
            catch (Exception ex) {string error = "抓取關聯尺寸標註失敗" + "\n" + ex.Message + "\n" + ex.ToString(); }
            return dimensions;
        }
        /// <summary>
        /// 僅能點選FamilyInstance
        /// </summary>
        private class FamilyInstanceFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return (elem is FamilyInstance);
            }
            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }
    }
}