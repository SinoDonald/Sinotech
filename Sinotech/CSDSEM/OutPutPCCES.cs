using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Sinotech.UpdateView;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace Sinotech.CSDSEM
{
    [Transaction(TransactionMode.Manual)]
    public class OutPutPCCES : IExternalCommand
    {
        public class ItemDescription
        {
            public string item = string.Empty; // 項次
            public string description = string.Empty; // 項目及說明
        }
        public class OpeningContrast
        {
            public string name = string.Empty; // 章名
            public string type = string.Empty; // 套管or開口
            public string host = string.Empty; // 干涉品類(牆、樑、板)
            public double diameter = 0.0; // 管徑（mm）
            public double area = 0.0; // 面積（m²）
            public double volume = 0.0; // 體積（m³）
            public double min = 0.0; // 小值
            public double max = 0.0; // 大值
            public string prjNumber = string.Empty; // 工程項目編號
            public string description = string.Empty; // 項目及說明
        }
        public class ModelInfo
        {
            public Level level = null; // 樓層
            public double elevation = 0.0; // 高程（Revit 內部單位 ft，僅用於排序）
            public string levelName = string.Empty; // 樓層名稱
            public string item = string.Empty; // 項次
            public string familyName = string.Empty; // 開口名稱
            public string type = string.Empty; // 開口類型
            public string pipeOrDuct = string.Empty; // 套管or開口or基座
            public int isPillar = 0; // 是否為止水墩
            public int pipeOrDuctInt = 0; // 開口類型排序
            public string host = string.Empty; // 開口對象(牆、樑、板)
            public double diameter = 0.0; // 直徑（mm）
            public double area = 0.0; // 面積（m²）
            public double volume = 0.0; // 體積（m³）
            public double length = 0.0; // 計價長度（m，導線管與止水墩統一）
            public double floorLength = 0.0; // 樓板(or基座止水墩)長度（mm）
            public double floorWidth = 0.0; // 樓板(or基座止水墩)寬度（mm）
            public double floorHeight = 0.0; // 基座止水墩高度（mm）
            public double perimeter = 0.0; // 周長
            public double interference = 0.0; // 干涉長度（mm）
            public string description = string.Empty; // 項目及說明
            public string unit = string.Empty; // 單位
            public int count = 0; // 數量
            public string calculator = string.Empty; // 計算式
            public string drawingNumber = string.Empty; // 圖號
            public string prjNumber = string.Empty; // 工程項目編號
            public string comments = string.Empty; // 備註
            public string linkPrj = string.Empty; // 連結專案
            public string pCode = string.Empty; // 專業代碼
            public ElementId elementId = null;
        }
        // 專案中所有的Level
        public List<Level> levelList = new List<Level>();
        // Excel All Sheet比對資料
        public List<OpeningContrast> openingContrastList = new List<OpeningContrast>();
        public static List<ExcelCellData> ecDataList = new List<ExcelCellData>(); // 將Excel中Sheet的Cell資料都撈出來
        // PCCES 比對單位：直徑與尺寸為 mm，面積為 m²，體積為 m³，計價長度為 m。
        // 容許單位轉換造成的浮點誤差（mm），不使用顯示精度進行比對。
        private const double DiameterToleranceMillimeters = 1e-6;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
            Document doc = uidoc.Document;

            // Excel檔案路徑
            // 彈跳視窗選擇Excel檔
            OpenFileDialog ofd = new OpenFileDialog();
            string filePath = string.Empty; // Excel路徑
            if (string.IsNullOrEmpty(ofd.InitialDirectory))
            {                
                ofd.Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*";
                ofd.Title = "請選擇Excel檔";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    filePath = ofd.FileName;
                }
            }
            if (!filePath.Equals("") && filePath != null)
            {
                //CloseExcel(filePath); // 查詢Excel是否開啟中

                DateTime timeStart = DateTime.Now; // 計時開始 取得目前時間

                // 讀取Excel中的All Sheet, 將比對的套管、開口, 對應名稱&工程編號撈出紀錄
                UpdateExcel updateExcel = new UpdateExcel();
                openingContrastList = updateExcel.ReadExcel(filePath);
                List<ItemDescription> ItemDescriptions = updateExcel.ItemDescriptions; // A1、A2、A3配合各標名稱

                // 收集專案中所有的Level, 並且依高程排序
                List<Level> levels = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Levels).WhereElementIsNotElementType().Cast<Level>().ToList();
                levelList = levels.OrderByDescending(x => x.Elevation).ToList();
                // 將Excel中Sheet的Cell資料都撈出來
                ecDataList = new List<ExcelCellData>();
                // 讀取並儲存所有的Element
                List<ModelInfo> modelDB;
                try { modelDB = ModelDB(doc); }
                catch (InvalidOperationException ex)
                {
                    TaskDialog.Show("PCCES 止水墩計算", ex.Message);
                    return Result.Cancelled;
                }
                // 篩選各樓層, 高程由上而下
                List<string> disLevelNames = modelDB.OrderByDescending(x => x.elevation).Select(x => x.levelName).Distinct().ToList();

                // 篩選連結的檔案
                List<string> disLinkPrjs = modelDB.Select(x => x.linkPrj).Distinct().OrderBy(x => x).ToList();
                // 如果條件為"開口"+"樓板", 則計算該條件下的止水墩周長, 並加入modelDB
                CurbStopCalcul(modelDB, disLevelNames, disLinkPrjs);
                try
                {
                    foreach (string disLevelName in disLevelNames)
                    {
                        ExcelCellData excelCellData = new ExcelCellData();
                        for (int i = 0; i <= 6; i++)
                        {
                            if (i.Equals(1))
                            {
                                excelCellData.cellValues.Add(disLevelName);
                            }
                            else
                            {
                                excelCellData.cellValues.Add("");
                            }
                        }
                        ecDataList.Add(excelCellData); // 樓層
                        foreach (string disLinkPrj in disLinkPrjs)
                        {
                            excelCellData = new ExcelCellData();
                            excelCellData.cellValues.Add(disLinkPrj);
                            // A1、A2、A3配合各標名稱
                            string itemDescriptions = (from x in ItemDescriptions
                                                       where x.item.Equals(disLinkPrj)
                                                       select x.description).FirstOrDefault();
                            excelCellData.cellValues.Add(itemDescriptions);
                            for (int j = 0; j <= 4; j++)
                            {
                                excelCellData.cellValues.Add("");
                            }
                            ecDataList.Add(excelCellData); // 配合
                            int i = 1;
                            List<ModelInfo> modelDBFilter = (from x in modelDB
                                                             where x.levelName.Equals(disLevelName) && x.linkPrj.Equals(disLinkPrj)
                                                             select x).Distinct().OrderBy(x => x.pipeOrDuctInt).ThenByDescending(x => x.host).ThenBy(x => x.diameter).ThenBy(x => x.area).ThenBy(x => x.volume).ToList();
                            // 篩選出不同的項目及說明
                            List<string> descriptions = (from x in modelDBFilter
                                                         select x.description).Distinct().ToList();
                            foreach (string description in descriptions)
                            {
                                List<ModelInfo> filteredItems = (from x in modelDB
                                                                 where x.levelName.Equals(disLevelName) && x.linkPrj.Equals(disLinkPrj) && x.description.Equals(description)
                                                                 select x).ToList();
                                // 單位
                                string unit = (from x in filteredItems
                                               select x.unit).FirstOrDefault();
                                // 長度
                                double length = 0.0;
                                if (unit.Equals("公尺"))
                                {
                                    double sum = (from x in filteredItems
                                                  select x.length).Sum();
                                    if (description.Contains("止水墩"))
                                    {
                                        if (description.Contains("基座"))
                                        {
                                            length = Math.Round(sum, 0, MidpointRounding.AwayFromZero);
                                        }
                                        else
                                        {
                                            length = Math.Round(sum, 2, MidpointRounding.AwayFromZero); // 貴森兄
                                        }
                                    }
                                    else
                                    {
                                        length = Math.Round(sum, 0, MidpointRounding.AwayFromZero);
                                    }
                                }
                                // 工程項目編號
                                string prjNumber = (from x in filteredItems
                                                    select x.prjNumber).FirstOrDefault();
                                if (filteredItems.Count() > 0)
                                {
                                    excelCellData = new ExcelCellData();
                                    excelCellData.cellValues.Add(disLinkPrj + "-" + i.ToString("00")); // 項次
                                    excelCellData.cellValues.Add(description); // 項目及說明
                                    excelCellData.cellValues.Add(unit); // 單位
                                    if (unit.Equals("公尺"))
                                    {
                                        excelCellData.cellValues.Add(length.ToString()); // 長度
                                    }
                                    else
                                    {
                                        excelCellData.cellValues.Add(filteredItems.Count().ToString()); // 數量
                                    }
                                    excelCellData.cellValues.Add(""); // 計算式
                                    excelCellData.cellValues.Add(""); // 圖號
                                    excelCellData.cellValues.Add(prjNumber); // 工程項目編號
                                    ecDataList.Add(excelCellData); // Cell
                                    i++;
                                }
                            }
                            excelCellData = new ExcelCellData();
                            for (int j = 0; j <= 6; j++)
                            {
                                excelCellData.cellValues.Add(""); // 換行
                            }
                            ecDataList.Add(excelCellData); // Cell
                        }
                    }
                    string sheetName = "工程數量計算表";
                    filePath = updateExcel.WriteExcelCell(filePath, sheetName, ecDataList);

                    // 將Excel中Sheet的Cell資料重置
                    ecDataList = new List<ExcelCellData>();
                    // 工程數量詳細表
                    List<ProjectInfo> projectInfos = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_ProjectInformation).WhereElementIsNotElementType().Cast<ProjectInfo>().ToList();
                    ProjectInfo projectInfo = (from x in projectInfos
                                               select x).FirstOrDefault();
                    ExcelCellData excelDetailCell = new ExcelCellData();
                    for (int i = 0; i <= 6; i++)
                    {
                        if (i.Equals(1))
                        {
                            excelDetailCell.cellValues.Add(projectInfo.Number);
                        }
                        else
                        {
                            excelDetailCell.cellValues.Add("");
                        }
                    }
                    ecDataList.Add(excelDetailCell); // 站名
                    foreach (string disLinkPrj in disLinkPrjs)
                    {
                        excelDetailCell = new ExcelCellData();
                        excelDetailCell.cellValues.Add(disLinkPrj);
                        // A1、A2、A3配合各標名稱
                        string itemDescriptions = (from x in ItemDescriptions
                                                   where x.item.Equals(disLinkPrj)
                                                   select x.description).FirstOrDefault();
                        excelDetailCell.cellValues.Add(itemDescriptions);
                        for (int j = 0; j <= 4; j++)
                        {
                            excelDetailCell.cellValues.Add("");
                        }
                        ecDataList.Add(excelDetailCell); // 配合
                        int i = 1;
                        List<ModelInfo> modelDBFilter = (from x in modelDB
                                                         where x.linkPrj.Equals(disLinkPrj)
                                                         select x).Distinct().OrderBy(x => x.pipeOrDuctInt).ThenByDescending(x => x.host).ThenBy(x => x.diameter).ThenBy(x => x.area).ThenBy(x => x.volume).ToList();
                        // 篩選出不同的項目及說明
                        List<string> descriptions = (from x in modelDBFilter
                                                     select x.description).Distinct().ToList();
                        foreach (string description in descriptions)
                        {
                            List<ModelInfo> filteredItems = (from x in modelDB
                                                             where x.linkPrj.Equals(disLinkPrj) && x.description.Equals(description)
                                                             select x).ToList();
                            // 單位
                            string unit = (from x in filteredItems
                                           select x.unit).FirstOrDefault();
                            // 長度
                            double length = 0.0;
                            if (unit.Equals("公尺"))
                            {
                                double sum = (from x in filteredItems
                                              select x.length).Sum();
                                if (description.Contains("止水墩"))
                                {
                                    if (description.Contains("基座"))
                                    {
                                        length = Math.Round(sum, 0, MidpointRounding.AwayFromZero);
                                    }
                                    else
                                    {
                                        length = Math.Round(sum, 2, MidpointRounding.AwayFromZero); // 貴森兄
                                    }
                                }
                                else
                                {
                                    length = Math.Round(sum, 0, MidpointRounding.AwayFromZero);
                                }
                            }
                            // 工程項目編號
                            string prjNumber = (from x in filteredItems
                                                select x.prjNumber).FirstOrDefault();
                            if (filteredItems.Count() > 0)
                            {
                                excelDetailCell = new ExcelCellData();
                                excelDetailCell.cellValues.Add(disLinkPrj + "-" + i.ToString("00")); // 項次
                                excelDetailCell.cellValues.Add(description); // 項目及說明
                                excelDetailCell.cellValues.Add(unit); // 單位
                                if (unit.Equals("公尺"))
                                {
                                    excelDetailCell.cellValues.Add(length.ToString()); // 長度
                                }
                                else
                                {
                                    excelDetailCell.cellValues.Add(filteredItems.Count().ToString()); // 數量
                                }
                                excelDetailCell.cellValues.Add(""); // 數量(彙總計算)
                                excelDetailCell.cellValues.Add(""); // 參考頁次
                                excelDetailCell.cellValues.Add(prjNumber); // 工程項目編號
                                ecDataList.Add(excelDetailCell); // Cell
                                i++;
                            }
                        }
                        excelDetailCell = new ExcelCellData();
                        for (int j = 0; j <= 6; j++)
                        {
                            excelDetailCell.cellValues.Add(""); // 換行
                        }
                        ecDataList.Add(excelDetailCell); // Cell
                    }
                    sheetName = "工程數量詳細表";
                    updateExcel.WriteExcelCell(filePath, sheetName, ecDataList);

                    DateTime timeEnd = DateTime.Now; // 計時結束 取得目前時間
                    TimeSpan totalTime = timeEnd - timeStart;
                    TaskDialog.Show("Revit", "耗時：" + totalTime.Minutes + " 分 " + totalTime.Seconds + " 秒 " + "\n\n完成");
                }
                catch (Exception)
                {

                }
            }

            return Result.Succeeded;
        }
        // 查詢Excel是否開啟中
        private void CloseExcel(string filePath)
        {
            int pathIndex = filePath.LastIndexOf('\\');
            int fileIndex = filePath.LastIndexOf('.');
            string fileName = filePath.Substring(pathIndex + 1, fileIndex - pathIndex - 1);
            Process[] apps = Process.GetProcessesByName("EXCEL");
            if (apps.Length > 0)
            {
                MessageBox.Show("提醒！\n需關閉『" + fileName + ".xlsx』方能進行數量計算寫入。");
                return;
            }
        }
        // 讀取並儲存所有的Element
        private List<ModelInfo> ModelDB(Document doc)
        {
            List<ModelInfo> modelInfoList = new List<ModelInfo>();

            // 開口+套管
            IList<ElementFilter> openingFilters = new List<ElementFilter>(); // 清空過濾器  
            ElementCategoryFilter pipeOpenFilter = new ElementCategoryFilter(BuiltInCategory.OST_PipeAccessory); // 管道套管
            ElementCategoryFilter ductOpenFilter = new ElementCategoryFilter(BuiltInCategory.OST_DuctAccessory); // 風管開口
            ElementCategoryFilter cableTrayOpenFilter = new ElementCategoryFilter(BuiltInCategory.OST_CableTrayFitting); // 電纜架開口
            ElementCategoryFilter conduitFilter = new ElementCategoryFilter(BuiltInCategory.OST_Conduit); // 電管
            openingFilters.Add(pipeOpenFilter);
            openingFilters.Add(ductOpenFilter);
            openingFilters.Add(cableTrayOpenFilter);
            openingFilters.Add(conduitFilter);
            LogicalOrFilter logicalOrFilter = new LogicalOrFilter(openingFilters);
            List<Element> openings = new FilteredElementCollector(doc).WherePasses(logicalOrFilter).WhereElementIsNotElementType().ToElements().ToList();
            // 基座
            List<Element> platforms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_GenericModel).WhereElementIsNotElementType().Where(x => x.Name.Contains("基座")).ToList();
            openings.AddRange(platforms);

            // 匯出前先驗證所有需要計價的基座，避免後面的既有例外處理漏掉失敗項目。
            var curbCalculator = new CurbWallContactCalculator(doc);
            var curbLengths = new Dictionary<ElementId, double>();
            foreach (Element platform in platforms)
            {
                if (platform.LookupParameter("止水墩")?.AsInteger() != 1) continue;
                try { curbLengths[platform.Id] = curbCalculator.CalculateMillimeters(platform); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("基座 " + platform.Id.Value + " 的貼牆計算失敗，已停止匯出。\n" + ex.Message, ex);
                }
            }

            int i = 1;
            foreach (Element opening in openings)
            {
                try
                {
                    ModelInfo modelInfo = new ModelInfo();
                    modelInfo.familyName = opening.Name; // 開口名稱
                    // 套管or開口
                    if (opening.Name.Contains("圓形"))
                    {
                        modelInfo.type = "管及管件";
                        modelInfo.pipeOrDuct = "套管";
                        modelInfo.pipeOrDuctInt = 1;
                        modelInfo.unit = "個";
                        try
                        {
                            Parameter para = opening.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            if (para != null)
                            {
                                modelInfo.linkPrj = CommentsLinkPrj(opening, modelInfo); // 連結專案
                                if (modelInfo.linkPrj.Equals("A3")) { }
                                modelInfo.pCode = opening.LookupParameter("專業代碼").AsString(); // 專業代碼
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.ArgumentNullException ex) { string error = ex.Message + "\n" + ex.ToString(); }
                        catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                        modelInfo.diameter = UnitUtils.ConvertFromInternalUnits(opening.LookupParameter("指定圓形套管直徑").AsDouble(), UnitTypeId.Millimeters);
                        if(modelInfo.diameter == 0) { modelInfo.diameter = UnitUtils.ConvertFromInternalUnits(opening.LookupParameter("預設圓形套管直徑").AsDouble(), UnitTypeId.Millimeters); }
                        // 開口對象(牆、樑、板)
                        if (opening.Name.Contains("牆")) { modelInfo.host = "牆"; }
                        else if (opening.Name.Contains("樓板") || opening.Name.Contains("樓版")) { modelInfo.host = "樓板"; }                        
                        string description = openingContrastList.Where(x => x.type.Equals(modelInfo.pipeOrDuct) && x.host.Equals(modelInfo.host) && Math.Abs(x.diameter - modelInfo.diameter) < DiameterToleranceMillimeters).Select(x => x.name).LastOrDefault(); // 項目及說明
                        modelInfo.description = description;                        
                        string prjNumber = openingContrastList.Where(x => x.name.Equals(description)).Select(x => x.prjNumber).LastOrDefault(); // 工程項目編號
                        modelInfo.prjNumber = prjNumber;
                    }
                    else if (opening.Name.Contains("風管") || opening.Name.Contains("電纜架"))
                    {
                        if (opening.Name.Contains("風管")) { modelInfo.type = "風管"; }
                        else if (opening.Name.Contains("電纜架")) { modelInfo.type = "電纜架"; }
                        modelInfo.pipeOrDuct = "開口";
                        modelInfo.pipeOrDuctInt = 2;
                        modelInfo.unit = "個";
                        try
                        {
                            Parameter para = opening.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            if (para != null)
                            {
                                modelInfo.linkPrj = CommentsLinkPrj(opening, modelInfo); // 連結專案
                                modelInfo.pCode = opening.LookupParameter("專業代碼").AsString(); // 專業代碼
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.ArgumentNullException ex) { string error = ex.Message + "\n" + ex.ToString(); }
                        catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                        modelInfo.area = UnitUtils.ConvertFromInternalUnits(opening.LookupParameter("矩形開口面積").AsDouble(), UnitTypeId.SquareMeters);
                        // 開口對象(牆、樑、板)
                        if (opening.Name.Contains("牆")) { modelInfo.host = "牆"; }
                        else if (opening.Name.Contains("樓板") || opening.Name.Contains("樓版")) 
                        {
                            modelInfo.host = "樓板";
                            modelInfo.floorLength = UnitUtils.ConvertFromInternalUnits(opening.LookupParameter("矩形開口高度").AsDouble(), UnitTypeId.Millimeters);
                            modelInfo.floorWidth = UnitUtils.ConvertFromInternalUnits(opening.LookupParameter("矩形開口寬度").AsDouble(), UnitTypeId.Millimeters);
                        }
                        // 項目及說明
                        OpeningContrast item = openingContrastList.Where(x => x.type.Equals(modelInfo.pipeOrDuct) && x.host.Equals(modelInfo.host))
                                               .Where(x => x.min < modelInfo.area && modelInfo.area <= x.max).FirstOrDefault();
                        if (item != null)
                        {
                            if (item.min < modelInfo.area && modelInfo.area <= item.max)
                            {
                                modelInfo.description = item.name;                                
                                string prjNumber = openingContrastList.Where(x => x.name.Equals(item.name)).Select(x => x.prjNumber).LastOrDefault(); // 工程項目編號
                                modelInfo.prjNumber = prjNumber;
                            }
                        }
                        else
                        {
                            item = openingContrastList.Where(x => x.type.Equals(modelInfo.pipeOrDuct) && x.host.Equals(modelInfo.host))
                                   .Where(x => x.min < modelInfo.area && x.max == 0).FirstOrDefault();
                            if (item.min < modelInfo.area && item.max == 0)
                            {
                                modelInfo.description = item.name;
                                string prjNumber = openingContrastList.Where(x => x.name.Equals(item.name)).Select(x => x.prjNumber).LastOrDefault(); // 工程項目編號
                                modelInfo.prjNumber = prjNumber;
                            }
                        }
                    }
                    else if (opening.Name.Contains("基座"))
                    {
                        modelInfo.type = "管及管件";
                        modelInfo.pipeOrDuct = "基座";
                        modelInfo.pipeOrDuctInt = 3;
                        modelInfo.unit = "個";
                        try
                        {
                            Parameter para = opening.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            if (para != null)
                            {
                                modelInfo.linkPrj = CommentsLinkPrj(opening, modelInfo); // 連結專案

                                //modelInfo.pCode = opening.LookupParameter("專業代碼").AsString(); // 專業代碼
                            }
                        }
                        catch (Autodesk.Revit.Exceptions.ArgumentNullException ex) { string error = ex.Message + "\n" + ex.ToString(); }
                        catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                        modelInfo.floorLength = UnitUtils.ConvertFromInternalUnits(opening.LookupParameter("長度").AsDouble(), UnitTypeId.Millimeters); // 基座止水墩長度
                        modelInfo.floorWidth = UnitUtils.ConvertFromInternalUnits(opening.LookupParameter("寬度").AsDouble(), UnitTypeId.Millimeters); // 基座止水墩寬度（mm）
                        modelInfo.isPillar = opening.LookupParameter("止水墩")?.AsInteger() ?? 0;
                        if (modelInfo.isPillar == 1)
                        {
                            // 由實際貼牆區段計算溝槽，不讀取「周長(長)／周長(寬)」勾選值。
                            modelInfo.interference = curbLengths[opening.Id];
                        }
                        modelInfo.volume = UnitUtils.ConvertFromInternalUnits(opening.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED).AsDouble(), UnitTypeId.CubicMeters); // 體積（m³）
                        // 項目及說明
                        OpeningContrast item = openingContrastList.Where(x => x.type.Equals(modelInfo.pipeOrDuct)).Where(x => x.min < modelInfo.volume && modelInfo.volume <= x.max).FirstOrDefault();
                        if(item != null)
                        {
                            if (item.min < modelInfo.volume && modelInfo.volume <= item.max)
                            {
                                modelInfo.description = item.name;                                
                                string prjNumber = openingContrastList.Where(x => x.name.Equals(item.name)).Select(x => x.prjNumber).LastOrDefault(); // 工程項目編號
                                modelInfo.prjNumber = prjNumber;
                            }
                        }
                        else
                        {
                            item = openingContrastList.Where(x => x.type.Equals(modelInfo.pipeOrDuct) && x.host.Equals(modelInfo.host))
                                   .Where(x => x.min < modelInfo.volume && x.max == 0).FirstOrDefault();
                            if (item.min < modelInfo.volume && item.max == 0)
                            {
                                modelInfo.description = item.name;
                                string prjNumber = openingContrastList.Where(x => x.name.Equals(item.name)).Select(x => x.prjNumber).LastOrDefault(); // 工程項目編號
                                modelInfo.prjNumber = prjNumber;
                            }
                        }
                    }
                    //else if (opening.Name.Contains("導線管") || opening.Name.Contains("硬質非金屬導管"))
                    else if(opening.Category.Name.Equals("電管"))
                    {
                        modelInfo.type = "導線管";
                        modelInfo.pipeOrDuct = "導線管";
                        modelInfo.pipeOrDuctInt = 4;
                        modelInfo.unit = "公尺";
                        try
                        {
                            Parameter para = opening.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            if (para != null) { modelInfo.linkPrj = "A1"/*CommentsLinkPrj(opening, modelInfo)*/; } // 連結專案
                        }
                        catch (Autodesk.Revit.Exceptions.ArgumentNullException ex) { string error = ex.Message + "\n" + ex.ToString(); }
                        catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                        
                        modelInfo.length = UnitUtils.ConvertFromInternalUnits(opening.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble(), UnitTypeId.Meters); // 長度
                        // 取Excel資料庫中的最近數值
                        double diameter = UnitUtils.ConvertFromInternalUnits(opening.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM).AsDouble(), UnitTypeId.Millimeters); // 直徑（mm） (標稱尺寸)
                        List<double> values = openingContrastList.Where(x => x.type.Equals(modelInfo.pipeOrDuct)).Select(x => x.diameter).Distinct().ToList();
                        diameter = values.OrderBy(x => Math.Abs(x - diameter)).FirstOrDefault();
                        modelInfo.diameter = diameter;                        
                        string description = openingContrastList.Where(x => x.type.Equals(modelInfo.pipeOrDuct) && Math.Abs(x.diameter - modelInfo.diameter) < DiameterToleranceMillimeters).Select(x => x.name).LastOrDefault(); // 項目及說明
                        modelInfo.description = description;                        
                        string prjNumber = openingContrastList.Where(x => x.name.Equals(description)).Select(x => x.prjNumber).LastOrDefault(); // 工程項目編號
                        modelInfo.prjNumber = prjNumber;
                    }
                    // 樓層
                    //if (opening.Name.Contains("導線管") || opening.Name.Contains("硬質非金屬導管"))
                    if(opening.Category.Name.Equals("電管"))
                    {
                        Level level = levelList.Where(x => x.Name.Contains("軌道層")).FirstOrDefault();
                        try
                        {
                            ElementId levelId = opening.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM).AsElementId(); // 參考樓層
                            level = doc.GetElement(levelId) as Level;
                        }
                        catch(Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                        modelInfo.level = level;
                    }
                    else { modelInfo.level = doc.GetElement(opening.LevelId) as Level; } // 樓層
                    modelInfo.elevation = modelInfo.level.Elevation; // 高程（Revit 內部單位 ft，僅用於排序）
                    modelInfo.levelName = modelInfo.level.Name; // 樓層名稱
                    modelInfo.elementId = opening.Id; // ElementId
                    if (!String.IsNullOrEmpty(modelInfo.description)) { modelInfoList.Add(modelInfo); }
                    i++;
                }
                catch (Exception) { }
            }

            return modelInfoList;
        }
        // 備註歸類的連結專案
        private string CommentsLinkPrj(Element opening, ModelInfo modelInfo)
        {
            string linkPrj = string.Empty;
            Parameter para = opening.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (para != null)
            {
                modelInfo.comments = para.AsString(); // 備註
                string[] comments = para.AsString().Split('_');
                linkPrj = comments[0];
                if (linkPrj.Equals("PS") || linkPrj.Equals("COM") || linkPrj.Equals("SN") || linkPrj.Equals("COM/SN") || linkPrj.Equals("AFC")) { linkPrj = "A1"; }
                else if (linkPrj.Equals("AD") || linkPrj.Equals("AP") || linkPrj.Equals("EE") || linkPrj.Equals("EP") ||
                         linkPrj.Equals("WS") || linkPrj.Equals("DS") || linkPrj.Equals("FP") || linkPrj.Equals("ECS") ||
                         linkPrj.Equals("E") || linkPrj.Equals("M") || linkPrj.Equals("CDA")) { linkPrj = "A2"; }
                else { linkPrj = "A3"; } // Test
                modelInfo.linkPrj = linkPrj; // 連結專案
            }

            return linkPrj;
        }
        // 如果條件為"開口"+"樓板", 則計算該條件下的止水墩周長, 並加入modelDB
        private void CurbStopCalcul(List<ModelInfo> modelDB, List<string> disLevelNames, List<string> disLinkPrjs)
        {
            foreach (string disLevelName in disLevelNames)
            {
                foreach (string disLinkPrj in disLinkPrjs)
                {
                    List<string> descriptions = modelDB.Where(x => x.levelName.Equals(disLevelName) && x.linkPrj.Equals(disLinkPrj) && 
                                                x.pipeOrDuct.Equals("開口") && x.host.Equals("樓板")).Select(x => x.description).Distinct().ToList();
                    foreach (string description in descriptions)
                    {
                        double perimeter = 0.0;
                        List<ModelInfo> modelDBFilter = modelDB.Where(x => x.levelName.Equals(disLevelName) && x.linkPrj.Equals(disLinkPrj) &&
                                                        x.pipeOrDuct.Equals("開口") && x.host.Equals("樓板") && x.description.Equals(description)).ToList();
                        foreach (ModelInfo curbStopCalcul in modelDBFilter)
                        {
                            // 各面積級距的固定計價周長以 m 表示。
                            if (description.Contains("面積≦0.1m2")) { perimeter += 2.56; }
                            else if (description.Contains("0.1m2＜面積≦0.5m2")) { perimeter += 5.68; }
                            else if (description.Contains("0.5m2＜面積≦1.0m2")) { perimeter += 8.0; }
                            else if (description.Contains("1.0m2＜面積≦1.5m2")) { perimeter += 9.84; }
                            else if (description.Contains("1.5m2＜面積≦2.0m2")) { perimeter += 11.36; }
                            else { perimeter += UnitUtils.Convert((curbStopCalcul.floorLength + curbStopCalcul.floorWidth) * 2, UnitTypeId.Millimeters, UnitTypeId.Meters); }
                        }
                        ModelInfo modelInfo = new ModelInfo();
                        modelInfo.levelName = disLevelName;
                        modelInfo.linkPrj = disLinkPrj; 
                        modelInfo.pipeOrDuct = "止水墩";
                        modelInfo.pipeOrDuctInt = 5;
                        modelInfo.host = "樓板";
                        modelInfo.unit = "公尺";
                        modelInfo.description = "止水墩，100mmX50mm(樓板開口)";
                        modelInfo.length = perimeter;
                        modelDB.Add(modelInfo);
                    }
                    List<string> stopPillars = modelDB.Where(x => x.levelName.Equals(disLevelName) && x.linkPrj.Equals(disLinkPrj) &&
                                               x.pipeOrDuct.Equals("基座") && x.isPillar.Equals(1)).Select(x => x.description).Distinct().ToList();
                    foreach (string stopPillar in stopPillars)
                    {
                        double perimeter = 0.0;
                        List<ModelInfo> modelDBFilter = modelDB.Where(x => x.levelName.Equals(disLevelName) && x.linkPrj.Equals(disLinkPrj) &&
                                                        x.pipeOrDuct.Equals("基座") && x.isPillar.Equals(1) && x.description.Equals(stopPillar)).ToList();
                        foreach (ModelInfo curbStopCalcul in modelDBFilter)
                        {
                            perimeter += UnitUtils.Convert(curbStopCalcul.interference, UnitTypeId.Millimeters, UnitTypeId.Meters);
                        }
                        ModelInfo modelInfo = new ModelInfo();
                        modelInfo.levelName = disLevelName;
                        modelInfo.linkPrj = disLinkPrj;
                        modelInfo.pipeOrDuct = "止水墩";
                        modelInfo.pipeOrDuctInt = 6;
                        modelInfo.host = "樓板";
                        modelInfo.unit = "公尺";
                        modelInfo.description = "止水墩，100mmX100mm(基座)";
                        modelInfo.length = perimeter;
                        modelDB.Add(modelInfo);
                    }
                }
            }
        }
    }
}
