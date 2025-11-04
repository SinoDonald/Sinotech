using Autodesk.Revit.UI;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Collections.Generic;
using System.IO;
using static Sinotech_2020.OutPutPCCES;

namespace Sinotech_2020
{
    public class UpdateExcel
    {
        // A1、A2、A3配合各標名稱
        public List<ItemDescription> ItemDescriptions = new List<ItemDescription>();
        // 讀取Excel內容
        public List<OpeningContrast> ReadExcel(string filePath)
        {
            List<OpeningContrast> openingContrastList = new List<OpeningContrast>();

            FileInfo existingFile = new FileInfo(filePath); //開啟已存在的Excel檔案            
            ExcelPackage package = new ExcelPackage(existingFile); // new一個包存取.xlsx檔
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial; //因為EPPlus 升版和授權,需要加這行            
            ExcelWorksheets worksheets = package.Workbook.Worksheets; // 讀取所有的sheet
            foreach (ExcelWorksheet worksheet in worksheets)
            {
                if (worksheet.Name.Equals("detail"))
                {
                    for (int i = 3; i <= 5; i++)
                    {
                        try
                        {
                            ItemDescription itemDescription = new ItemDescription();
                            itemDescription.item = worksheet.Cells[i, 1].Value.ToString();
                            itemDescription.description = worksheet.Cells[i, 2].Value.ToString();
                            ItemDescriptions.Add(itemDescription);
                        }
                        catch (Exception ex)
                        {
                            string error = ex.Message + "\n\n" + ex.ToString();
                        }
                    }
                }
                if (worksheet.Name.Equals("All"))
                {
                    // 取得所選sheet的列數和行數
                    int rows = worksheet.Dimension.End.Row;
                    int cols = worksheet.Dimension.End.Column;
                    for (int i = 3; i < rows; i++)
                    {
                        if (worksheet.Cells[i, 3].Value != null)
                        {
                            try
                            {
                                OpeningContrast openingContrast = new OpeningContrast();
                                openingContrast.name = worksheet.Cells[i, 3].Value.ToString(); // 章名
                                // Type
                                if (openingContrast.name.Contains("套管"))
                                {
                                    openingContrast.type = "套管";
                                }
                                else if (openingContrast.name.Contains("開口"))
                                {
                                    openingContrast.type = "開口";
                                }
                                else if (openingContrast.name.Contains("基座"))
                                {
                                    openingContrast.type = "基座";
                                }
                                else if (openingContrast.name.Contains("導線管"))
                                {
                                    openingContrast.type = "導線管";
                                }
                                else if (openingContrast.name.Contains("電機接線盒"))
                                {
                                    openingContrast.type = "電機接線盒";
                                }
                                else if (openingContrast.name.Contains("金屬導線槽"))
                                {
                                    openingContrast.type = "金屬導線槽";
                                }
                                // Host
                                if (openingContrast.name.Contains("牆"))
                                {
                                    openingContrast.host = "牆";
                                }
                                else if (openingContrast.name.Contains("樑"))
                                {
                                    openingContrast.host = "樑";
                                }
                                else if (openingContrast.name.Contains("樓板") || openingContrast.name.Contains("樓版"))
                                {
                                    openingContrast.host = "樓板";
                                }
                                // 管徑&面積&體積
                                if (openingContrast.name.Contains("標稱"))
                                {
                                    int startIndex = openingContrast.name.IndexOf("標稱") + 2;
                                    int endIndex = openingContrast.name.IndexOf("mm");
                                    string value = openingContrast.name.Substring(startIndex, endIndex - startIndex);
                                    openingContrast.diameter = Convert.ToDouble(value);
                                }
                                else if (openingContrast.name.Contains("面積") || openingContrast.name.Contains("體積"))
                                {
                                    int startIndex = 0;
                                    int endIndex = 0;
                                    string value = string.Empty;
                                    double min = 0.0;
                                    double max = 0.0;
                                    if (openingContrast.name.Contains("＜"))
                                    {
                                        startIndex = openingContrast.name.LastIndexOf("，") + 1;
                                        endIndex = openingContrast.name.IndexOf("m2");
                                        if (endIndex.Equals(-1))
                                        {
                                            endIndex = openingContrast.name.IndexOf("m3");
                                        }
                                        value = openingContrast.name.Substring(startIndex, endIndex - startIndex);
                                        min = Convert.ToDouble(value);
                                        openingContrast.min = min;
                                    }
                                    if (openingContrast.name.Contains("≦"))
                                    {
                                        startIndex = openingContrast.name.IndexOf("≦") + 1;
                                        endIndex = openingContrast.name.LastIndexOf("m2");
                                        if (endIndex.Equals(-1))
                                        {
                                            endIndex = openingContrast.name.LastIndexOf("m3");
                                        }
                                        value = openingContrast.name.Substring(startIndex, endIndex - startIndex);
                                        max = Convert.ToDouble(value);
                                        openingContrast.max = max;
                                    }
                                }
                                // 工程項目編號
                                openingContrast.prjNumber = worksheet.Cells[i, 8].Value.ToString();
                                openingContrastList.Add(openingContrast);
                            }
                            catch (NullReferenceException)
                            {

                            }
                            catch (Exception)
                            {

                            }
                        }
                    }
                }
            }

            return openingContrastList;
        }
        // 寫入Excel
        public string WriteExcelCell(string filePath, string sheetName, List<ExcelCellData> ecDataList)
        {
            FileInfo existingFile = new FileInfo(filePath); //開啟已存在的Excel檔案            
            ExcelPackage package = new ExcelPackage(existingFile); // new一個包存取.xlsx檔
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial; //因為EPPlus 升版和授權,需要加這行            
            ExcelWorksheets worksheets = package.Workbook.Worksheets; // 讀取所有的sheet
            foreach (ExcelWorksheet worksheet in worksheets)
            {
                if(worksheet.Name.Equals(sheetName))
                {
                    // 取得所選sheet的列數和行數
                    int rows = worksheet.Dimension.End.Row;
                    for (; rows >= 10; rows--)
                    {
                        worksheet.DeleteRow(rows);
                    }
                    // 讀取專案中所有圖紙資訊
                    int createCell = 1;
                    int createParaValue = 9;
                    List<string> vsParaValue = new List<string>();
                    vsParaValue.Add("項次");
                    vsParaValue.Add("項目及說明");
                    vsParaValue.Add("單位");
                    vsParaValue.Add("數量");
                    if (sheetName.Equals("工程數量計算表"))
                    {
                        vsParaValue.Add("計算式(含必要簡圖及說明)");
                        vsParaValue.Add("圖號(SEM)");
                    }
                    else if (sheetName.Equals("工程數量詳細表"))
                    {
                        vsParaValue.Add("數量(彙總計算)");
                        vsParaValue.Add("參考頁次");
                    }
                    vsParaValue.Add("工程項目編號");
                    foreach (string paraValue in vsParaValue)
                    {                        
                        worksheet.Cells[createParaValue, createCell].Value = paraValue;
                        worksheet.Cells[createParaValue, createCell].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[createParaValue, createCell].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center; // 文字置中
                        // 背景色
                        worksheet.Cells[createParaValue, createCell].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                        // 加框線
                        worksheet.Cells[createParaValue, createCell].Style.Border.Top.Style = ExcelBorderStyle.Thin; //上框線
                        worksheet.Cells[createParaValue, createCell].Style.Border.Bottom.Style = ExcelBorderStyle.Thin; //下框線
                        worksheet.Cells[createParaValue, createCell].Style.Border.Left.Style = ExcelBorderStyle.Thin; //左框線
                        worksheet.Cells[createParaValue, createCell].Style.Border.Right.Style = ExcelBorderStyle.Thin; //右框線
                        createCell++;
                    }

                    int createRow = 10;
                    foreach (ExcelCellData excelCellData in ecDataList)
                    {
                        try
                        {
                            createCell = 1;
                            foreach (var cellContent in excelCellData.cellValues)
                            {
                                worksheet.Cells[createRow, createCell].Value = cellContent;
                                if (!createCell.Equals(2)) { worksheet.Cells[createRow, createCell].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center; } // 文字置中
                                // 加框線
                                worksheet.Cells[createRow, createCell].Style.Border.Top.Style = ExcelBorderStyle.Thin; //上框線
                                worksheet.Cells[createRow, createCell].Style.Border.Bottom.Style = ExcelBorderStyle.Thin; //下框線
                                worksheet.Cells[createRow, createCell].Style.Border.Left.Style = ExcelBorderStyle.Thin; //左框線
                                worksheet.Cells[createRow, createCell].Style.Border.Right.Style = ExcelBorderStyle.Thin; //右框線
                                createCell++;
                            }
                            createRow++;
                        }
                        catch (Exception)
                        {

                        }
                    }
                }
            }
            try
            {
                if (sheetName.Equals("工程數量計算表"))
                {
                    int pathIndex = filePath.LastIndexOf('\\');
                    int fileIndex = filePath.LastIndexOf('.');
                    string now = DateTime.Now.ToString("yyyyMMddhhmm");
                    string fileName = filePath.Substring(pathIndex + 1, fileIndex - pathIndex - 1);
                    filePath = filePath.Substring(0, pathIndex) + "\\" + fileName + "_" + now + ".xlsx";
                    //建立檔案
                    using (FileStream createStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                    {
                        package.SaveAs(createStream);
                    }
                }
                else if (sheetName.Equals("工程數量詳細表"))
                {
                    //建立檔案
                    using (FileStream createStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                    {
                        package.SaveAs(createStream);
                    }
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Revit", ex.ToString() + "\n\n" + ex.Message);
            }

            return filePath;
        }
    }
}
