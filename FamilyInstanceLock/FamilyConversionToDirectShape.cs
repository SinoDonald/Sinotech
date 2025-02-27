using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FamilyInstanceLock
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FamilyConversionToDirectShape : IExternalEventHandler
    {
        public void Execute(UIApplication uiapp)
        {
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            TransactionGroup tranGrp1 = new TransactionGroup(doc, "元件保護");
            tranGrp1.Start();

            int count = 0;
            List<string> familyNames = new List<string>();
            using (Transaction trans = new Transaction(doc, "建立模型"))
            {
                List<FamilyInstance> chooseFamilys = ChooseElems.chooseFamilys;
                List<ElementId> familySymbolIds = new List<ElementId>();
                // 新增共用參數
                familyNames = chooseFamilys.Select(x => x.Symbol.FamilyName).Distinct().ToList();
                List<string> containWords = new List<string>() { "長度", "管線" };
                List<Parameter> paraList = new List<Parameter>();

                string templatePath = @"E:\Donald的檔案\中興工程\測試檔案\公制通用模型.rft";
                foreach (string familyName in familyNames)
                {
                    string tempFamilyPath = Path.Combine(@"E:\Donald的檔案\中興工程\測試檔案\", familyName + ".rfa");
                    try
                    {
                        Document familyDoc = doc.Application.NewFamilyDocument(templatePath);
                        FamilyInstance familyInstance = chooseFamilys.Where(x => x.Symbol.FamilyName.Equals(familyName)).FirstOrDefault();
                        List<GeometryObject> resultList = new List<GeometryObject>();
                        List<Solid> solids = GetSolids(doc, familyInstance);
                        foreach (Solid solid in solids) { resultList.Add(solid); }
                        List<ElementId> subComponentIds = familyInstance.GetSubComponentIds().ToList();
                        foreach (ElementId subComponentId in subComponentIds)
                        {
                            Element subElem = doc.GetElement(subComponentId);
                            List<Solid> subSolids = GetSolids(doc, subElem);
                            foreach (Solid subSolid in subSolids) { resultList.Add(subSolid); }
                        }
                        //using (Transaction famTrans = new Transaction(familyDoc, "創建幾何體"))
                        //{
                        //    famTrans.Start();
                        //    foreach (Solid solid in resultList) { FreeFormElement.Create(familyDoc, solid); }
                        //    famTrans.Commit();
                        //}
                        //// 另存新檔
                        //SaveAsOptions saveOpt = new SaveAsOptions();
                        //saveOpt.OverwriteExistingFile = true;
                        //familyDoc.SaveAs(tempFamilyPath, saveOpt);
                        //familyDoc.Close();

                        CreateNewFamily(doc, resultList, familyName, out Family newFamily); // 建立相同的族群
                        string test = "Test";
                        //foreach (Parameter para in familyInstances.Parameters)
                        //{                   
                        //    if (containWords.Where(x => para.Definition.Name.Contains(x)).FirstOrDefault() != null)
                        //    {
                        //        paraList.Add(para); 
                        //    }
                        //}
                        //paraList = paraList.OrderBy(x => x.Definition.Name).ToList();
                        //List<string> paraNames = paraList.Select(x => x.Definition.Name).ToList();
                        //CreateSharedParameter(doc, familyInstances.Symbol, paraList);
                    }
                    catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                }
                trans.Start();
                foreach (FamilyInstance chooseFamily in chooseFamilys)
                {
                    try
                    {
                        DirectShape directShape = DirectShape.CreateElement(doc, chooseFamily.Category.Id);
                        directShape.ApplicationDataId = "Sinotech";
                        //directShape.ApplicationId = "Donald";
                        directShape.ApplicationId = chooseFamily.Id.ToString();

                        List<GeometryObject> resultList = new List<GeometryObject>();
                        List<Solid> solids = GetSolids(doc, chooseFamily);
                        foreach (Solid solid in solids) { resultList.Add(solid); }
                        List<ElementId> subComponentIds = chooseFamily.GetSubComponentIds().ToList();
                        foreach (ElementId subComponentId in subComponentIds)
                        {
                            Element subElem = doc.GetElement(subComponentId);
                            List<Solid> subSolids = GetSolids(doc, subElem);
                            foreach (Solid subSolid in subSolids) { resultList.Add(subSolid); }
                        }
                        directShape.SetShape(resultList);
                        directShape.Name = doc.GetElement(chooseFamily.Symbol.Id).Name;
                        //SetParameterFromOriginalElement(directShape, chooseFamily); // 修改參數
                        //SetPropertyValueFromParameters(directShape, chooseFamily.Symbol, paraList);
                        List<Dimension> dimensionList = GetDimension(doc, chooseFamily); 
                        familySymbolIds.Add(chooseFamily.Symbol.Id);
                        //doc.Delete(chooseFamily.Id);
                        count++;
                    }
                    catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                }
                //// 移除未使用的元件
                //familySymbolIds = familySymbolIds.Distinct().ToList();
                //foreach (ElementId familySymbolId in familySymbolIds) { doc.Delete(familySymbolId); }
                trans.Commit();
            }
            TaskDialog.Show("Revit", "成功鎖定 " + count + " 個元件。");

            tranGrp1.Assimilate();
        }
        /// <summary>
        /// 建立相同的族群
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="resultList"></param>
        /// <param name="familyName"></param>
        /// <param name="newFamily"></param>
        /// <returns></returns>
        public bool CreateNewFamily(Document doc, List<GeometryObject> resultList, string familyName, out Family newFamily)
        {
            newFamily = null;
            try
            {
                string templatePath = @"E:\Donald的檔案\中興工程\測試檔案\公制通用模型.rft"; 
                string tempFamilyPath = Path.Combine(@"E:\Donald的檔案\中興工程\測試檔案\", familyName + ".rfa");

                Document familyDoc = doc.Application.NewFamilyDocument(templatePath);
                using (Transaction famTrans = new Transaction(familyDoc, "創建幾何體"))
                {
                    famTrans.Start(); 
                    foreach (Solid solid in resultList) { FreeFormElement.Create(familyDoc, solid); }
                    famTrans.Commit();
                }
                // 另存新檔
                SaveAsOptions saveOpt = new SaveAsOptions();
                saveOpt.OverwriteExistingFile = true;
                familyDoc.SaveAs(tempFamilyPath, saveOpt);
                // 關閉
                familyDoc.Close(false);

                //// 將新族群載入到當前文件
                //using (Transaction loadTrans = new Transaction(doc, "載入新族群"))
                //{
                //    loadTrans.Start();
                //    Family loadedFamily = null;
                //    try
                //    {
                //        //doc.LoadFamily(tempFamilyPath, out loadedFamily);
                //        bool a = doc.LoadFamily(tempFamilyPath);

                //        if (loadedFamily != null)
                //        {
                //            // 重命名族
                //            loadedFamily.Name = familyName;
                //            newFamily = loadedFamily;
                //        }
                //    }
                //    catch(Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                //    loadTrans.Commit();
                //}

                //// 移除檔案
                //if (File.Exists(tempFamilyPath))
                //{
                //    try { File.Delete(tempFamilyPath); } catch { }
                //}

                return newFamily != null;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", "發生錯誤: " + ex.Message);
                return false;
            }
        }
        // 新增共用參數
        private void CreateSharedParameter(Document doc, FamilySymbol familySymbol, List<Parameter> paraList)
        {
            string directoryName = Path.GetDirectoryName(doc.PathName);
            doc.Application.SharedParametersFilename = Path.Combine(directoryName, "SinotechSharedParameter.txt");
            DefinitionFile definitionFile = doc.Application.OpenSharedParameterFile();
            if (definitionFile == null)
            {
                if (File.Exists(doc.Application.SharedParametersFilename)) { File.Delete(doc.Application.SharedParametersFilename); }
                File.Create(doc.Application.SharedParametersFilename).Close();
                definitionFile = doc.Application.OpenSharedParameterFile();
            }
            DefinitionGroup definitionGroup = definitionFile.Groups.get_Item("Sinotech");
            if (definitionGroup == null) { definitionGroup = definitionFile.Groups.Create("Sinotech"); }
            foreach (Parameter para in paraList)
            {
                //string text = para.Definition.Name.Contains("Type") ? ("_" + para.Definition.Name) : (familySymbol.FamilyName + "_" + para.Definition.Name);
                string text = para.Definition.Name;
                if (text.Contains("平台")) { string test = text; }
                if (definitionGroup.Definitions.get_Item(text) == null)
                {
                    ExternalDefinitionCreationOptions externalDefinitionCreationOptions = new ExternalDefinitionCreationOptions(text, para.Definition.ParameterType);
                    //ExternalDefinitionCreationOptions externalDefinitionCreationOptions = new ExternalDefinitionCreationOptions(text, UnitTypeId.Meters);
                    try { definitionGroup.Definitions.Create(externalDefinitionCreationOptions); }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException ex) { string error = ex.Message + "\n" + ex.ToString(); }
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
                        while (enumerator2.MoveNext()) { Category category2 = (Category)enumerator2.Current; categorySet.Insert(category2); }
                        break;
                        //}
                    }
                }
                categorySet.Insert(category);
                InstanceBinding instanceBinding = doc.Application.Create.NewInstanceBinding(categorySet);
                doc.ParameterBindings.Insert(definition, instanceBinding);
                if (categorySet.Size > 1) { doc.ParameterBindings.ReInsert(definition, instanceBinding); }
                else { doc.ParameterBindings.Insert(definition, instanceBinding); }
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
            catch (Exception ex) { TaskDialog.Show("Error", "修改參數失敗" + ex.Message); result = false; }
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
            catch (Exception ex) { TaskDialog.Show("Error", "修改族群參數失敗" + ex.Message); result = false; }
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
            List<Dimension> list = new List<Dimension>();
            try
            {
                foreach (Dimension current in new FilteredElementCollector(doc).OfClass(typeof(Dimension)).Cast<Dimension>().ToList<Dimension>())
                {
                    IEnumerator enumerator2 = current.References.GetEnumerator();
                    //using (IEnumerator enumerator2 = current.References.GetEnumerator())
                    //{
                    while (enumerator2.MoveNext())
                    {
                        if (((Reference)enumerator2.Current).ElementId.IntegerValue == selectedElem.Id.IntegerValue)
                        {
                            list.Add(current);
                            break;
                        }
                    }
                    //}
                }
            }
            catch (Exception ex)
            {
                //TaskDialog.Show("抓取關聯尺寸標註失敗", ex.Message);
            }
            return list;
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

        public string GetName()
        {
            return "元件保護";
        }
    }
}