using Autodesk.Revit.UI;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using System;
using System.Collections.Generic;
using System.IO;
using static CSDSEM.OutPutPCCES;

namespace CSDSEM
{
    public class UpdateExcel
    {
        // A1、A2、A3配合各標名稱
        public List<ItemDescription> ItemDescriptions = new List<ItemDescription>();
        // 讀取Excel All Sheet
        public List<OpeningContrast> NameAndNumber(string filePath, List<OpeningContrast> openingContrastList)
        {
            //讀取專案內中的sample.xls 的excel 檔案
            Stream stream = null;
            IWorkbook workbook = null;
            ISheet sheet = null;
            // 07年以後的版本使用XSSFWorkbook和XSSFSheet，03年以前的使用HSSFWorkbook和HSSFSheet
            try
            {
                stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                workbook = new XSSFWorkbook(stream);
                sheet = workbook.GetSheet("detail");
            }
            catch (Exception)
            {
                stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                workbook = new HSSFWorkbook(stream);
                sheet = workbook.GetSheet("detail");
            }
            IRow row = null;
            int lastRowIndex = -1;
            if (sheet.PhysicalNumberOfRows > 0)
            {
                lastRowIndex = sheet.LastRowNum; // 讀取row所涵蓋的範圍
                for (int i = 2; i <= 4; i++)
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
                        try
                        {
                            ItemDescription itemDescription = new ItemDescription();
                            itemDescription.item = row.GetCell(0).StringCellValue;
                            itemDescription.description = row.GetCell(1).StringCellValue;
                            ItemDescriptions.Add(itemDescription);
                        }
                        catch(Exception ex)
                        {
                            string error = ex.Message + "\n\n" + ex.ToString();
                        }
                    }
                }
            }
            
            try
            {
                stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                workbook = new XSSFWorkbook(stream);
                sheet = workbook.GetSheet("All");
            }
            catch (Exception)
            {
                stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                workbook = new HSSFWorkbook(stream);
                sheet = workbook.GetSheet("All");
            }
            row = null;
            lastRowIndex = -1;
            if (sheet.PhysicalNumberOfRows > 0)
            {
                lastRowIndex = sheet.LastRowNum; // 讀取row所涵蓋的範圍
                for (int i = 2; i <= lastRowIndex; i++)
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
                        try
                        {
                            OpeningContrast openingContrast = new OpeningContrast();
                            openingContrast.name = row.GetCell(2).StringCellValue; // 章名
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
                            openingContrast.prjNumber = row.GetCell(7).StringCellValue;
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

            return openingContrastList;
        }
        // 讀取Excel Cell
        public string WriteExcelCell(string filePath, string sheetName, List<ExcelCellData> ecDataList)
        {
            //讀取專案內中的sample.xls 的excel 檔案
            Stream stream = null;
            IWorkbook workbook = null;
            ISheet sheet = null;
            // 07年以後的版本使用XSSFWorkbook和XSSFSheet，03年以前的使用HSSFWorkbook和HSSFSheet
            try
            {
                stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                workbook = new XSSFWorkbook(stream);
                sheet = workbook.GetSheet(sheetName);
            }
            catch (Exception)
            {
                stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                workbook = new HSSFWorkbook(stream);
                sheet = workbook.GetSheet(sheetName);
            }
            // 移除Cell內文, 重新填入新資料
            RemoveRow(sheet, 9);

            // 讀取專案中所有圖紙資訊
            int createCell = 0;
            int createParaValue = 8;
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
                sheet.GetRow(createParaValue).GetCell(createCell).SetCellValue(paraValue);
                XSSFCellStyle cs = (XSSFCellStyle)workbook.CreateCellStyle();
                ////設定背景顏色
                //cs.FillForegroundColor = NPOI.HSSF.Util.HSSFColor.Grey40Percent.Index;
                //cs.FillPattern = NPOI.SS.UserModel.FillPattern.SolidForeground; //灰色
                // 設定框線為"細實線"
                cs.BorderTop = NPOI.SS.UserModel.BorderStyle.Thin;
                cs.BorderBottom = NPOI.SS.UserModel.BorderStyle.Thin;
                cs.BorderLeft = NPOI.SS.UserModel.BorderStyle.Thin;
                cs.BorderRight = NPOI.SS.UserModel.BorderStyle.Thin;
                //文字置中
                cs.VerticalAlignment = NPOI.SS.UserModel.VerticalAlignment.Center;
                cs.Alignment = NPOI.SS.UserModel.HorizontalAlignment.Center;
                sheet.GetRow(createParaValue).GetCell(createCell).CellStyle = cs;
                createCell++;
            }

            int createRow = 9;
            foreach (ExcelCellData excelCellData in ecDataList)
            {
                try
                {
                    createCell = 0;
                    sheet.CreateRow(createRow);
                    foreach (var cellContent in excelCellData.cellValues)
                    {
                        sheet.GetRow(createRow).CreateCell(createCell).SetCellValue(cellContent);
                        // 設定上下左右的框線為"細實線"
                        XSSFCellStyle cs = (XSSFCellStyle)workbook.CreateCellStyle();
                        cs.BorderTop = NPOI.SS.UserModel.BorderStyle.Thin;
                        cs.BorderBottom = NPOI.SS.UserModel.BorderStyle.Thin;
                        cs.BorderLeft = NPOI.SS.UserModel.BorderStyle.Thin;
                        cs.BorderRight = NPOI.SS.UserModel.BorderStyle.Thin;
                        if (!createCell.Equals(1))
                        {                            
                            //文字置中
                            cs.VerticalAlignment = NPOI.SS.UserModel.VerticalAlignment.Center;
                            cs.Alignment = NPOI.SS.UserModel.HorizontalAlignment.Center;
                        }
                        sheet.GetRow(createRow).GetCell(createCell).CellStyle = cs;
                        createCell++;
                    }
                    createRow++;
                }
                catch (Exception)
                {

                }
            }
            try
            {
                int pathIndex = filePath.LastIndexOf('\\');
                int fileIndex = filePath.LastIndexOf('.');
                if (sheetName.Equals("工程數量計算表"))
                {
                    string now = DateTime.Now.ToString("yyyyMMddhhmm");
                    string fileName = filePath.Substring(pathIndex + 1, fileIndex - pathIndex - 1);
                    filePath = filePath.Substring(0, pathIndex) + "\\" + fileName + "_" + now + ".xlsx";
                    FileStream file = new FileStream(filePath, FileMode.Create); //產生檔案
                    workbook.Write(file);
                    file.Close();
                }
                else if (sheetName.Equals("工程數量詳細表"))
                {
                    FileStream file = new FileStream(filePath, FileMode.Create); //產生檔案
                    workbook.Write(file);
                    file.Close();
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Revit", ex.ToString() + "\n\n" + ex.Message);
            }

            return filePath;
        }
        // 清除Sheet內的所有Row
        public static void RemoveRow(ISheet sheet, int startRow)
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
