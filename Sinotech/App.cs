using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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
    public class App : IExternalApplication
    {
        public string addinAssmeblyPath = Assembly.GetExecutingAssembly().Location; // 封包版路徑位址
        public Result OnStartup(UIControlledApplication application)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // 支援中文編碼, 解決 Revit 2025 (.NET 8) 不支援 IBM437 編碼的問題

            // 創建一個新的選單
            RibbonPanel ribbonPanel = null;
            try { application.CreateRibbonTab("中興自動化"); } catch { }
            try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "圖紙更新"); }
            catch
            {
                List<RibbonPanel> panel_list = new List<RibbonPanel>();
                panel_list = application.GetRibbonPanels("中興自動化");
                foreach (RibbonPanel rp in panel_list) { if (rp.Name == "圖紙更新") { ribbonPanel = rp; } }
            }
            // 添加「圖紙更新」面板
            PushButton sinotechBtn = ribbonPanel.AddItem(new PushButtonData("Sinotech_API", "圖紙更新", addinAssmeblyPath, "Sinotech.UpdateView.Sinotech_API")) as PushButton;
            //sinotechBtn.LargeImage = new BitmapImage(new Uri(picPath + "圖紙更新.png")); <-- 舊版寫法
            sinotechBtn.LargeImage = convertFromBitmap(Properties.Resources.圖紙更新);
            PushButton detectionScaleBtn = ribbonPanel.AddItem(new PushButtonData("DetectionScale", "更新比例尺", addinAssmeblyPath, "Sinotech.UpdateView.DetectionScale")) as PushButton;
            detectionScaleBtn.LargeImage = convertFromBitmap(Properties.Resources.更新比例尺);
            PushButton updateExcelBtn = ribbonPanel.AddItem(new PushButtonData("UpdateExcel", "更新Excel", addinAssmeblyPath, "Sinotech.UpdateView.UpdateExcelCell")) as PushButton;
            updateExcelBtn.LargeImage = convertFromBitmap(Properties.Resources.更新Excel);
            PushButton batchSignBtn = ribbonPanel.AddItem(new PushButtonData("BatchSign", "自動簽圖", addinAssmeblyPath, "Sinotech.UpdateView.BatchSign")) as PushButton;
            batchSignBtn.LargeImage = convertFromBitmap(Properties.Resources.自動簽圖);
            PushButton editViewSheetNumberBtn = ribbonPanel.AddItem(new PushButtonData("EditViewSheetNumber", "更新圖號", addinAssmeblyPath, "Sinotech.UpdateView.EditViewSheetNumber")) as PushButton;
            editViewSheetNumberBtn.LargeImage = convertFromBitmap(Properties.Resources.更新圖號);

            // 添加「自動出圖」面板
            try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "自動出圖"); }
            catch
            {
                List<RibbonPanel> panel_list = new List<RibbonPanel>();
                panel_list = application.GetRibbonPanels("中興自動化");
                foreach (RibbonPanel rp in panel_list) { if (rp.Name == "自動出圖") { ribbonPanel = rp; } }
            }
            PushButton viewCopyBtn = ribbonPanel.AddItem(new PushButtonData("ViewCopy", "視圖複製", addinAssmeblyPath, "Sinotech.Plotting.CopyDrawings")) as PushButton;
            viewCopyBtn.LargeImage = convertFromBitmap(Properties.Resources.視圖複製);
            PushButton moveViewBtn = ribbonPanel.AddItem(new PushButtonData("MoveView", "視圖搬移", addinAssmeblyPath, "Sinotech.Plotting.MoveView")) as PushButton;
            moveViewBtn.LargeImage = convertFromBitmap(Properties.Resources.視圖搬移);
            PushButton exportViewBtn = ribbonPanel.AddItem(new PushButtonData("ExportView", "圖紙匯出", addinAssmeblyPath, "Sinotech.Plotting.ExportDWG")) as PushButton;
            exportViewBtn.LargeImage = convertFromBitmap(Properties.Resources.圖紙匯出);

            // 添加「快速工具」面板
            try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "快速工具"); }
            catch
            {
                List<RibbonPanel> panel_list = new List<RibbonPanel>();
                panel_list = application.GetRibbonPanels("中興自動化");
                foreach (RibbonPanel rp in panel_list) { if (rp.Name == "快速工具") { ribbonPanel = rp; } }
            }
            PushButton autoJoinBtn = ribbonPanel.AddItem(new PushButtonData("AutoJoin", "自動接合", addinAssmeblyPath, "Sinotech.SpeedTool.AutoJoin")) as PushButton;
            autoJoinBtn.LargeImage = convertFromBitmap(Properties.Resources.自動接合);
            PushButton parkOrRoomBtn = ribbonPanel.AddItem(new PushButtonData("ParkOrRoom", "雲形線編號", addinAssmeblyPath, "Sinotech.SpeedTool.ParkOrRoom")) as PushButton;
            parkOrRoomBtn.LargeImage = convertFromBitmap(Properties.Resources.雲形線編號);
            PushButton autoUpdateBtn = ribbonPanel.AddItem(new PushButtonData("AutoUpdate", "自動升版", addinAssmeblyPath, "Sinotech.SpeedTool.AutoUpdate")) as PushButton;
            autoUpdateBtn.LargeImage = convertFromBitmap(Properties.Resources.自動升版);

            // 添加「CSD」面板
            try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "CSD"); }
            catch
            {
                List<RibbonPanel> panel_list = new List<RibbonPanel>();
                panel_list = application.GetRibbonPanels("中興自動化");
                foreach (RibbonPanel rp in panel_list) { if (rp.Name == "CSD") { ribbonPanel = rp; } }
            }
            PushButton autoPipeTagBtn = ribbonPanel.AddItem(new PushButtonData("AutoPipeTag", "自動標籤", addinAssmeblyPath, "Sinotech.CSDSEM.AutoPipeTag")) as PushButton;
            autoPipeTagBtn.LargeImage = convertFromBitmap(Properties.Resources.自動標籤);
            PushButton tagArrayBtn = ribbonPanel.AddItem(new PushButtonData("TagArray", "標籤排序", addinAssmeblyPath, "Sinotech.CSDSEM.TagArray")) as PushButton;
            tagArrayBtn.LargeImage = convertFromBitmap(Properties.Resources.標籤排序);

            // 添加「SEM」面板
            try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "SEM"); }
            catch
            {
                List<RibbonPanel> panel_list = new List<RibbonPanel>();
                panel_list = application.GetRibbonPanels("中興自動化");
                foreach (RibbonPanel rp in panel_list) { if (rp.Name == "SEM") { ribbonPanel = rp; } }
            }
            PushButton autoPipeOpenBtn = ribbonPanel.AddItem(new PushButtonData("AutoPipeOpen", "自動開口", addinAssmeblyPath, "Sinotech.CSDSEM.LinkOpening")) as PushButton;
            autoPipeOpenBtn.LargeImage = convertFromBitmap(Properties.Resources.自動開口);
            PushButton autoNumberBtn = ribbonPanel.AddItem(new PushButtonData("AutoNumber", "自動編號", addinAssmeblyPath, "Sinotech.CSDSEM.AutoNumber")) as PushButton;
            autoNumberBtn.LargeImage = convertFromBitmap(Properties.Resources.自動編號);
            //PushButton manualPipeTagBtn = ribbonPanel.AddItem(new PushButtonData("ManualPipeTag", "手動編號", addinAssmeblyPath, "Sinotech.CSDSEM.ManualPipeTag")) as PushButton;
            //manualPipeTagBtn.LargeImage = convertFromBitmap(Properties.Resources.手動編號);
            PushButton autoOpeningTagBtn = ribbonPanel.AddItem(new PushButtonData("AutoOpeningTag", "標籤排序", addinAssmeblyPath, "Sinotech.CSDSEM.AutoOpeningTag")) as PushButton;
            autoOpeningTagBtn.LargeImage = convertFromBitmap(Properties.Resources.手動編號);
            PushButton PCCESBtn = ribbonPanel.AddItem(new PushButtonData("OutPutPCCES", "PCCES", addinAssmeblyPath, "Sinotech.CSDSEM.OutPutPCCES")) as PushButton;
            PCCESBtn.LargeImage = convertFromBitmap(Properties.Resources.PCCES);

            // 添加「元件保護」面板
            try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "元件保護"); }
            catch
            {
                List<RibbonPanel> panel_list = new List<RibbonPanel>();
                panel_list = application.GetRibbonPanels("中興自動化");
                foreach (RibbonPanel rp in panel_list) { if (rp.Name == "元件保護") { ribbonPanel = rp; } }
            }
            PushButton familyInstanceLockBtn = ribbonPanel.AddItem(new PushButtonData("FamilyInstanceLock", "元件鎖定", addinAssmeblyPath, "Sinotech.FamilyProtect.FamilyProtect")) as PushButton;
            familyInstanceLockBtn.LargeImage = convertFromBitmap(Properties.Resources.元件鎖定);
            //PushButton lockOneBtn = ribbonPanel.AddItem(new PushButtonData("LockOne", "單一元件鎖定", addinAssmeblyPath, "Sinotech.FamilyProtect.LockOne")) as PushButton;
            //lockOneBtn.LargeImage = convertFromBitmap(Properties.Resources.元件鎖定);

            //// 添加「快速翻模」面板
            //try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "快速翻模"); }
            //catch
            //{
            //    List<RibbonPanel> panel_list = new List<RibbonPanel>();
            //    panel_list = application.GetRibbonPanels("中興自動化");
            //    foreach (RibbonPanel rp in panel_list) { if (rp.Name == "快速翻模") { ribbonPanel = rp; } }
            //}
            //PushButton autoColumnsBtn = ribbonPanel.AddItem(new PushButtonData("AutoColumns", "自動翻柱", addinAssmeblyPath, "Sinotech.CreateModel.AutoColumn")) as PushButton;
            //autoColumnsBtn.LargeImage = convertFromBitmap(Properties.Resources.自動翻柱);
            //PushButton autoPipeBtn = ribbonPanel.AddItem(new PushButtonData("AutoPipe", "自動建管", addinAssmeblyPath, "Sinotech.CreateModel.AutoPipe")) as PushButton;
            //autoPipeBtn.LargeImage = convertFromBitmap(Properties.Resources.自動標籤);

            //// 添加「規範校核」面板
            //try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "規範校核"); }
            //catch
            //{
            //    List<RibbonPanel> panel_list = new List<RibbonPanel>();
            //    panel_list = application.GetRibbonPanels("中興自動化");
            //    foreach (RibbonPanel rp in panel_list) { if (rp.Name == "規範校核") { ribbonPanel = rp; } }
            //}
            //PushButton crushReportBtn = ribbonPanel.AddItem(new PushButtonData("CrushReport", "干涉報告", addinAssmeblyPath, "Sinotech.Verification.CrushReport")) as PushButton;
            //crushReportBtn.LargeImage = convertFromBitmap(Properties.Resources.干涉報告);
            //PushButton copyFilesBtn = ribbonPanel.AddItem(new PushButtonData("CopyFiles", "規範詳圖", addinAssmeblyPath, "Sinotech.Verification.CopyFiles")) as PushButton;
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
