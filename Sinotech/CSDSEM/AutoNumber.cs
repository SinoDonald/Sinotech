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

namespace Sinotech.CSDSEM
{
    [Transaction(TransactionMode.Manual)]
    public class AutoNumber : IExternalCommand
    {
        public class OpeningInfo
        {
            public FamilyInstance opening = null; // 開口
            public Level level = null; // 樓層
            public ViewPlan viewPlan = null; // 視圖
            public string linkRvt = string.Empty; // 連結的檔案
            public int crushPipeId = 0; // 干涉的管
            public int crushElemId = 0; // 干涉的牆樑板
            public double x = 0; // 座標點X
            public double y = 0; // 座標點Y
            public double z = 0; // 座標點Z
        }
        public class PipeData
        {
            public Element elem = null; // 主體
            public List<Element> connectors = new List<Element>(); // 連結的元件
            public XYZ start = new XYZ(); // 起點
            public bool isStart = false; // 是否為起點
        }
        public class RevitLinkPipeType
        {
            public RevitLinkInstance revitLinkInstance = null; // rvt
            public string type = string.Empty; // Type
            public List<PipingSystem> pypingSystems = new List<PipingSystem>();
            public List<MechanicalSystem> mechanicalSystems = new List<MechanicalSystem>();
        }
        public class ErrorId
        {
            public string errorMessge { get; set; }
            public string id { get; set; }
        }
        private List<OpeningInfo> openingInfoList = new List<OpeningInfo>(); // 依座標點排序的開口
        private static List<Level> docLevels = new List<Level>(); // Document內所有的Level
        private List<ErrorId> errorIds = new List<ErrorId>(); // 無法編號的開口元件


        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            DateTime timeStart = DateTime.Now;

            // 1. 取得所有 Revit 連結
            List<RevitLinkInstance> rvtInss = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .WhereElementIsNotElementType()
                .Cast<RevitLinkInstance>()
                .Where(x => x.GetLinkDocument() != null)
                .ToList();

            // 2. 叫出設定視窗讓使用者指派欄位與排序順序
            NumberingExecutionSettings settings;
            using (var form = new AutoNumberSettingForm(rvtInss))
            {
                if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return Result.Cancelled;
                }
                settings = form.ResultSettings;
            }

            // 讀取所有 Document 的 Level
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

            // MEP 篩選器構建
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
                    // 一、套管編號處理 (依使用者排序順序 OrderedCasingCodes)
                    List<OpeningInfo> levelOpeningList = openingInfoList.Where(x => x.level.Id.Equals(docLevel.Id)).ToList();
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

                            // 處理未干涉但屬於該代碼的其餘開口
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

                    // 二、開口編號處理 (依使用者排序順序 OrderedOpeningCodes)
                    levelOpeningList = openingInfoList.Where(x => x.level.Id.Equals(docLevel.Id)).ToList();
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
                                List<Element> ductAndFittings = new List<Element>();
                                foreach (Element elem in mechSystem.DuctNetwork) { ductAndFittings.Add(elem); }
                                List<PipeData> ductDataList = PipeAndConnector(ductAndFittings);
                                List<PipeData> ductDataSort = PipeSort(ductDataList);
                                snOpening = CrushSearch(ductDataSort, levelOpeningList, snOpening);
                            }
                        }

                        // 2. 電纜架
                        List<Element> cableTrayElems = new FilteredElementCollector(linkDoc).WherePasses(cableTrayOrOpeningFilter).WhereElementIsNotElementType().ToList();
                        if (cableTrayElems.Count > 0)
                        {
                            List<PipeData> trayDataList = PipeAndConnector(cableTrayElems);
                            List<PipeData> trayDataSort = PipeSort(trayDataList);
                            snOpening = CrushSearch(trayDataSort, levelOpeningList, snOpening);
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
        // 將openingList資料儲存為OpeningInfo, 稍後執行排序
        private List<OpeningInfo> SaveOpeningInfo(Document doc, List<FamilyInstance> openingList)
        {
            List<OpeningInfo> opList = new List<OpeningInfo>();
            foreach (FamilyInstance familyInstance in openingList)
            {
                ErrorId errorId = new ErrorId();
                OpeningInfo opening = new OpeningInfo();
                opening.opening = familyInstance; // 開口
                try
                {
                    Level level = doc.GetElement(familyInstance.LevelId) as Level;
                    opening.level = level; // 樓層
                    ViewPlan viewPlan = doc.GetElement(level.FindAssociatedPlanViewId()) as ViewPlan;
                    opening.viewPlan = viewPlan; // 視圖
                    string[] comments = familyInstance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString().Split('_');
                    try
                    {
                        opening.linkRvt = comments[0]; // 連結的檔案
                        opening.crushPipeId = Convert.ToInt32(comments[1]); // 干涉的管
                        opening.crushElemId = Convert.ToInt32(comments[2]); // 干涉的牆樑板
                    }
                    catch (FormatException) { opening.crushElemId = Convert.ToInt32(comments[3]); } // 干涉的牆樑板
                    catch (Exception ex)
                    { 
                        string error = ex.Message + "\n" + ex.ToString();
                        errorId.errorMessge = ex.Message;
                        if (ex.Message.Equals("索引在陣列的界限之外。")) { errorId.errorMessge += "(【備註】資訊不足)"; }
                        errorId.id = familyInstance.Id.ToString();
                        if (errorIds.Where(x => x.id.Equals(familyInstance.Id.ToString())).ToList().Count() == 0) { errorIds.Add(errorId); }
                    }
                    LocationPoint lp = familyInstance.Location as LocationPoint;
                    opening.x = lp.Point.X; // 座標點X
                    opening.y = lp.Point.Y; // 座標點Y
                    opening.z = lp.Point.Z; // 座標點Z
                    opList.Add(opening);
                }
                catch(Exception ex)
                {
                    string error = ex.Message + "\n" + ex.ToString();
                    errorId.errorMessge = ex.Message;
                    if (ex.Message.Equals("並未將物件參考設定為物件的執行個體。")) { errorId.errorMessge += "(缺少【樓層】資訊)"; }
                    errorId.id = familyInstance.Id.ToString();
                    if (errorIds.Where(x => x.id.Equals(familyInstance.Id.ToString())).ToList().Count() == 0) { errorIds.Add(errorId); }
                }
            }
            openingInfoList = opList.OrderBy(x => x.x).ThenBy(x => x.y).ToList();
            return openingInfoList;
        }
        // 查詢並儲存各管道與彎頭的連結對象
        private List<PipeData> PipeAndConnector(List<Element> pipeAndFittings)
        {
            List<PipeData> pipeDataList = new List<PipeData>();
            foreach(Element elem in pipeAndFittings)
            {
                PipeData pipeData = new PipeData();
                if(elem is Pipe)
                {
                    Pipe pipe = elem as Pipe;
                    pipeData.elem = pipe; // 管道
                    LocationCurve locationCurve = pipe.Location as LocationCurve;
                    pipeData.start = locationCurve.Curve.GetEndPoint(0); // 起點座標
                    foreach (Connector connector in pipe.ConnectorManager.Connectors)
                    {
                        foreach(Connector allRef in connector.AllRefs)
                        {
                            try
                            {
                                if(allRef.MEPSystem != null)
                                {
                                    if (allRef.Owner.Id != pipe.Id)
                                    {
                                        pipeData.connectors.Add(allRef.Owner); // 旁邊的connectors
                                        break;
                                    }
                                }
                            }
                            catch(Autodesk.Revit.Exceptions.InvalidOperationException ex) { string error = ex.Message + "\n" + ex.ToString(); }
                        }
                    }
                    if (pipeData.connectors.Count.Equals(1)) { pipeData.isStart = true; }
                    pipeDataList.Add(pipeData);
                }
                else if (elem is Duct)
                {
                    Duct duct = elem as Duct;
                    pipeData.elem = duct; // 管道
                    LocationCurve locationCurve = duct.Location as LocationCurve;
                    pipeData.start = locationCurve.Curve.GetEndPoint(0); // 起點座標
                    foreach (Connector connector in duct.ConnectorManager.Connectors)
                    {
                        foreach (Connector allRef in connector.AllRefs)
                        {
                            try
                            {
                                if (allRef.MEPSystem != null)
                                {
                                    if (allRef.Owner.Id != duct.Id)
                                    {
                                        pipeData.connectors.Add(allRef.Owner); // 旁邊的connectors
                                        break;
                                    }
                                }
                            }
                            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex) { string error = ex.Message + "\n" + ex.ToString(); }
                        }
                    }
                    if (pipeData.connectors.Count.Equals(1)) { pipeData.isStart = true; }
                    pipeDataList.Add(pipeData);
                }
                else if (elem is CableTray)
                {
                    CableTray cableTray = elem as CableTray;
                    pipeData.elem = cableTray; // 管道
                    LocationCurve locationCurve = cableTray.Location as LocationCurve;
                    pipeData.start = locationCurve.Curve.GetEndPoint(0); // 起點座標
                    foreach (Connector connector in cableTray.ConnectorManager.Connectors)
                    {
                        foreach (Connector allRef in connector.AllRefs)
                        {
                            try
                            {
                                if (allRef.Owner.Id != cableTray.Id)
                                {
                                    pipeData.connectors.Add(allRef.Owner); // 旁邊的connectors
                                    break;
                                }
                            }
                            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex) { string error = ex.Message + "\n" + ex.ToString(); }
                        }
                    }
                    if (pipeData.connectors.Count.Equals(1)) { pipeData.isStart = true; }
                    pipeDataList.Add(pipeData);
                }                
                else if(elem is FamilyInstance)
                {
                    FamilyInstance familyInstance = elem as FamilyInstance;
                    pipeData.elem = familyInstance; // 彎頭
                    LocationPoint locationPoint = familyInstance.Location as LocationPoint;
                    pipeData.start = locationPoint.Point; // 起點座標
                    foreach (Connector connector in familyInstance.MEPModel.ConnectorManager.Connectors)
                    {
                        foreach (Connector allRef in connector.AllRefs)
                        {
                            try
                            {
                                //if (allRef.MEPSystem != null)
                                //{
                                    if (allRef.Owner.Id != familyInstance.Id) { pipeData.connectors.Add(allRef.Owner); } // 旁邊的connectors
                                //}
                            }
                            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex) { string error = ex.Message + "\n" + ex.ToString(); }
                        }
                    }
                    pipeDataList.Add(pipeData);
                }
            }
            return pipeDataList;
        }
        // 管道排序
        private List<PipeData> PipeSort(List<PipeData> pipeDataList)
        {
            // 排序過後的管道
            List<PipeData> pipeDataSort = new List<PipeData>();
            // 一個PipingSystem一個起點
            List<PipeData> pipeDatas = pipeDataList.Where(x => x.isStart == true).ToList();
            if (pipeDatas.Count.Equals(0))
            {
                pipeDatas = pipeDataList.Where(x => {
                    foreach (Element elem in x.connectors) { if (elem is MechanicalSystem) { return true; } }
                    return false;
                }).ToList();
                //pipeDatas = pipeDataList.Where(x => x.connectors.Where(y => y is MechanicalSystem).ToList().Count > 0).ToList();
            }            
            PipeData pipeData = pipeDatas.OrderBy(p => p.start.X).FirstOrDefault(); // 找到List中最小的x座標, 從左往右排序            
            RemoveRepeat(pipeDataList, pipeData, pipeDataSort); // 重複查詢執行排序
            return pipeDataSort;
        }
        // 重複查詢執行排序
        private void RemoveRepeat(List<PipeData> pipeDataList, PipeData pipeData, List<PipeData> pipeDataSort)
        {
            if (pipeData != null)
            {
                if (pipeData.elem is Pipe || pipeData.elem is Duct || pipeData.elem is CableTray || pipeData.elem is FamilyInstance) {  pipeDataSort.Add(pipeData); } // 排序List                
                pipeDataList.Remove(pipeData); // 排序後將pipeDataList移除, 避免重複計算
                foreach (Element connectElem in pipeData.connectors)
                {
                    PipeData notRepeat = pipeDataSort.Where(x => x.elem.Id.Equals(connectElem.Id)).FirstOrDefault();
                    if (notRepeat == null)
                    {
                        PipeData connectorPipeData = pipeDataList.Where(x => x.elem.Id.Equals(connectElem.Id)).FirstOrDefault();
                        if (connectorPipeData != null) { RemoveRepeat(pipeDataList, connectorPipeData, pipeDataSort); } // Repeat
                    }
                }
            }
        }
        // 依管道順序干涉查詢, 將開口編號
        private int CrushSearch(List<PipeData> pipeDataSort, List<OpeningInfo> openingInfoList, int sn)
        {
            foreach(PipeData pipeData in pipeDataSort)
            {
                ErrorId errorId = new ErrorId();
                // 儲存修改過編號的開口
                List<OpeningInfo> removeOpenings = new List<OpeningInfo>();
                List<OpeningInfo> sameCrushPipes = openingInfoList.Where(x => x.crushPipeId.Equals((int)pipeData.elem.Id.Value)).ToList();
                foreach (OpeningInfo sameCrushPipe in sameCrushPipes)
                {
                    FamilyInstance opening = sameCrushPipe.opening;
                    string[] openingInfos = opening.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).AsString().Split('_');
                    if (openingInfos[1].Equals(pipeData.elem.Id.ToString()))
                    {
                        try
                        {
                            string info = pipeData.elem.Id + "_" + opening.Id;
                            Parameter para = null;
                            if(pipeData.elem is Pipe) { para = opening.LookupParameter("圓形牆開口流水號"); }
                            else if(pipeData.elem is Duct || pipeData.elem is CableTray || pipeData.elem is FamilyInstance) { para = opening.LookupParameter("矩形牆開口流水號"); }
                            para.Set(sn);
                            removeOpenings.Add(sameCrushPipe);
                            sn++;
                        }
                        catch (Exception ex)
                        {
                            string error = ex.Message + "\n" + ex.ToString();
                            errorId.errorMessge = ex.Message;
                            if (ex.Message.Equals("並未將物件參考設定為物件的執行個體。")) { errorId.errorMessge += "(缺少【牆開口流水號】資訊)"; }
                            errorId.id = opening.Id.ToString();
                            if (errorIds.Where(x => x.id.Equals(opening.Id.ToString())).ToList().Count() == 0) { errorIds.Add(errorId); }
                        }
                    }
                }
                // 移除已編號的開口
                foreach(OpeningInfo removeOpening in removeOpenings) { openingInfoList.Remove(removeOpening); }
            }

            return sn;
        }
        // 自動標籤錯誤訊息
        private void CreateErrorMessage()
        {
            try
            {
                string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "自動標籤錯誤訊息.txt");
                // 先檢查是否有此檔案, 沒有的話則新增
                string folderPath = Path.GetDirectoryName(filePath);
                if (!File.Exists(filePath)) { using (FileStream fs = File.Create(filePath)) { } }
                using (StreamWriter sw = new StreamWriter(filePath))
                {
                    string content = string.Empty;
                    List<string> messages = errorIds.Select(x => x.errorMessge).Distinct().ToList();
                    foreach (string info in messages)
                    {
                        content += "\n\n" + info + "\n\n";
                        List<string> ids = errorIds.Where(x => x.errorMessge.Equals(info)).Distinct().OrderBy(x => x.id).Select(x => x.id).ToList();
                        foreach (string id in ids) { content += id + "\n"; }
                    }
                    sw.WriteLine(content);
                    sw.Close();
                }
            }
            catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
        }
        // 創建標籤
        private IndependentTag CreateIndependentTag(Document doc, Pipe pipe, int i)
        {
            // 確認不是在3D視圖
            Autodesk.Revit.DB.View view = doc.ActiveView;

            // 定義標籤模式與方向
            TagMode tagMode = TagMode.TM_ADDBY_CATEGORY;
            TagOrientation tagorn = TagOrientation.Horizontal;

            // 於管道終點標籤
            LocationCurve pipeLoc = pipe.Location as LocationCurve;
            XYZ pipeStart = pipeLoc.Curve.GetEndPoint(0); // 管道起點
            XYZ pipeEnd = pipeLoc.Curve.GetEndPoint(1); // 管道終點
            XYZ pipeMid = pipeLoc.Curve.Evaluate(0.5, true); // 管道中點
            Reference pipeRef = new Reference(pipe);
            IndependentTag newTag = null;
            try
            {
                newTag = IndependentTag.Create(doc, view.Id, pipeRef, true, tagMode, tagorn, pipeMid);
                if (null == newTag) { throw new Exception("建立標籤失敗."); }

                // 修改管道「備註」的資訊, 於標籤顯示
                string number = i.ToString();
                Parameter wallPara = pipe.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                wallPara.Set(number);

                // 設置標籤模式
                newTag.LeaderEndCondition = LeaderEndCondition.Free;
                //XYZ elbowPnt = pipeMid + new XYZ(5.0, 0.0, 0.0);
                //newTag.LeaderElbow = elbowPnt;
                //XYZ headerPnt = pipeMid + new XYZ(5.0, 10.0, 0.0);
                //newTag.TagHeadPosition = headerPnt;
            }
            catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }

            return newTag;
        }
    }
}