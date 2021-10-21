using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.Model;
using NPOI.XSSF.UserModel;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using static Sinotech.ReadExcel;

namespace Sinotech
{
    [Transaction(TransactionMode.Manual)]
    public class Sinotech_API : IExternalCommand
    {
        class ViewSheetAndNumber
        {
            public ViewSheet viewSheet { get; set; }
            public string number { get; set; }
        } // 圖紙與圖號
        public static List<FamilySymbol> familySymbolList = new List<FamilySymbol>(); // 從專案中找到全部的圖框
        public static List<string> sheetNames = new List<string>(); // Excel中全部的Sheet        
        public class ExcelCellData
        {
            public string sheetName = string.Empty; // Sheet名稱
            public int rowCount = 0; // 第幾列
            public List<string> cellValues = new List<string>(); // 值
            public bool header = false; // 是否為標頭
        } // 儲存Excel內Sheet的資料        
        public static List<ExcelCellData> ecDataList = new List<ExcelCellData>(); // 將Excel中Sheet的Cell資料都撈出來
        public static List<string> levelList = new List<string>(); // 所有樓層

        // 主程式
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
            Document doc = uidoc.Document;

            // 彈跳視窗選擇Excel檔
            OpenFileDialog ofd = new OpenFileDialog();
            string excelPath = string.Empty; // Excel路徑
            if (string.IsNullOrEmpty(ofd.InitialDirectory))
            {
                ofd.Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*";
                ofd.Title = "請選擇Excel檔";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    excelPath = ofd.FileName;
                }
            }

            if (!excelPath.Equals("") && excelPath != null)
            {
                try
                {
                    DateTime timeStart = DateTime.Now; // 計時開始 取得目前時間

                    sheetNames = ExcelSheet(excelPath); // Excel中全部的Sheet
                    // 從專案中找到全部的圖框
                    familySymbolList = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_TitleBlocks).Cast<FamilySymbol>().ToList();
                    CreateDrawings createDrawings = new CreateDrawings();
                    createDrawings.ShowDialog();
                    string titleBlocksName = createDrawings.titleBlocksName; // 選取的圖框名稱
                    string[] titleBlocksSplit = titleBlocksName.Split('_');

                    FamilySymbol familySymbol = null;
                    if (titleBlocksSplit.Length.Equals(2))
                    {
                        familySymbol = (from x in familySymbolList
                                        where x.FamilyName.Equals(titleBlocksSplit[0]) && x.Name.Equals(titleBlocksSplit[1])
                                        select x).FirstOrDefault();
                    }
                    else if (titleBlocksSplit.Length.Equals(3))
                    {
                        familySymbol = (from x in familySymbolList
                                        where x.FamilyName.Equals(titleBlocksSplit[0] + "_" + titleBlocksSplit[1]) && x.Name.Equals(titleBlocksSplit[2])
                                        select x).FirstOrDefault();
                    }
                    // 如果讀取不到FamilySymbol, 則預設為第一個
                    if(familySymbol == null)
                    {
                        familySymbol = familySymbolList[0];
                    }
                    // 如果FamilySymbol尚未啟動, 必須啟用才能使用
                    if (familySymbol != null)
                    {
                        if (!familySymbol.IsActive)
                        {
                            familySymbol.Activate();
                            doc.Regenerate();
                        }
                    }

                    List<string> checkSheets = createDrawings.checkSheets; // 選取的Sheet名稱
                    if (CreateDrawings.trueOrFalse == true && checkSheets.Count > 0) // 確定, 並且選取的Sheet大於0
                    {
                        ecDataList = ExcelSheetData(checkSheets, excelPath); // 將Excel中Sheet的Cell資料都撈出來
                        CreateFrames(doc, familySymbol, ecDataList); // 創建圖框, 並將參數寫入
                        
                        DateTime timeEnd = DateTime.Now; // 計時結束 取得目前時間
                        TimeSpan totalTime = timeEnd - timeStart;
                        TaskDialog.Show("Revit", "耗時：" + totalTime.Minutes + " 分 " + totalTime.Seconds + " 秒 " + "\n\n完成。");
                    }
                }
                catch (ArgumentException)
                {
                    TaskDialog.Show("Revit", "請選擇Excel檔");
                    return Result.Failed;
                }
                catch (FileNotFoundException)
                {
                    TaskDialog.Show("Revit", "找不到Excel檔");
                    return Result.Failed;
                }
                catch (DirectoryNotFoundException)
                {
                    TaskDialog.Show("Revit", "找不到Excel檔");
                    return Result.Failed;
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Revit", ex.Message + "\n\n" + ex.ToString());
                    return Result.Failed;
                }
            }

            return Result.Succeeded;
        }
        // 讀取Excel Sheet
        public static List<string> ExcelSheet(string filePath)
        {
            List<string> sheetNames = new List<string>();
            //讀取專案內中的sample.xls 的excel 檔案
            Stream stream = null;
            IWorkbook workbook = null;
            //ISheet sheet = null;
            // 07年以後的版本使用XSSFWorkbook和XSSFSheet，03年以前的使用HSSFWorkbook和HSSFSheet
            try
            {
                stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                workbook = new XSSFWorkbook(stream);
            }
            catch
            {
                stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                workbook = new HSSFWorkbook(stream);
            }
            int sheetCounts = workbook.NumberOfSheets; // Sheet的數量
            for (int i = 0; i < sheetCounts; i++)
            {
                sheetNames.Add(workbook.GetSheetName(i));
            }

            return sheetNames;
        }
        // 將Excel中Sheet的Cell資料都撈出來
        private static List<ExcelCellData> ExcelSheetData(List<string> checkSheets, string excelPath)
        {
            ecDataList = new List<ExcelCellData>();

            Stream stream = null;
            IWorkbook workbook = null;
            ISheet sheet = null;//上邊這幾行都是固定格式，如果你不深究，記著就行
            ExcelCellData ecData = new ExcelCellData();
            foreach (string sheetName in checkSheets)
            {
                try // Excel 2007以後
                {
                    stream = new FileStream(excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    workbook = new XSSFWorkbook(stream);
                    sheet = (XSSFSheet)workbook.GetSheet(sheetName);
                }
                catch // Excel 2007以前
                {
                    stream = new FileStream(excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    workbook = new HSSFWorkbook(stream);
                    sheet = (HSSFSheet)workbook.GetSheet(sheetName);
                }
                IRow row = null;
                int lastRowIndex = -1;
                if (sheet.PhysicalNumberOfRows > 0)
                {
                    lastRowIndex = sheet.LastRowNum; // 讀取row所涵蓋的範圍
                    for (int i = 0; i <= lastRowIndex; i++)
                    {
                        try
                        {
                            row = (XSSFRow)sheet.GetRow(i); // 2007以後
                        }
                        catch
                        {
                            row = (HSSFRow)sheet.GetRow(i); // 2007以前
                        }
                        if (row != null)
                        {
                            int lastCellIndex = row.LastCellNum; // 讀取此列cell的數量
                            ICell cellData = null;
                            try
                            {
                                ecData = new ExcelCellData();
                                ecData.sheetName = sheetName; // Sheet名稱
                                ecData.rowCount = i; // 第幾列
                                for (int j = 0; j < lastCellIndex; j++)
                                {
                                    cellData = row.GetCell(j);
                                    ecData = ExcelFormatCellData(workbook, cellData, ecData);  // 判別Cell格式後, 並儲存到ecData
                                }
                                if (!ecData.cellValues[0].Equals("")) // 圖號不得為空才儲存
                                {
                                    ecDataList.Add(ecData);
                                }
                            }
                            catch (NullReferenceException)
                            {

                            }
                        }
                    }
                }
            }

            return ecDataList;
        }
        // 判別Cell格式後, 並儲存到ecData
        private static ExcelCellData ExcelFormatCellData(IWorkbook workbook, ICell cellData, ExcelCellData ecData)
        {
            try
            {
                StylesTable st = ((XSSFWorkbook)workbook).GetStylesSource();
                XSSFDataFormat df = new XSSFDataFormat(st);
                string formatCode = df.GetFormat(cellData.CellStyle.DataFormat);
                // 如果儲存格式是數值
                if (cellData.CellType == NPOI.SS.UserModel.CellType.Numeric)
                {
                    if (formatCode.EndsWith("%"))
                    {
                        double dataValue = Convert.ToDouble(cellData.ToString()) * 100;
                        string value = Math.Round(dataValue, 2, MidpointRounding.AwayFromZero).ToString("0.00") + "%";
                        ecData.cellValues.Add(value);
                    }
                    else
                    {
                        ecData.cellValues.Add(cellData.ToString());
                    }
                }
                // 如果儲存格式是公式
                else if (cellData.CellType == NPOI.SS.UserModel.CellType.Formula)
                {
                    if (formatCode.EndsWith("%"))
                    {
                        double dataValue = cellData.NumericCellValue * 100;
                        string value = Math.Round(dataValue, 2, MidpointRounding.AwayFromZero).ToString("0.00") + "%";
                        ecData.cellValues.Add(value);
                    }
                    else
                    {
                        try
                        {
                            string value = string.Empty;
                            IFormulaEvaluator formulaEvaluator; // 運算公式
                            try
                            {
                                formulaEvaluator = new XSSFFormulaEvaluator(workbook);
                            }
                            catch
                            {
                                formulaEvaluator = new HSSFFormulaEvaluator(workbook);
                            }
                            var formulaValue = formulaEvaluator.Evaluate(cellData); // 公式計算值
                            if (formulaValue.CellType == NPOI.SS.UserModel.CellType.String)
                            {
                                value = formulaValue.StringValue.ToString();  // 執行公式後的值為字串型態
                            }
                            else if (formulaValue.CellType == NPOI.SS.UserModel.CellType.Numeric)
                            {
                                value = formulaValue.NumberValue.ToString();    // 執行公式後的值為數字型態
                            }
                            ecData.cellValues.Add(value);
                        }
                        catch (Exception)
                        {
                            ecData.cellValues.Add("資料錯誤");
                        }
                    }
                }
                else
                {
                    ecData.cellValues.Add(cellData.ToString());
                }
            }
            catch (NullReferenceException)
            {
                ecData.cellValues.Add("");
            }
            catch (InvalidOperationException)
            {
                ecData.cellValues.Add("");
            }

            return ecData;
        }
        // 從專案中找到全部的Title Blocks
        private void CreateFrames(Document doc, FamilySymbol familySymbol, List<ExcelCellData> ecDataList)
        {
            // 找到專案中現有的所有ViewSheet, 並儲存相對的圖紙號碼
            List<ViewSheetAndNumber> vsAndNumberList = new List<ViewSheetAndNumber>();
            List<ViewSheet> viewSheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).WhereElementIsNotElementType().Cast<ViewSheet>().ToList();
            foreach(ViewSheet viewSheet in viewSheets)
            {
                ViewSheetAndNumber vsAndNumber = new ViewSheetAndNumber();
                Parameter para = viewSheet.get_Parameter(BuiltInParameter.SHEET_NUMBER);
                string number = para.AsString();
                vsAndNumber.viewSheet = viewSheet;
                vsAndNumber.number = number;
                vsAndNumberList.Add(vsAndNumber);
            }

            // 將所有Excel的資料創建為視圖, 並填入參數資訊
            using (Transaction trans = new Transaction(doc, "圖紙更新"))
            {
                trans.Start();
                var ecDataSheetNames = (from x in ecDataList
                                        select x.sheetName).Distinct();
                foreach (string ecDataSheetName in ecDataSheetNames)
                {
                    var ecDataFilter = (from x in ecDataList
                                        where x.sheetName.Equals(ecDataSheetName)
                                        select x);
                    // 找到標頭列
                    int headerRow = 0;
                    int sheetCount = 0; // 圖號在第幾行
                    ExcelCellData headerCell = new ExcelCellData();
                    foreach (var ecData in ecDataFilter)
                    {
                        int check = 0;
                        for(int count = 0; count < ecData.cellValues.Count(); count++)
                        {
                            if (ecData.cellValues[count].Equals("Sheet Number") ||
                                ecData.cellValues[count].Equals("Sheet Name") ||
                                ecData.cellValues[count].Equals("Designed By") ||
                                ecData.cellValues[count].Equals("Checked By") ||
                                ecData.cellValues[count].Equals("Drawn By") ||
                                ecData.cellValues[count].Equals("Has Key Plan Block") ||
                                ecData.cellValues[count].Equals("圖框-單位"))
                            {
                                check++;
                                if (ecData.cellValues[count].Equals("Sheet Number"))
                                {
                                    sheetCount = count;
                                }
                            }
                        }
                        if(check >= 5)
                        {
                            ecData.header = true; // 此列為標頭
                            headerCell = ecData;
                            break;
                        }
                        headerRow++;
                    }
                    List<FamilyInstance> fiList = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>().ToList();
                    int i = 0;
                    foreach (var ecData in ecDataFilter)
                    {
                        if (i > headerRow)
                        {
                            try
                            {
                                if (familySymbol != null)
                                {
                                    ViewSheetAndNumber viewsheetAndNumber = (from x in vsAndNumberList
                                                                             where x.number.Equals(ecData.cellValues[sheetCount])
                                                                             select x).FirstOrDefault();
                                    ViewSheet viewSheet = null;
                                    FamilyInstance familyInstance = null;
                                    try // 若圖紙已有則更新目前Excel Cell資料
                                    {
                                        viewSheet = viewsheetAndNumber.viewSheet;
                                        foreach (FamilyInstance fi in fiList)
                                        {
                                            if(fi is FamilyInstance)
                                            {
                                                try
                                                {
                                                    string sheetNumber = fi.get_Parameter(BuiltInParameter.SHEET_NUMBER).AsString();
                                                    if (sheetNumber == viewsheetAndNumber.number)
                                                    {
                                                        familyInstance = fi;
                                                        break;
                                                    }
                                                }
                                                catch (NullReferenceException)
                                                {

                                                }
                                            }
                                        }
                                    }
                                    catch (NullReferenceException) // 若圖紙尚無, 則先建立新的ViewSheet
                                    {
                                        viewSheet = ViewSheet.Create(doc, familySymbol.Id);
                                        foreach (FamilyInstance fi in fiList)
                                        {
                                            if (fi is FamilyInstance)
                                            {
                                                try
                                                {
                                                    string sheetNumber = fi.get_Parameter(BuiltInParameter.SHEET_NUMBER).AsString();
                                                    if (sheetNumber == viewsheetAndNumber.number)
                                                    {
                                                        familyInstance = fi;
                                                        break;
                                                    }
                                                }
                                                catch (NullReferenceException)
                                                {

                                                }
                                            }
                                        }
                                        if (null == viewSheet)
                                        {
                                            throw new Exception("新增圖紙失敗.");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        TaskDialog.Show("Revit", ex.Message + "\n" + i + " : " + ecData.cellValues[sheetCount]);
                                    }
                                    // 將參數填入圖框內
                                    Parameter para = null;
                                    int headerCount = 0;
                                    foreach (string cellString in headerCell.cellValues)
                                    {
                                        try
                                        {
                                            if (cellString.Equals("Sheet Number"))
                                            {
                                                para = viewSheet.get_Parameter(BuiltInParameter.SHEET_NUMBER); // 圖紙號碼
                                                para.Set(ecData.cellValues[headerCount]);
                                            }
                                            else if (cellString.Equals("Sheet Name"))
                                            {
                                                para = viewSheet.get_Parameter(BuiltInParameter.SHEET_NAME); // 圖紙名稱
                                                para.Set(ecData.cellValues[headerCount]);
                                            }
                                            else if (cellString.Equals("Designed By"))
                                            {
                                                para = viewSheet.get_Parameter(BuiltInParameter.SHEET_DESIGNED_BY); // 設計
                                                para.Set(ecData.cellValues[headerCount]);
                                            }
                                            else if (cellString.Equals("Checked By"))
                                            {
                                                para = viewSheet.get_Parameter(BuiltInParameter.SHEET_CHECKED_BY); // 初核
                                                para.Set(ecData.cellValues[headerCount]);
                                            }
                                            else if (cellString.Equals("Drawn By"))
                                            {
                                                para = viewSheet.get_Parameter(BuiltInParameter.SHEET_DRAWN_BY); // 繪圖
                                                para.Set(ecData.cellValues[headerCount]);
                                            }
                                            else if (cellString.Equals("Has Key Plan Block")) // 有無索引圖
                                            {
                                                Parameter fiPara = familyInstance.LookupParameter(cellString);
                                                int trueOrFalse = Convert.ToInt32(ecData.cellValues[headerCount]);
                                                fiPara.Set(trueOrFalse);
                                            }
                                            else if (cellString.Equals("圖框-單位")) // 單位
                                            {
                                                para = viewSheet.LookupParameter(cellString);
                                                para.Set(ecData.cellValues[headerCount]);
                                                Parameter fiPara = familyInstance.LookupParameter("Unit");
                                                fiPara.Set(ecData.cellValues[headerCount]);
                                            }
                                            else
                                            {
                                                para = viewSheet.LookupParameter(cellString);
                                                para.Set(ecData.cellValues[headerCount]);
                                            }
                                        }
                                        catch (ArgumentOutOfRangeException)
                                        {
                                            para.Set("");
                                        }
                                        catch (Exception)
                                        {
                                            
                                        }
                                        headerCount++;
                                    }
                                }
                            }
                            catch (Exception)
                            {

                            }
                        }
                        i++;
                    }
                }
                trans.Commit();
            }
        }
        // 字串數據計算, 先乘除後加減
        private static double Evaluate(string calculator)
        {
            DataTable table = new DataTable();
            table.Columns.Add("myExpression", string.Empty.GetType(), calculator);
            DataRow row = table.NewRow();
            table.Rows.Add(row);

            return double.Parse((string)row["myExpression"]);
        }
    }
}
