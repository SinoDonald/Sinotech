using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DocumentProcessing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sinotech
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    [Journaling(JournalingMode.NoCommandData)]
    public class BatchSign : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            ExcelReader excelReader = new ExcelReader();
            PdfReader pdfReader = new PdfReader();
            List<DrawingSign> drawingDatas = excelReader.GetDrawingSigns();
            // 比對檔名, 將PDF的日期資料寫入至data
            foreach (DrawingSign drawingData in drawingDatas)
            {
                string pdf_path = Path.Combine(Path.GetDirectoryName(excelReader.FilePath), $@"pdf\{drawingData.FileName}.pdf");
                drawingData.SignDates = pdfReader.ExtractDateText(pdf_path);
            }

            SignMyName(doc, drawingDatas); // 將Excel資料寫入圖框

            TaskDialog.Show("Revit", "更新完成");

            return Result.Succeeded;
        }
        /// <summary>
        /// 將Excel資料寫入圖框
        /// </summary>
        /// <param name="doc"></param>
        /// <param name="data"></param>
        private void SignMyName(Document doc, List<DrawingSign> drawingDatas)
        {
            using (Transaction trans = new Transaction(doc, "自動簽圖"))
            {
                trans.Start();
                List<ViewSheet> viewSheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).WhereElementIsNotElementType().Cast<ViewSheet>().ToList(); // 讀取專案中所有的ViewSheet

                foreach (DrawingSign drawingData in drawingDatas)
                {
                    ViewSheet viewSheet = viewSheets.Where(x => !String.IsNullOrEmpty(x.LookupParameter("圖框-電腦圖號").AsString()))
                                                    .Where(x => x.LookupParameter("圖框-電腦圖號").AsString().Equals(drawingData.FileName)).FirstOrDefault();
                    if (viewSheet != null)
                    {
                        try
                        {
                            // 人員
                            viewSheet.get_Parameter(BuiltInParameter.SHEET_DESIGNED_BY).Set(drawingData.SignNames[0]); // 設計(Designed)
                            viewSheet.get_Parameter(BuiltInParameter.SHEET_CHECKED_BY).Set(drawingData.SignNames[1]); // 初核(Checked)
                            viewSheet.LookupParameter("Rechecked By").Set(drawingData.SignNames[2]); // 複核(Rechecked)
                            viewSheet.LookupParameter("P.E./Architect").Set(drawingData.SignNames[3]); // 技師(Prof. Eng.)
                            viewSheet.LookupParameter("Project Manager").Set(drawingData.SignNames[4]); // 計畫經理(PM)
                            // 日期
                            viewSheet.LookupParameter("Designed Date").Set(drawingData.SignDates[0]); // 設計日期
                            viewSheet.LookupParameter("Checked Date").Set(drawingData.SignDates[1]); // 初核日期
                            viewSheet.LookupParameter("Rechecked Date").Set(drawingData.SignDates[2]); // 複核日期
                            viewSheet.LookupParameter("P.E./Architect Signed Date").Set(drawingData.SignDates[3]); // 技師簽核日期
                            viewSheet.LookupParameter("Project Manager Signed Date").Set(drawingData.SignDates[4]); // 計畫經理簽核日期
                            // 核備章
                            viewSheet.LookupParameter("圖框-核備章-年").Set(drawingData.SignDates[5]); // 核備章-年
                            viewSheet.LookupParameter("圖框-核備章-月").Set(drawingData.SignDates[6]); // 核備章-月
                            viewSheet.LookupParameter("圖框-核備章-日").Set(drawingData.SignDates[7]); // 核備章-日
                        }
                        catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                    }
                }
                trans.Commit();
            }
        }
    }
}