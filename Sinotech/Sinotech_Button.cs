using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Sinotech
{    
    // 預設Excel檔案路徑
    public class LicPath
    {
        public string previous = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        public string pathStr = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\tmp.txt"; // 我的文件
    }
    public class Sinotech_Button : IExternalApplication
    {
        public string addinAssmeblyPath = Assembly.GetExecutingAssembly().Location; // 封包版路徑位址
        public Result OnStartup(UIControlledApplication application)
        {
            // 2020
            string autoBuildAsb = Path.Combine(Directory.GetParent(addinAssmeblyPath).FullName, "AutoBuild_Old.dll"); // 快速翻模
            string autoExportAsb = Path.Combine(Directory.GetParent(addinAssmeblyPath).FullName, "AutoExport_Old.dll"); // 自動出圖
            string codeViewAsb = Path.Combine(Directory.GetParent(addinAssmeblyPath).FullName, "CodeView_Old.dll"); // 規範校核
            string CSDSEMAsb = Path.Combine(Directory.GetParent(addinAssmeblyPath).FullName, "CSDSEM_Old.dll"); // CSD/SEM
            string speedToolsAsb = Path.Combine(Directory.GetParent(addinAssmeblyPath).FullName, "SpeedTools.dll"); // 快速工具

            //// 2024
            //string autoBuildAsb = Path.Combine(Directory.GetParent(addinAssmeblyPath).FullName, "AutoBuild.dll"); // 快速翻模
            //string autoExportAsb = Path.Combine(Directory.GetParent(addinAssmeblyPath).FullName, "AutoExport.dll"); // 自動出圖
            //string codeViewAsb = Path.Combine(Directory.GetParent(addinAssmeblyPath).FullName, "CodeView.dll"); // 規範校核
            //string CSDSEMAsb = Path.Combine(Directory.GetParent(addinAssmeblyPath).FullName, "CSDSEM.dll"); // CSD/SEM
            //string speedToolsAsb = Path.Combine(Directory.GetParent(addinAssmeblyPath).FullName, "SpeedTools.dll"); // 快速工具

            // 創建一個新的選單
            String tabName = "中興自動化";
            RibbonPanel ribbonPanel = null;
            try { application.CreateRibbonTab("中興自動化"); } catch { }
            try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "圖紙更新"); }
            catch
            {
                List<RibbonPanel> panel_list = new List<RibbonPanel>();
                panel_list = application.GetRibbonPanels("中興自動化");
                foreach (RibbonPanel rp in panel_list)
                {
                    if (rp.Name == "圖紙更新")
                    {
                        ribbonPanel = rp;
                    }
                }
            }

            // 添加「圖紙更新」面板
            PushButton sinotechBtn = ribbonPanel.AddItem(new PushButtonData("Sinotech_API", "圖紙更新", addinAssmeblyPath, "Sinotech.Sinotech_API")) as PushButton;
            //sinotechBtn.LargeImage = new BitmapImage(new Uri(picPath + "圖紙更新.png")); <-- 舊版寫法
            sinotechBtn.LargeImage = convertFromBitmap(Properties.Resources.圖紙更新);
            PushButton detectionScaleBtn = ribbonPanel.AddItem(new PushButtonData("DetectionScale", "更新比例尺", addinAssmeblyPath, "Sinotech.DetectionScale")) as PushButton;
            detectionScaleBtn.LargeImage = convertFromBitmap(Properties.Resources.更新比例尺);
            PushButton updateExcelBtn = ribbonPanel.AddItem(new PushButtonData("UpdateExcel", "更新Excel", addinAssmeblyPath, "Sinotech.UpdateExcelCell")) as PushButton;
            updateExcelBtn.LargeImage = convertFromBitmap(Properties.Resources.更新Excel);
            PushButton editViewSheetNumberBtn = ribbonPanel.AddItem(new PushButtonData("EditViewSheetNumber", "更新圖號", addinAssmeblyPath, "Sinotech.EditViewSheetNumber")) as PushButton;
            editViewSheetNumberBtn.LargeImage = convertFromBitmap(Properties.Resources.更新圖號);

            // 添加「自動出圖」面板
            try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "自動出圖"); }
            catch
            {
                List<RibbonPanel> panel_list = new List<RibbonPanel>();
                panel_list = application.GetRibbonPanels("中興自動化");
                foreach (RibbonPanel rp in panel_list) { if (rp.Name == "自動出圖") { ribbonPanel = rp; } }
            }
            PushButton viewCopyBtn = ribbonPanel.AddItem(new PushButtonData("ViewCopy", "視圖複製", autoExportAsb, "AutoExport.CopyDrawings")) as PushButton;
            viewCopyBtn.LargeImage = convertFromBitmap(Properties.Resources.視圖複製);
            PushButton moveViewBtn = ribbonPanel.AddItem(new PushButtonData("MoveView", "視圖搬移", autoExportAsb, "AutoExport.MoveView")) as PushButton;
            moveViewBtn.LargeImage = convertFromBitmap(Properties.Resources.視圖搬移);
            PushButton exportViewBtn = ribbonPanel.AddItem(new PushButtonData("ExportView", "圖紙匯出", autoExportAsb, "AutoExport.ExportDWG")) as PushButton;
            exportViewBtn.LargeImage = convertFromBitmap(Properties.Resources.圖紙匯出);

            // 添加「快速工具」面板
            try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "快速工具"); }
            catch
            {
                List<RibbonPanel> panel_list = new List<RibbonPanel>();
                panel_list = application.GetRibbonPanels("中興自動化");
                foreach (RibbonPanel rp in panel_list) { if (rp.Name == "快速工具") { ribbonPanel = rp; } }
            }
            PushButton autoJoinBtn = ribbonPanel.AddItem(new PushButtonData("AutoJoin", "自動接合", speedToolsAsb, "SpeedTools.AutoJoin")) as PushButton;
            autoJoinBtn.LargeImage = convertFromBitmap(Properties.Resources.自動接合);
            PushButton parkOrRoomBtn = ribbonPanel.AddItem(new PushButtonData("ParkOrRoom", "雲形線編號", speedToolsAsb, "SpeedTools.ParkOrRoom")) as PushButton;
            parkOrRoomBtn.LargeImage = convertFromBitmap(Properties.Resources.雲形線編號);
            PushButton autoUpdateBtn = ribbonPanel.AddItem(new PushButtonData("AutoUpdate", "自動升版", speedToolsAsb, "SpeedTools.AutoUpdate")) as PushButton;
            autoUpdateBtn.LargeImage = convertFromBitmap(Properties.Resources.自動升版);

            // 添加「CSD/SEM」面板
            try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "CSD/SEM"); }
            catch
            {
                List<RibbonPanel> panel_list = new List<RibbonPanel>();
                panel_list = application.GetRibbonPanels("中興自動化");
                foreach (RibbonPanel rp in panel_list) { if (rp.Name == "CSD/SEM") { ribbonPanel = rp; } }
            }
            PushButton autoPipeOpenBtn = ribbonPanel.AddItem(new PushButtonData("AutoPipeOpen", "自動開口", CSDSEMAsb, "CSDSEM.LinkOpening")) as PushButton;
            autoPipeOpenBtn.LargeImage = convertFromBitmap(Properties.Resources.自動開口);
            PushButton autoPipeTagBtn = ribbonPanel.AddItem(new PushButtonData("AutoPipeTag", "自動標籤", CSDSEMAsb, "CSDSEM.AutoPipeTag")) as PushButton;
            autoPipeTagBtn.LargeImage = convertFromBitmap(Properties.Resources.自動標籤);
            PushButton manualPipeTagBtn = ribbonPanel.AddItem(new PushButtonData("ManualPipeTag", "手動標籤", CSDSEMAsb, "CSDSEM.ManualPipeTag")) as PushButton;
            manualPipeTagBtn.LargeImage = convertFromBitmap(Properties.Resources.手動標籤);
            PushButton PCCESBtn = ribbonPanel.AddItem(new PushButtonData("OutPutPCCES", "PCCES", CSDSEMAsb, "CSDSEM.OutPutPCCES")) as PushButton;
            PCCESBtn.LargeImage = convertFromBitmap(Properties.Resources.PCCES);

            //// 添加「快速翻模」面板
            //try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "快速翻模"); }
            //catch
            //{
            //    List<RibbonPanel> panel_list = new List<RibbonPanel>();
            //    panel_list = application.GetRibbonPanels("中興自動化");
            //    foreach (RibbonPanel rp in panel_list) { if (rp.Name == "快速翻模") { ribbonPanel = rp; } }
            //}
            //PushButton autoColumnsBtn = ribbonPanel.AddItem(new PushButtonData("AutoColumns", "自動翻柱", autoBuildAsb, "AutoBuild.AutoColumn")) as PushButton;
            //autoColumnsBtn.LargeImage = convertFromBitmap(Properties.Resources.自動翻柱);
            //PushButton autoPipeBtn = ribbonPanel.AddItem(new PushButtonData("AutoPipe", "自動建管", autoBuildAsb, "AutoBuild.AutoPipe")) as PushButton;
            //autoPipeBtn.LargeImage = convertFromBitmap(Properties.Resources.自動標籤);

            //// 添加「規範校核」面板
            //try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "規範校核"); }
            //catch
            //{
            //    List<RibbonPanel> panel_list = new List<RibbonPanel>();
            //    panel_list = application.GetRibbonPanels("中興自動化");
            //    foreach (RibbonPanel rp in panel_list) { if (rp.Name == "規範校核") { ribbonPanel = rp; } }
            //}
            //PushButton crushReportBtn = ribbonPanel.AddItem(new PushButtonData("CrushReport", "干涉報告", codeViewAsb, "CodeView.CrushReport")) as PushButton;
            //crushReportBtn.LargeImage = convertFromBitmap(Properties.Resources.干涉報告);
            //PushButton copyFilesBtn = ribbonPanel.AddItem(new PushButtonData("CopyFiles", "規範詳圖", codeViewAsb, "CodeView.CopyFiles")) as PushButton;
            //copyFilesBtn.LargeImage = convertFromBitmap(Properties.Resources.規範詳圖);

            return Result.Succeeded;
        }

        BitmapSource convertFromBitmap(System.Drawing.Bitmap bitmap)
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                bitmap.GetHbitmap(),
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
