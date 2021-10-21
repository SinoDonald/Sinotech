using System;
using System.Collections.Generic;
using System.IO;

using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.Model;
using NPOI.XSSF.UserModel;

namespace Sinotech
{
    class ReadExcel
    {
        // 儲存Excel內Sheet的資料
        public class ExcelCellData
        {
            public string sheetName = string.Empty; // Sheet名稱
            public int rowCount = 0; // 第幾列
            public List<string> cellValues = new List<string>(); // 值
        }

        // 讀取Excel Sheet
        public static List<string> ReadExcelSheet(string filePath)
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
            for(int i = 0; i < sheetCounts; i++)
            {
                sheetNames.Add(workbook.GetSheetName(i));
            }

            return sheetNames;
        }
        // 讀取Excel資料
        public static List<ExcelCellData> ReadSheetData(string excelPath)
        {
            List<ExcelCellData> ecDataList = new List<ExcelCellData>();
            //List<string> sheetNames = new List<string>(); // Sheet的名稱
            List<string> sheetNames = ReadExcelSheet(excelPath);

            Stream stream = null;
            IWorkbook workbook = null;
            ISheet sheet = null;//上邊這幾行都是固定格式，如果你不深究，記著就行
            ExcelCellData ecData = new ExcelCellData();
            foreach (string sheetName in sheetNames)
            {
                try // Excel 2007以後
                {
                    stream = new FileStream(excelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    workbook = new XSSFWorkbook(stream);
                    sheet = (XSSFSheet)workbook.GetSheet(sheetName)/*.GetSheetAt(0)*/;
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
                        if(row != null)
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
                                    ecData = SaveFormatCellData(workbook, cellData, ecData);  // 判別Cell格式後, 並儲存到ecData
                                }

                                ecDataList.Add(ecData);
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
        private static ExcelCellData SaveFormatCellData(IWorkbook workbook, ICell cellData, ExcelCellData ecData)
        {
            try
            {
                StylesTable st = ((XSSFWorkbook)workbook).GetStylesSource();
                XSSFDataFormat df = new XSSFDataFormat(st);
                string formatCode = df.GetFormat(cellData.CellStyle.DataFormat);
                // 如果儲存格式是數值
                if (cellData.CellType == CellType.Numeric)
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
                else if (cellData.CellType == CellType.Formula)
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
                            if (formulaValue.CellType == CellType.String)
                            {
                                value = formulaValue.StringValue.ToString();  // 執行公式後的值為字串型態
                            }                                
                            else if (formulaValue.CellType == CellType.Numeric)
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
        // 清除sheet內的所有row
        public static void removeRow(ISheet sheet, int startRow)
        {
            int lastRowIndex = -1;
            if (sheet.PhysicalNumberOfRows > 0)
            {
                lastRowIndex = sheet.LastRowNum;

                for (; lastRowIndex >= startRow; lastRowIndex--)
                {
                    IRow row = sheet.GetRow(lastRowIndex);
                    if (row != null)
                    {                        
                        sheet.RemoveRow(row);
                    }
                }
            }
        }
    }
}
