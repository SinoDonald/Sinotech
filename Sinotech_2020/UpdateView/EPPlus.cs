using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace Sinotech_2020.UpdateView
{
    // 儲存Excel內Sheet的資料
    public class SheetHeader
    {
        public string sheet { get; set; } // Sheet名稱
        public int headerRow = 0; // 標頭列
        public List<string> header = new List<string>(); // 標題名稱
        public List<string> paraValue = new List<string>(); // 參數讀取
        public List<string> sheetNumbers = new List<string>(); // 所有圖號
    }
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
    public class EPPlus
    {
        // 讀取Excel內容
        public Tuple<List<string>, List<ExcelCellData>> ReadExcel(string filePath)
        {
            List<ExcelCellData> ecDataList = new List<ExcelCellData>();
            List<string> sheetNames = new List<string>();

            FileInfo existingFile = new FileInfo(filePath); //開啟已存在的Excel檔案            
            ExcelPackage package = new ExcelPackage(existingFile); // new一個包存取.xlsx檔
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial; //因為EPPlus 升版和授權,需要加這行            
            ExcelWorksheets worksheets = package.Workbook.Worksheets; // 讀取所有的sheet
            foreach (ExcelWorksheet worksheet in worksheets)
            {
                string sheetName = worksheet.Name;
                sheetNames.Add(sheetName);

                // 取得所選sheet的列數和行數
                int rows = worksheet.Dimension.End.Row;
                int cols = worksheet.Dimension.End.Column;
                // 讀取資料放入DataTable
                DataTable dt = new DataTable(worksheet.Name);
                DataRow dr = null;
                int sheetNumber = 0; // 找到Sheet Number的列
                // ExcelWorksheet在.net framwork 的index從1開始;在.net從0開始
                for (int i = 1; i <= rows; i++)
                {
                    ExcelCellData ecData = new ExcelCellData();
                    ecData.sheetName = sheetName; // Sheet名稱
                    ecData.rowCount = i; // 第幾列
                    if (i > 1)
                    {
                        dr = dt.Rows.Add();
                    }
                    for (int j = 1; j <= cols; j++)
                    {
                        try
                        {
                            var value = worksheet.Cells[i, j].Value;
                            if (value == null) { value = ""; }
                            else if (value.ToString() == "Sheet Number") { sheetNumber = j; }
                            ecData.cellValues.Add(value.ToString());
                        }
                        catch (Exception ex)
                        {
                            string error = ex.Message + "\n" + ex.ToString();
                        }
                    }
                    // 圖紙號碼不為空才存入ecDataList中
                    if (!String.IsNullOrEmpty(ecData.cellValues[sheetNumber].ToString()))
                    {
                        ecDataList.Add(ecData);
                    }
                }
            }

            return new Tuple<List<string>, List<ExcelCellData>>(sheetNames, ecDataList);
        }
        // 匯出Excel
        public void ExportExcel(string filePath, List<string> vsSheets, List<ExcelCellData> excelRowList)
        {
            List<Tuple<string, DataTable>> sheetAndDatas = new List<Tuple<string, DataTable>>();
            foreach (string vsSheet in vsSheets)
            {
                //建立datatable
                DataTable dt = new DataTable();
                int createHeader = 0;
                List<string> vsHeadle = (from x in excelRowList
                                         where x.sheetName.Equals(vsSheet)
                                         select x.header).FirstOrDefault();
                foreach (string headleContent in vsHeadle)
                {
                    if (headleContent.Equals("")) { dt.Columns.Add("", typeof(string)); }
                    else { dt.Columns.Add(new DataColumn(headleContent, typeof(string))); }                    
                    createHeader++;
                }

                int createCell = 0;
                DataRow dr = dt.NewRow();
                List<string> vsParaValue = (from x in excelRowList
                                            where x.sheetName.Equals(vsSheet)
                                            select x.paraValue).FirstOrDefault();
                foreach (string paraValue in vsParaValue)
                {
                    dr[createCell] = paraValue;
                    createCell++;
                }
                dt.Rows.Add(dr);

                int createRow = 2;
                List<ExcelCellData> createExcelCell = (from x in excelRowList
                                                       where x.sheetName.Equals(vsSheet)
                                                       select x).ToList();
                foreach (ExcelCellData excelCellData in createExcelCell)
                {
                    try
                    {
                        createCell = 0;
                        dr = dt.NewRow();
                        foreach (string cellContent in excelCellData.cellValues)
                        {
                            dr[createCell] = cellContent;
                            createCell++;
                        }
                        dt.Rows.Add(dr);
                        createRow++;
                    }
                    catch (Exception)
                    {

                    }
                }
                sheetAndDatas.Add(new Tuple<string, DataTable>(vsSheet, dt));
            }
            using (ExcelPackage package = new ExcelPackage())
            {
                foreach(Tuple<string, DataTable> sheetAndData in sheetAndDatas)
                {
                    // 新增worksheet
                    ExcelWorksheet worksheet = package.Workbook.Worksheets.Add(sheetAndData.Item1);
                    //新增DataTable到sheet
                    worksheet.Cells["A1"].LoadFromDataTable(sheetAndData.Item2, true);
                }

                package.SaveAs(filePath);
            }
        }
    }
}