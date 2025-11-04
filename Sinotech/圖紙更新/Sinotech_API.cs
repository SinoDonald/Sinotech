using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Parameter = Autodesk.Revit.DB.Parameter;

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
        public static string addinAssmeblyPath = Assembly.GetExecutingAssembly().Location; // dll路徑

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
                    EPPlus epPlus = new EPPlus();
                    Tuple<List<string>, List<ExcelCellData>> readExcel = epPlus.ReadExcel(excelPath);
                    sheetNames = readExcel.Item1; // Excel中全部的Sheet

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
                        foreach(string checkSheet in checkSheets)
                        {
                            List<ExcelCellData> ecDataList = readExcel.Item2.Where(x => x.sheetName.Equals(checkSheet)).ToList(); // 將Excel中Sheet的Cell資料都撈出來
                            CreateFrames(doc, familySymbol, ecDataList); // 創建圖框, 並將參數寫入
                        }
                        
                        DateTime timeEnd = DateTime.Now; // 計時結束 取得目前時間
                        TimeSpan totalTime = timeEnd - timeStart;
                        TaskDialog.Show("Revit", "耗時：" + totalTime.Minutes + " 分 " + totalTime.Seconds + " 秒 " + "\n\n完成。");
                    }
                }
                catch (ArgumentException ex)
                {
                    TaskDialog.Show("Revit", "請選擇Excel檔" + ex.Message + "\n" + ex.ToString());
                    return Result.Failed;
                }
                catch (FileNotFoundException ex)
                {
                    TaskDialog.Show("Revit", "找不到Excel檔" + ex.Message + "\n" + ex.ToString());
                    return Result.Failed;
                }
                catch (DirectoryNotFoundException ex)
                {
                    TaskDialog.Show("Revit", "找不到資料夾" + ex.Message + "\n" + ex.ToString());
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
                            ecData.isheader = true; // 此列為標頭
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
        // 外部讀取dll
        protected void Application_Start()
        {
            AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(MyAssemblyResolver);
        }
        static Assembly MyAssemblyResolver(object sender, ResolveEventArgs args)
        {
            AppDomain domain = (AppDomain)sender;
            Assembly assembly = null;
            List<string> dllNames = new List<string>() { "ICSharpCode.SharpZipLib", "NPOI.Core", "NPOI.OOXML", "NPOI.OpenXml4Net", "NPOI.OpenXmlFormats" };
            string folderName = @"C:\ProgramData\Autodesk\Revit\Addins\2020\Sinotech\";
            foreach(string dllName in dllNames)
            {
                byte[] rawAssembly = File.ReadAllBytes(Path.Combine(folderName, dllName + ".dll"));
                byte[] rawSymbolStore = File.ReadAllBytes(Path.Combine(folderName, dllName + ".pdb"));
                assembly = domain.Load(rawAssembly, rawSymbolStore);
            }
            return assembly;
        }
        public static void LoadExtraDll(string dllName)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (object sender, ResolveEventArgs args) =>
            {
                if (args.Name.Contains(dllName))
                {
                    Assembly assembly = Assembly.GetExecutingAssembly();
                    string dirPath = assembly.Location;
                    string filename = Path.GetDirectoryName(dirPath);
                    filename = Path.Combine(Path.GetDirectoryName(addinAssmeblyPath), $"{dllName}.dll");
                    if (File.Exists(filename))
                    {
                        try
                        {
                            Assembly loadAssembly = Assembly.LoadFrom(filename);
                            //TaskDialog.Show("Revit", loadAssembly.Location + "\nSuccess.");
                        }
                        catch (Exception ex) { TaskDialog.Show("Error", ex.Message + "\n" + ex.ToString()); }
                    }
                }
                return null;
            };
        }
    }
}