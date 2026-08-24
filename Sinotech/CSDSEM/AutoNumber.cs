using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sinotech.CSDSEM
{
    [Transaction(TransactionMode.Manual)]
    public class AutoNumber : IExternalCommand
    {
        public class OpeningInfo
        {
            public FamilyInstance opening = null;
            public Level level = null;
            public ViewPlan viewPlan = null;
            public string linkRvt = string.Empty;
            public int crushPipeId = 0;
            public int crushElemId = 0;
            public double x = 0;
            public double y = 0;
            public double z = 0;
        }

        public class PipeData
        {
            public Element elem = null;
            public List<Element> connectors = new List<Element>();
            public XYZ start = new XYZ();
            public bool isStart = false;
        }

        public class RevitLinkPipeType
        {
            public RevitLinkInstance revitLinkInstance = null;
            public string type = string.Empty;
            public List<PipingSystem> pypingSystems = new List<PipingSystem>();
            public List<MechanicalSystem> mechanicalSystems = new List<MechanicalSystem>();
        }

        public class ErrorId
        {
            public string errorMessge { get; set; }
            public string id { get; set; }
        }

        private List<OpeningInfo> openingInfoList = new List<OpeningInfo>();
        private static List<Level> docLevels = new List<Level>();
        private List<ErrorId> errorIds = new List<ErrorId>();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            DateTime timeStart = DateTime.Now;

            // 1. 讀取所有連結專案
            List<RevitLinkInstance> rvtInss = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .WhereElementIsNotElementType()
                .Cast<RevitLinkInstance>()
                .Where(x => x.GetLinkDocument() != null)
                .ToList();

            // 2. 開啟 UI 介面供使用者設定解析欄位與開口/套管順序
            NumberingExecutionSettings settings;
            using (var form = new AutoNumberSettingForm(rvtInss))
            {
                if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return Result.Cancelled;
                }
                settings = form.ResultSettings;
            }

            // 讀取所有樓層
            docLevels = new FilteredElementCollector(doc).OfClass(typeof(Level)).WhereElementIsNotElementType().Cast<Level>().ToList();

            // 讀取並儲存專案中所有開口
            IList<ElementFilter> accessortyOpeningFilter = new List<ElementFilter>
            {
                new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory),
                new ElementCategoryFilter(BuiltInCategory.OST_DuctAccessory),
                new ElementCategoryFilter(BuiltInCategory.OST_CableTrayFitting)
            };
            LogicalOrFilter openLogicalOrFilter = new LogicalOrFilter(accessortyOpeningFilter);
            List<FamilyInstance> openingList = new FilteredElementCollector(doc)
                .WherePasses(openLogicalOrFilter)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            openingInfoList = SaveOpeningInfo(doc, openingList);

            // 過濾器配置
            ElementCategoryFilter pipeFilter = new ElementCategoryFilter(BuiltInCategory.OST_PipeCurves);
            ElementCategoryFilter pipeFittingFilter = new ElementCategoryFilter(BuiltInCategory.OST_PipeFitting);
            ElementCategoryFilter ductFilter = new ElementCategoryFilter(BuiltInCategory.OST_DuctCurves);
            ElementCategoryFilter ductAccessoryFilter = new ElementCategoryFilter(BuiltInCategory.OST_DuctAccessory);
            ElementCategoryFilter cableTrayFilter = new ElementCategoryFilter(BuiltInCategory.OST_CableTray);
            ElementCategoryFilter cableTrayFittingFilter = new ElementCategoryFilter(BuiltInCategory.OST_CableTrayFitting);

            LogicalOrFilter pipeOrFittingFilter = new LogicalOrFilter(pipeFilter, pipeFittingFilter);
            LogicalOrFilter ductOrOpeningFilter = new LogicalOrFilter(ductFilter, ductAccessoryFilter);
            LogicalOrFilter cableTrayOrOpeningFilter = new LogicalOrFilter(cableTrayFilter, cableTrayFittingFilter);

            using (Transaction trans = new Transaction(doc, "自動編號"))
            {
                trans.Start();

                foreach (Level docLevel in docLevels)
                {
                    // ==========================================
                    // 順序一：先編【開口】(風管、電纜架)
                    // ==========================================
                    List<OpeningInfo> levelOpeningList = openingInfoList.Where(x => x.level.Id.Equals(docLevel.Id)).ToList();
                    int snOpening = 1;

                    foreach (string openingCode in settings.OrderedOpeningCodes)
                    {
                        if (!settings.CodeToLinkMap.TryGetValue(openingCode, out RevitLinkInstance rvtIns)) continue;
                        Document linkDoc = rvtIns.GetLinkDocument();
                        if (linkDoc == null) continue;

                        // 1. 風管
                        List<Element> ductElems = new FilteredElementCollector(linkDoc).WherePasses(ductOrOpeningFilter).WhereElementIsNotElementType().ToList();
                        if (ductElems.Count > 0)
                        {
                            List<MechanicalSystem> mechanicalSystems = new FilteredElementCollector(linkDoc).OfCategory(BuiltInCategory.OST_DuctSystem).WhereElementIsNotElementType().Cast<MechanicalSystem>().ToList();
                            foreach (MechanicalSystem mechSystem in mechanicalSystems)
                            {
                                List<Element> pipeAndFittings = new List<Element>();
                                foreach (Element elem in mechSystem.DuctNetwork) { pipeAndFittings.Add(elem); }
                                List<PipeData> pipeDataList = PipeAndConnector(pipeAndFittings);
                                List<PipeData> pipeDataSort = PipeSort(pipeDataList);
                                snOpening = CrushSearch(pipeDataSort, levelOpeningList, snOpening);
                            }
                        }

                        // 2. 電纜架
                        List<Element> cableTrayElems = new FilteredElementCollector(linkDoc).WherePasses(cableTrayOrOpeningFilter).WhereElementIsNotElementType().ToList();
                        if (cableTrayElems.Count > 0)
                        {
                            List<PipeData> pipeDataList = PipeAndConnector(cableTrayElems);
                            List<PipeData> pipeDataSort = PipeSort(pipeDataList);
                            snOpening = CrushSearch(pipeDataSort, levelOpeningList, snOpening);
                        }

                        // 處理未干涉但屬於該代碼的其餘開口
                        List<OpeningInfo> otherList = levelOpeningList.Where(x => x.opening.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()?.Split('_')[0] == openingCode).ToList();
                        foreach (OpeningInfo other in otherList)
                        {
                            try
                            {
                                Parameter para = other.opening.LookupParameter("矩形牆開口流水號");
                                if (para != null)
                                {
                                    para.Set(snOpening);
                                    snOpening++;
                                }
                            }
                            catch (Exception ex)
                            {
                                LogError(other.opening.Id.ToString(), ex.Message);
                            }
                        }
                    }

                    // ==========================================
                    // 順序二：後編【套管】(管道系統)
                    // ==========================================
                    levelOpeningList = openingInfoList.Where(x => x.level.Id.Equals(docLevel.Id)).ToList();
                    int snCasing = 1;

                    foreach (string casingCode in settings.OrderedCasingCodes)
                    {
                        if (!settings.CodeToLinkMap.TryGetValue(casingCode, out RevitLinkInstance rvtIns)) continue;
                        Document linkDoc = rvtIns.GetLinkDocument();
                        if (linkDoc == null) continue;

                        List<Element> pipeElems = new FilteredElementCollector(linkDoc).WherePasses(pipeOrFittingFilter).WhereElementIsNotElementType().ToList();
                        if (pipeElems.Count > 0)
                        {
                            List<PipingSystem> pipingSystems = new FilteredElementCollector(linkDoc).OfCategory(BuiltInCategory.OST_PipingSystem).WhereElementIsNotElementType().Cast<PipingSystem>().ToList();
                            foreach (PipingSystem pipingSystem in pipingSystems)
                            {
                                List<Element> pipeAndFittings = new List<Element>();
                                foreach (Element elem in pipingSystem.PipingNetwork) { pipeAndFittings.Add(elem); }
                                List<PipeData> pipeDataList = PipeAndConnector(pipeAndFittings);
                                List<PipeData> pipeDataSort = PipeSort(pipeDataList);
                                snCasing = CrushSearch(pipeDataSort, levelOpeningList, snCasing);
                            }

                            // 處理未干涉但屬於該代碼的其餘套管
                            List<OpeningInfo> otherList = levelOpeningList.Where(x => x.opening.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()?.Split('_')[0] == casingCode).ToList();
                            foreach (OpeningInfo other in otherList)
                            {
                                try
                                {
                                    Parameter para = other.opening.LookupParameter("圓形牆開口流水號");
                                    if (para != null)
                                    {
                                        para.Set(snCasing);
                                        snCasing++;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogError(other.opening.Id.ToString(), ex.Message);
                                }
                            }
                        }
                    }
                }

                trans.Commit();
            }

            if (errorIds.Count > 0) { CreateErrorMessage(); }

            TimeSpan totalTime = DateTime.Now - timeStart;
            string info = errorIds.Count == 0 ? "完成！\n" : $"尚有 {errorIds.Count} 筆元件需檢查\n";
            TaskDialog.Show("Revit", info + $"耗時：{totalTime.Minutes} 分 {totalTime.Seconds} 秒。");

            return Result.Succeeded;
        }

        private void LogError(string id, string message)
        {
            if (!errorIds.Any(x => x.id == id))
            {
                errorIds.Add(new ErrorId { id = id, errorMessge = message });
            }
        }

        private List<OpeningInfo> SaveOpeningInfo(Document doc, List<FamilyInstance> openingList)
        {
            List<OpeningInfo> opList = new List<OpeningInfo>();
            foreach (FamilyInstance familyInstance in openingList)
            {
                ErrorId errorId = new ErrorId();
                OpeningInfo opening = new OpeningInfo { opening = familyInstance };
                try
                {
                    Level level = doc.GetElement(familyInstance.LevelId) as Level;
                    opening.level = level;
                    if (level != null && level.FindAssociatedPlanViewId() != ElementId.InvalidElementId)
                    {
                        opening.viewPlan = doc.GetElement(level.FindAssociatedPlanViewId()) as ViewPlan;
                    }

                    string commentStr = familyInstance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString();
                    if (!string.IsNullOrEmpty(commentStr))
                    {
                        string[] comments = commentStr.Split('_');
                        try
                        {
                            opening.linkRvt = comments[0];
                            opening.crushPipeId = Convert.ToInt32(comments[1]);
                            opening.crushElemId = Convert.ToInt32(comments[2]);
                        }
                        catch (FormatException)
                        {
                            if (comments.Length > 3) opening.crushElemId = Convert.ToInt32(comments[3]);
                        }
                        catch (Exception ex)
                        {
                            LogError(familyInstance.Id.ToString(), ex.Message + " (【備註】資訊不足)");
                        }
                    }

                    if (familyInstance.Location is LocationPoint lp)
                    {
                        opening.x = lp.Point.X;
                        opening.y = lp.Point.Y;
                        opening.z = lp.Point.Z;
                    }
                    opList.Add(opening);
                }
                catch (Exception ex)
                {
                    LogError(familyInstance.Id.ToString(), ex.Message + " (缺少【樓層】資訊)");
                }
            }
            return opList.OrderBy(x => x.x).ThenBy(x => x.y).ToList();
        }

        private List<PipeData> PipeAndConnector(List<Element> pipeAndFittings)
        {
            List<PipeData> pipeDataList = new List<PipeData>();
            foreach (Element elem in pipeAndFittings)
            {
                PipeData pipeData = new PipeData { elem = elem };
                ConnectorSet connectors = null;

                if (elem is MEPCurve mepCurve)
                {
                    LocationCurve lc = mepCurve.Location as LocationCurve;
                    if (lc != null) pipeData.start = lc.Curve.GetEndPoint(0);
                    connectors = mepCurve.ConnectorManager?.Connectors;
                }
                else if (elem is FamilyInstance fi && fi.MEPModel != null)
                {
                    LocationPoint lp = fi.Location as LocationPoint;
                    if (lp != null) pipeData.start = lp.Point;
                    connectors = fi.MEPModel.ConnectorManager?.Connectors;
                }

                if (connectors != null)
                {
                    foreach (Connector connector in connectors)
                    {
                        foreach (Connector allRef in connector.AllRefs)
                        {
                            try
                            {
                                if (allRef.Owner.Id != elem.Id)
                                {
                                    pipeData.connectors.Add(allRef.Owner);
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                }

                if (pipeData.connectors.Count == 1) pipeData.isStart = true;
                pipeDataList.Add(pipeData);
            }
            return pipeDataList;
        }

        private List<PipeData> PipeSort(List<PipeData> pipeDataList)
        {
            List<PipeData> pipeDataSort = new List<PipeData>();
            List<PipeData> pipeDatas = pipeDataList.Where(x => x.isStart).ToList();
            if (pipeDatas.Count == 0)
            {
                pipeDatas = pipeDataList.Where(x => x.connectors.Any(elem => elem is MechanicalSystem || elem is PipingSystem)).ToList();
            }

            PipeData pipeData = (pipeDatas.Count > 0 ? pipeDatas : pipeDataList).OrderBy(p => p.start.X).FirstOrDefault();
            RemoveRepeat(pipeDataList, pipeData, pipeDataSort);
            return pipeDataSort;
        }

        private void RemoveRepeat(List<PipeData> pipeDataList, PipeData pipeData, List<PipeData> pipeDataSort)
        {
            if (pipeData != null)
            {
                pipeDataSort.Add(pipeData);
                pipeDataList.Remove(pipeData);
                foreach (Element connectElem in pipeData.connectors)
                {
                    if (!pipeDataSort.Any(x => x.elem.Id.Equals(connectElem.Id)))
                    {
                        PipeData connectorPipeData = pipeDataList.FirstOrDefault(x => x.elem.Id.Equals(connectElem.Id));
                        if (connectorPipeData != null)
                        {
                            RemoveRepeat(pipeDataList, connectorPipeData, pipeDataSort);
                        }
                    }
                }
            }
        }

        private int CrushSearch(List<PipeData> pipeDataSort, List<OpeningInfo> openingInfoList, int sn)
        {
            foreach (PipeData pipeData in pipeDataSort)
            {
                List<OpeningInfo> removeOpenings = new List<OpeningInfo>();
                List<OpeningInfo> sameCrushPipes = openingInfoList.Where(x => x.crushPipeId.Equals((int)pipeData.elem.Id.Value)).ToList();

                foreach (OpeningInfo sameCrushPipe in sameCrushPipes)
                {
                    FamilyInstance opening = sameCrushPipe.opening;
                    string[] openingInfos = opening.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()?.Split('_');
                    if (openingInfos != null && openingInfos.Length > 1 && openingInfos[1].Equals(pipeData.elem.Id.ToString()))
                    {
                        try
                        {
                            Parameter para = null;
                            if (pipeData.elem is Pipe) para = opening.LookupParameter("圓形牆開口流水號");
                            else para = opening.LookupParameter("矩形牆開口流水號");

                            if (para != null)
                            {
                                para.Set(sn);
                                removeOpenings.Add(sameCrushPipe);
                                sn++;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogError(opening.Id.ToString(), ex.Message + " (缺少【牆開口流水號】資訊)");
                        }
                    }
                }

                foreach (OpeningInfo removeOpening in removeOpenings)
                {
                    openingInfoList.Remove(removeOpening);
                }
            }
            return sn;
        }

        private void CreateErrorMessage()
        {
            try
            {
                string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "自動標籤錯誤訊息.txt");
                using (StreamWriter sw = new StreamWriter(filePath, false))
                {
                    List<string> messages = errorIds.Select(x => x.errorMessge).Distinct().ToList();
                    foreach (string info in messages)
                    {
                        sw.WriteLine("\n\n" + info + "\n");
                        List<string> ids = errorIds.Where(x => x.errorMessge.Equals(info)).Distinct().OrderBy(x => x.id).Select(x => x.id).ToList();
                        foreach (string id in ids) sw.WriteLine(id);
                    }
                }
            }
            catch { }
        }
    }
}