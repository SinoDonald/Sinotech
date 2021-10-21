using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.Model;
using NPOI.XSSF.UserModel;

namespace Sinotech
{
    [Transaction(TransactionMode.Manual)]
    public class UpdateExcelCell : IExternalCommand
    {
        // 儲存Excel內所有Cell的資料
        public class ExcelCellData
        {
            public string sheetName = string.Empty; // Sheet名稱
            public int rowCount = 0; // 第幾列
            public List<string> cellValues = new List<string>(); // 值
            public bool isheader = false; // 是否為標頭
            public List<string> header = new List<string>(); // 標題名稱
            public List<string> paraValue = new List<string>(); // 參數讀取
        }
        // 儲存Excel內Sheet的資料
        public class SheetHeader
        {
            public string sheet { get; set; } // Sheet名稱
            public int headerRow = 0; // 標頭列
            public List<string> header = new List<string>(); // 標題名稱
            public List<string> paraValue = new List<string>(); // 參數讀取
            public List<string> sheetNumbers = new List<string>(); // 所有圖號
        }
        public static List<ExcelCellData> ecDataList = new List<ExcelCellData>(); // 將Excel中Sheet的Cell資料都撈出來
        List<SheetHeader> sheetHeaderList = new List<SheetHeader>(); // 儲存各Sheet的標頭名稱與圖號
        List<ExcelCellData> excelRowList = new List<ExcelCellData>(); // 儲存所有圖紙的參數

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
                    List<string> sheetNames = ExcelSheet(excelPath); // Excel中全部的Sheet
                    ecDataList = ExcelSheetData(sheetNames, excelPath); // 將Excel中Sheet的Cell資料都撈出來
                    HeaderAndSheetNumber(sheetNames); // 讀取Excel中, 所有的標頭與SheetNumber

                    List<ViewSheet> viewSheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).WhereElementIsNotElementType().Cast<ViewSheet>().ToList();
                    List<FamilyInstance> fiList = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>().ToList();
                    foreach (ViewSheet viewSheet in viewSheets)
                    {
                        if (viewSheet is ViewSheet)
                        {
                            try
                            {
                                FamilyInstance familyInstance = null;
                                Parameter vsPara = null;
                                // 找到ViewSheet的圖框FamilyInstance
                                foreach (FamilyInstance fi in fiList)
                                {
                                    if (fi is FamilyInstance)
                                    {
                                        try
                                        {
                                            string sheetNumber = fi.get_Parameter(BuiltInParameter.SHEET_NUMBER).AsString();
                                            if (sheetNumber == viewSheet.SheetNumber)
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
                                // 判斷ViewSheet Number是否在Excel內, 若有, 使用Excel內的Sheet標題參數, 沒有則儲存在"New" Sheet
                                SheetHeader sheetHeader = new SheetHeader();
                                foreach (SheetHeader sh in sheetHeaderList)
                                {
                                    foreach (string sheetNumber in sh.sheetNumbers)
                                    {
                                        if (sheetNumber.Equals(viewSheet.SheetNumber))
                                        {
                                            sheetHeader.sheetNumbers.Add(sheetNumber);
                                            sheetHeader.header = sh.header;
                                            sheetHeader.paraValue = sh.paraValue;
                                            sheetHeader.sheet = sh.sheet;
                                            break;
                                        }
                                    }
                                }
                                if (sheetHeader.sheet == null)
                                {
                                    sheetHeader.sheetNumbers.Add(viewSheet.SheetNumber);
                                    sheetHeader.header = sheetHeaderList[0].header;
                                    sheetHeader.paraValue = sheetHeaderList[0].paraValue;
                                    sheetHeader.sheet = "New";
                                }
                                // 比對Excel中所需要的參數名稱
                                ExcelCellData excelCelldata = new ExcelCellData();
                                excelCelldata.sheetName = sheetHeader.sheet;
                                excelCelldata.header = sheetHeader.header;
                                excelCelldata.paraValue = sheetHeader.paraValue;
                                foreach (string paraName in sheetHeader.paraValue) // 找到要存的參數
                                {
                                    try
                                    {
                                        if (paraName.Equals("Sheet Number"))
                                        {
                                            vsPara = viewSheet.get_Parameter(BuiltInParameter.SHEET_NUMBER); // 圖紙號碼
                                            excelCelldata.cellValues.Add(vsPara.AsString());
                                        }
                                        else if (paraName.Equals("Sheet Name"))
                                        {
                                            vsPara = viewSheet.get_Parameter(BuiltInParameter.SHEET_NAME); // 圖紙名稱
                                            excelCelldata.cellValues.Add(vsPara.AsString());
                                        }
                                        else if (paraName.Equals("Designed By"))
                                        {
                                            vsPara = viewSheet.get_Parameter(BuiltInParameter.SHEET_DESIGNED_BY); // 設計
                                            excelCelldata.cellValues.Add(vsPara.AsString());
                                        }
                                        else if (paraName.Equals("Checked By"))
                                        {
                                            vsPara = viewSheet.get_Parameter(BuiltInParameter.SHEET_CHECKED_BY); // 初核
                                            excelCelldata.cellValues.Add(vsPara.AsString());
                                        }
                                        else if (paraName.Equals("Drawn By"))
                                        {
                                            vsPara = viewSheet.get_Parameter(BuiltInParameter.SHEET_DRAWN_BY); // 繪圖
                                            excelCelldata.cellValues.Add(vsPara.AsString());
                                        }
                                        else if (paraName.Equals("Has Key Plan Block")) // 有無索引圖
                                        {
                                            Parameter fiPara = familyInstance.LookupParameter(paraName);
                                            int trueOrFalse = fiPara.AsInteger();
                                            excelCelldata.cellValues.Add(trueOrFalse.ToString());
                                        }
                                        else if (paraName.Equals("圖框-單位")) // 單位
                                        {
                                            //vsPara = viewSheet.LookupParameter(paraName);
                                            //excelCelldata.cellValues.Add(vsPara.AsString());
                                            Parameter fiPara = familyInstance.LookupParameter("Unit");
                                            excelCelldata.cellValues.Add(fiPara.AsString());
                                        }
                                        else
                                        {
                                            vsPara = viewSheet.LookupParameter(paraName);
                                            excelCelldata.cellValues.Add(vsPara.AsString());
                                        }
                                    }
                                    catch (NullReferenceException)
                                    {
                                        excelCelldata.cellValues.Add("");
                                    }
                                    catch (ArgumentOutOfRangeException)
                                    {
                                        vsPara.Set("");
                                    }
                                    catch (Exception)
                                    {

                                    }
                                }
                                if (excelCelldata.cellValues.Count > 0)
                                {
                                    excelRowList.Add(excelCelldata);
                                }
                            }
                            catch (Exception)
                            {

                            }
                        }
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

                // 讀取專案中所有圖紙資訊
                var vsSheets = (from x in excelRowList
                                select x.sheetName).Distinct();
                //建立Excel 2007檔案, 創建Sheet
                IWorkbook wb = new XSSFWorkbook();
                foreach (var vsSheet in vsSheets)
                {
                    ISheet ws = wb.CreateSheet(vsSheet);
                    int createHeader = 0;
                    ws.CreateRow(createHeader); // 第一行為欄位名稱
                    var vsHeadle = (from x in excelRowList
                                    where x.sheetName.Equals(vsSheet)
                                    select x.header).FirstOrDefault();
                    foreach (string headleContent in vsHeadle)
                    {
                        ws.GetRow(0).CreateCell(createHeader).SetCellValue(headleContent);
                        createHeader++;
                    }

                    int createCell = 0;
                    int createParaValue = 1;
                    ws.CreateRow(createParaValue); // 要查詢的參數
                    var vsParaValue = (from x in excelRowList
                                    where x.sheetName.Equals(vsSheet)
                                    select x.paraValue).FirstOrDefault();
                    foreach (string paraValue in vsParaValue)
                    {
                        ws.GetRow(1).CreateCell(createCell).SetCellValue(paraValue);
                        createCell++;
                    }

                    var createExcelCell = (from x in excelRowList
                                           where x.sheetName.Equals(vsSheet)
                                           select x);
                    int createRow = 2;
                    foreach (ExcelCellData excelCellData in createExcelCell)
                    {
                        try
                        {
                            createCell = 0;
                            ws.CreateRow(createRow); 
                            foreach (var cellContent in excelCellData.cellValues)
                            {
                                ws.GetRow(createRow).CreateCell(createCell).SetCellValue(cellContent);
                                createCell++;
                            }
                            createRow++;
                        }
                        catch(Exception)
                        {

                        }
                    }
                }
                try
                {
                    int pathIndex = excelPath.LastIndexOf('\\');
                    int fileIndex = excelPath.LastIndexOf('.');
                    string now = DateTime.Now.ToString("yyyyMMddhhmm");
                    string fileName = excelPath.Substring(pathIndex + 1, fileIndex - pathIndex - 1);
                    excelPath = excelPath.Substring(0, pathIndex) + "\\" + fileName + "_" + now + ".xlsx";
                    FileStream file = new FileStream(excelPath, FileMode.Create); //產生檔案
                    wb.Write(file);
                    file.Close();
                    TaskDialog.Show("Revit", "完成");
                }
                catch(Exception ex)
                {
                    TaskDialog.Show("Revit", ex.ToString() + "\n\n" + ex.Message);
                }
            }

            return Result.Succeeded;
        }
        // 讀取Excel Sheet
        private static List<string> ExcelSheet(string filePath)
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
        // 讀取Excel中, 所有的標頭與SheetNumber
        private void HeaderAndSheetNumber(List<string> sheetNames)
        {
            foreach (string sheetName in sheetNames)
            {
                SheetHeader sheetHeader = new SheetHeader();
                List<ExcelCellData> ecDataFilter = (from x in ecDataList
                                                    where x.sheetName.Equals(sheetName)
                                                    select x).ToList();
                int snIndex = 0; // Sheet Number是第幾行
                int i = 0;
                // 找到各Sheet的標頭列
                foreach (ExcelCellData ecData in ecDataFilter)
                {
                    int check = 0;
                    for (int count = 0; count < ecData.cellValues.Count(); count++)
                    {
                        if (ecData.cellValues[count].Equals("Sheet Number") ||
                            ecData.cellValues[count].Equals("Sheet Name") ||
                            ecData.cellValues[count].Equals("Designed By") ||
                            ecData.cellValues[count].Equals("Checked By") ||
                            ecData.cellValues[count].Equals("Drawn By") ||
                            ecData.cellValues[count].Equals("Has Key Plan Block") ||
                            ecData.cellValues[count].Equals("圖框-單位"))
                        {
                            if (ecData.cellValues[count].Equals("Sheet Number"))
                            {
                                snIndex = count;
                            }
                            check++;
                        }
                    }
                    if (check >= 5)
                    {
                        sheetHeader.sheet = sheetName; // Sheet名稱
                        sheetHeader.headerRow = i; // 標頭是第幾列
                        sheetHeader.header = ecDataFilter[0].cellValues;
                        foreach (string data in ecData.cellValues)
                        {
                            sheetHeader.paraValue.Add(data);
                        }
                        for (int headerRow = sheetHeader.headerRow + 1; headerRow < ecDataFilter.Count(); headerRow++)
                        {
                            sheetHeader.sheetNumbers.Add(ecDataFilter[headerRow].cellValues[snIndex]); // 儲存Excel中所有圖號
                        }
                        sheetHeaderList.Add(sheetHeader);
                        break;
                    }
                    i++;
                }
            }
        }
    }
}
