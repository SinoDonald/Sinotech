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
    public class FamilyConversionToDirectShape : IExternalEventHandler
    {
        string genericModelPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "Revit", "Addins", "Sinotech", "公制通用模型.rft"); // 公制通用模型
        string tempPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "元件保護");
        public class OriginalElemParas
        {
            public string familyName { get; set; }
            public List<string> paraNames = new List<string>();
            public List<Parameter> paraList = new List<Parameter>();
        }
        public List<OriginalElemParas> originalElemParaList = new List<OriginalElemParas>();
        public void Execute(UIApplication uiapp)
        {
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;
            genericModelPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "RVT " + app.VersionNumber, "Family Templates", "Traditional Chinese", "公制通用模型.rft"); // 公制通用模型
            if (!File.Exists(genericModelPath)) 
            {
                genericModelPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", "RVT " + app.VersionNumber, "Family Templates", "English", "Metric Generic Model.rft");
            }
            tempPath = Path.GetDirectoryName(doc.PathName);
            originalElemParaList = new List<OriginalElemParas>(); // 重置

            TransactionGroup tranGrp1 = new TransactionGroup(doc, "元件保護");
            tranGrp1.Start();
            int count = 0;
            List<string> familyNames = new List<string>();
            using (Transaction trans = new Transaction(doc, "建立模型"))
            {
                List<FamilyInstance> chooseFamilys = ChooseElems.chooseFamilys;
                List<ElementId> familySymbolIds = new List<ElementId>();
                familyNames = chooseFamilys.Select(x => x.Symbol.FamilyName).Distinct().ToList();
                List<string> containWords = new List<string>() { "長度", "管線" };
                foreach (string familyName in familyNames)
                {
                    try
                    {
                        FamilyInstance familyInstance = chooseFamilys.Where(x => x.Symbol.FamilyName.Equals(familyName)).FirstOrDefault();
                        List<Parameter> paraList = new List<Parameter>();
                        foreach (Parameter para in familyInstance.Parameters)
                        {
                            //if (containWords.Where(x => para.Definition.Name.Contains(x)).FirstOrDefault() != null)
                            //{
                                paraList.Add(para);
                            //}
                        }
                        paraList = paraList.OrderBy(x => x.Definition.Name).ToList();
                        List<string> paraNames = paraList.Select(x => x.Definition.Name).ToList();

                        // 儲存原族群所擁有的參數
                        OriginalElemParas originalElemParas = new OriginalElemParas();
                        originalElemParas.familyName = familyInstance.Symbol.FamilyName;
                        originalElemParas.paraNames = paraNames;
                        originalElemParas.paraList = paraList;
                        originalElemParaList.Add(originalElemParas);
                    }
                    catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                }
                // 新增共用參數
                trans.Start();
                foreach (string familyName in familyNames)
                {
                    try
                    {
                        FamilyInstance familyInstance = chooseFamilys.Where(x => x.Symbol.FamilyName.Equals(familyName)).FirstOrDefault();
                        List<Parameter> paraList = originalElemParaList.Where(x => x.familyName.Equals(familyInstance.Symbol.FamilyName)).Select(x => x.paraList).FirstOrDefault();
                        CreateSharedParameter(doc, familyInstance.Symbol, paraList);
                        //Family newFamily = CreateNewFamily(uiapp, familyInstance); // 建立相同的族群, 可編輯族群, 參數都消失
                    }
                    catch(Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                }
                trans.Commit();
                // 建立無法編輯之元件
                trans.Start();
                foreach (FamilyInstance chooseFamily in chooseFamilys)
                {
                    try
                    {
                        DirectShape directShape = DirectShape.CreateElement(doc, chooseFamily.Category.Id);
                        directShape.ApplicationDataId = "Sinotech";
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
                        directShape.Name = chooseFamily.Symbol.FamilyName;
                        SetParameterFromOriginalElement(directShape, chooseFamily); // 修改參數
                        List<Dimension> dimensionList = GetDimension(doc, chooseFamily);
                        familySymbolIds.Add(chooseFamily.Symbol.Id);
                        doc.Delete(chooseFamily.Id);
                        count++;
                    }
                    catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                }
                // 移除未使用的元件
                familySymbolIds = familySymbolIds.Distinct().ToList();
                foreach (ElementId familySymbolId in familySymbolIds) { doc.Delete(familySymbolId); }
                trans.Commit();
            }
            if (File.Exists(doc.Application.SharedParametersFilename)) { File.Delete(doc.Application.SharedParametersFilename); }
            TaskDialog.Show("Revit", "成功鎖定 " + count + " 個元件。");

            tranGrp1.Assimilate();
        }
        /// <summary>
        /// 建立相同的族群, 可編輯族群, 參數都消失
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="resultList"></param>
        /// <param name="familyInstance"></param>
        /// <param name="newFamily"></param>
        /// <returns></returns>
        public Family CreateNewFamily(UIApplication uiapp, FamilyInstance familyInstance)
        {
            Family newFamily = null;
            try
            {
                Document doc = uiapp.ActiveUIDocument.Document;
                string tempFamilyPath = Path.Combine(tempPath, familyInstance.Symbol.FamilyName + ".rfa");
                Document familyDoc = doc.Application.NewFamilyDocument(genericModelPath);
                // 儲存Solid的形狀塑型
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

                using (Transaction famTrans = new Transaction(familyDoc, "創建幾何體"))
                {
                    famTrans.Start(); 
                    foreach (Solid solid in resultList) { FreeFormElement.Create(familyDoc, solid); }
                    List<Dimension> dimensionList = GetDimension(doc, familyInstance);
                    famTrans.Commit();
                }
                // 另存新檔
                SaveAsOptions saveOpt = new SaveAsOptions();
                saveOpt.OverwriteExistingFile = true;
                familyDoc.SaveAs(tempFamilyPath, saveOpt);

                // 更新專案內Family的參數
                Family loadedFamily = null;
                using (Transaction loadTrans = new Transaction(familyDoc, "載入新族群"))
                {
                    loadTrans.Start();
                    try 
                    { 
                        familyDoc.LoadFamily(tempFamilyPath, new LoadOptions(), out loadedFamily);
                        if (loadedFamily != null)
                        {
                            loadedFamily.Name = familyInstance.Symbol.FamilyName;
                            newFamily = loadedFamily;
                        }
                        familyDoc.LoadFamily(doc, new LoadOptions()); // 更新專案的Family
                    }
                    catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                    loadTrans.Commit();
                }
                familyDoc.Close(false); // 關閉
                if (File.Exists(tempFamilyPath)) { try { File.Delete(tempFamilyPath); } catch { } } // 移除檔案
            }
            catch (Exception ex) { string error = "發生錯誤: " + ex.Message; }

            return newFamily;
        }
        /// <summary>
        ///  新增共用參數
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="familySymbol"></param>
        /// <param name="paraList"></param>
        private void CreateSharedParameter(Document doc, FamilySymbol familySymbol, List<Parameter> paraList)
        {
            string directoryName = Path.GetDirectoryName(doc.PathName);
            doc.Application.SharedParametersFilename = Path.Combine(directoryName, "SinotechSharedParameter.txt"); // 共用參數檔案路徑
            //DefinitionFile definitionFile = doc.Application.OpenSharedParameterFile(); // 讀取共用參數訊息
            //if (definitionFile == null)
            //{
            if (File.Exists(doc.Application.SharedParametersFilename)) { File.Delete(doc.Application.SharedParametersFilename); }
            File.Create(doc.Application.SharedParametersFilename).Close();
            DefinitionFile definitionFile = doc.Application.OpenSharedParameterFile();
            //}
            DefinitionGroup definitionGroup = definitionFile.Groups.get_Item("Sinotech"); // 讀取共用參數中的Sinotech群組
            if (definitionGroup == null) { definitionGroup = definitionFile.Groups.Create("Sinotech"); }
            foreach (Parameter para in paraList)
            {
                string text = para.Definition.Name;
                if (definitionGroup.Definitions.get_Item(text) == null)
                {
                    try
                    {
                        //ExternalDefinitionCreationOptions externalDefinitionCreationOptions = RevitAPI.GetExternalDefinitionOptions(text, para);
                        //ExternalDefinitionCreationOptions externalDefinitionCreationOptions = new ExternalDefinitionCreationOptions(text, para.Definition.ParameterType); // 2020
                        ForgeTypeId forgeTypeId = para.Definition.GetDataType();
                        ExternalDefinitionCreationOptions externalDefinitionCreationOptions = new ExternalDefinitionCreationOptions(text, forgeTypeId); // 2024
                        externalDefinitionCreationOptions.UserModifiable = false; // 唯讀
                        externalDefinitionCreationOptions.HideWhenNoValue = true; // 無資料則隱藏
                        try { definitionGroup.Definitions.Create(externalDefinitionCreationOptions); }
                        catch (Autodesk.Revit.Exceptions.InvalidOperationException ex) { string error = ex.Message + "\n" + ex.ToString(); }
                    }
                    catch(Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                }
                Category category = familySymbol.Category;
                CategorySet categorySet = doc.Application.Create.NewCategorySet(); // 創建一個類別集合用於綁定, 把品類加入
                DefinitionBindingMapIterator definitionBindingMapIterator = doc.ParameterBindings.ForwardIterator();
                while (definitionBindingMapIterator.MoveNext())
                {
                    Definition key = definitionBindingMapIterator.Key;
                    ElementBinding elementBinding = (ElementBinding)definitionBindingMapIterator.Current;
                    if (text == key.Name)
                    {
                        IEnumerator enumerator2 = elementBinding.Categories.GetEnumerator();
                        while (enumerator2.MoveNext())
                        {
                            Category category2 = (Category)enumerator2.Current; 
                            categorySet.Insert(category2); 
                        }
                        break;
                    }
                }
                if (!categorySet.Contains(category))
                { 
                    categorySet.Insert(category);
                    // 取得當前檔案的BindingMap, 並將建立的InstanceBinding綁定上去
                    Definition definition = definitionGroup.Definitions.get_Item(text);
                    if(definition != null)
                    {
                        InstanceBinding instanceBinding = doc.Application.Create.NewInstanceBinding(categorySet);
                        BindingMap bindingMap = doc.ParameterBindings;
                        if (categorySet.Size > 1) { bindingMap.ReInsert(definition, instanceBinding); }
                        else { bindingMap.Insert(definition, instanceBinding); }
                    }
                }
            }
        }
        /// <summary>
        /// 修改參數
        /// </summary>
        /// <param name="newElem"></param>
        /// <param name="originalElem"></param>
        /// <returns></returns>
        public bool SetParameterFromOriginalElement(Element newElem, Element originalElem)
        {
            bool result;
            try
            {
                List<Parameter> paras = new List<Parameter>();
                foreach (Parameter para in originalElem.Parameters) { paras.Add(para); }
                OriginalElemParas originalElemParas = originalElemParaList.Where(x => x.familyName.Equals(newElem.Name)).FirstOrDefault();
                foreach (string paraName in originalElemParas.paraNames)
                {
                    List<Parameter> originalElemParameters = paras.Where(x => x.Definition.Name.Equals(paraName)).ToList();
                    Parameter parameter = paras.Where(x => x.Definition.Name.Equals(paraName)).FirstOrDefault();
                    //foreach (Parameter parameter in originalElemParameters)
                    //{
                        //if(parameter.Definition.ParameterGroup != BuiltInParameterGroup.INVALID)
                        if (parameter.Definition.GetDataType().IsValidObject)
                        {
                            Parameter para = newElem.LookupParameter(paraName);
                            if (para != null && !para.IsReadOnly)
                            {
                                if (parameter.StorageType == StorageType.Double) { double value = parameter.AsDouble(); para.Set(parameter.AsDouble()); }
                                else if (parameter.StorageType == StorageType.ElementId) { ElementId value = parameter.AsElementId(); para.Set(parameter.AsElementId()); }
                                else if (parameter.StorageType == StorageType.Integer) { int value = parameter.AsInteger(); para.Set(parameter.AsInteger()); }
                                else if (parameter.StorageType == StorageType.String) { string value = parameter.AsString(); if (!String.IsNullOrEmpty(value)) { para.Set(parameter.AsString()); } }
                            }
                        }
                    //}
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
                        if (((Reference)enumerator2.Current).ElementId.ToString() == selectedElem.Id.ToString())
                        //if (((Reference)enumerator2.Current).ElementId.IntegerValue == selectedElem.Id.IntegerValue)
                        //if (RevitAPI.NewElementId(((Reference)enumerator2.Current).ElementId.ToString()) == RevitAPI.NewElementId(selectedElem.Id.ToString()))
                        {
                            list.Add(current);
                            break;
                        }
                    }
                    //}
                }
            }
            catch (Exception ex) { string error = "抓取關聯尺寸標註失敗" + "\n" + ex.Message + "\n" + ex.ToString(); }
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
        // 更新專案內Family的參數
        public class LoadOptions : IFamilyLoadOptions
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
        public string GetName()
        {
            return "元件保護";
        }
    }
}