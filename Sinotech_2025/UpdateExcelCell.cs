using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace Sinotech_2025
{
    [Transaction(TransactionMode.Manual)]
    public class UpdateExcelCell : IExternalCommand
    {
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
                EPPlus epPlus = new EPPlus();
                List<ExcelCellData> excelRowList = new List<ExcelCellData>(); // 儲存所有圖紙的參數
                try
                {
                    Tuple<List<string>, List<ExcelCellData>> readExcel = epPlus.ReadExcel(excelPath);
                    List<string> sheetNames = readExcel.Item1; // Excel中全部的Sheet
                    List<ExcelCellData> ecDataList = readExcel.Item2; // 將Excel中Sheet的Cell資料都撈出來
                    List<SheetHeader> sheetHeaderList = HeaderAndSheetNumber(sheetNames, ecDataList); // 讀取Excel中, 所有的標頭與SheetNumber

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
                List<string> vsSheets = (from x in excelRowList
                                         select x.sheetName).Distinct().ToList();
                ////建立Excel 2007檔案, 創建Sheet
                //IWorkbook wb = new XSSFWorkbook();
                //foreach (var vsSheet in vsSheets)
                //{
                //    ISheet ws = wb.CreateSheet(vsSheet);
                //    int createHeader = 0;
                //    ws.CreateRow(createHeader); // 第一行為欄位名稱
                //    var vsHeadle = (from x in excelRowList
                //                    where x.sheetName.Equals(vsSheet)
                //                    select x.header).FirstOrDefault();
                //    foreach (string headleContent in vsHeadle)
                //    {
                //        ws.GetRow(0).CreateCell(createHeader).SetCellValue(headleContent);
                //        createHeader++;
                //    }

                //    int createCell = 0;
                //    int createParaValue = 1;
                //    ws.CreateRow(createParaValue); // 要查詢的參數
                //    var vsParaValue = (from x in excelRowList
                //                    where x.sheetName.Equals(vsSheet)
                //                    select x.paraValue).FirstOrDefault();
                //    foreach (string paraValue in vsParaValue)
                //    {
                //        ws.GetRow(1).CreateCell(createCell).SetCellValue(paraValue);
                //        createCell++;
                //    }

                //    var createExcelCell = (from x in excelRowList
                //                           where x.sheetName.Equals(vsSheet)
                //                           select x);
                //    int createRow = 2;
                //    foreach (ExcelCellData excelCellData in createExcelCell)
                //    {
                //        try
                //        {
                //            createCell = 0;
                //            ws.CreateRow(createRow); 
                //            foreach (var cellContent in excelCellData.cellValues)
                //            {
                //                ws.GetRow(createRow).CreateCell(createCell).SetCellValue(cellContent);
                //                createCell++;
                //            }
                //            createRow++;
                //        }
                //        catch(Exception)
                //        {

                //        }
                //    }
                //}
                try
                {
                    int pathIndex = excelPath.LastIndexOf('\\');
                    int fileIndex = excelPath.LastIndexOf('.');
                    string now = DateTime.Now.ToString("yyyyMMddhhmm");
                    string fileName = excelPath.Substring(pathIndex + 1, fileIndex - pathIndex - 1);
                    excelPath = excelPath.Substring(0, pathIndex) + "\\" + fileName + "_" + now + ".xlsx";
                    //FileStream file = new FileStream(excelPath, FileMode.Create); //產生檔案
                    ////wb.Write(file);
                    //file.Close();
                    epPlus.ExportExcel(excelPath, vsSheets, excelRowList);
                    TaskDialog.Show("Revit", "完成");
                }
                catch(Exception ex)
                {
                    TaskDialog.Show("Revit", ex.ToString() + "\n\n" + ex.Message);
                }
            }

            return Result.Succeeded;
        }
        // 讀取Excel中, 所有的標頭與SheetNumber
        private List<SheetHeader> HeaderAndSheetNumber(List<string> sheetNames, List<ExcelCellData> ecDataList)
        {
            List<SheetHeader> sheetHeaderList = new List<SheetHeader>();

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

            return sheetHeaderList;
        }
    }
}
