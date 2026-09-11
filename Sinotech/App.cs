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
            sinotechBtn.ToolTip = "讀取Excel內的各欄位資料, 自動依選擇的圖紙建立至模型, 並將相關資訊寫入圖紙中。";
            sinotechBtn.ToolTipImage = convertFromBitmap(Properties.Resources.圖紙更新_原圖, 250);
            PushButton detectionScaleBtn = ribbonPanel.AddItem(new PushButtonData("DetectionScale", "更新比例尺", addinAssmeblyPath, "Sinotech.UpdateView.DetectionScale")) as PushButton;
            detectionScaleBtn.LargeImage = convertFromBitmap(Properties.Resources.更新比例尺);
            detectionScaleBtn.ToolTip = "更新圖紙內比例尺參數。";
            detectionScaleBtn.ToolTipImage = convertFromBitmap(Properties.Resources.更新比例尺_原圖, 250);
            PushButton updateExcelBtn = ribbonPanel.AddItem(new PushButtonData("UpdateExcel", "更新Excel", addinAssmeblyPath, "Sinotech.UpdateView.UpdateExcelCell")) as PushButton;
            updateExcelBtn.LargeImage = convertFromBitmap(Properties.Resources.更新Excel);
            updateExcelBtn.ToolTip = "讀取專案中圖紙的參數資料, 寫入到選擇的Excel檔中。";
            updateExcelBtn.ToolTipImage = convertFromBitmap(Properties.Resources.更新Excel_原圖, 250);
            PushButton batchSignBtn = ribbonPanel.AddItem(new PushButtonData("BatchSign", "自動簽圖", addinAssmeblyPath, "Sinotech.UpdateView.BatchSign")) as PushButton;
            batchSignBtn.LargeImage = convertFromBitmap(Properties.Resources.自動簽圖);
            batchSignBtn.ToolTip = "讀取Excel內的各階段設計者名字, 自動寫入到圖框的參數欄位。";
            batchSignBtn.ToolTipImage = convertFromBitmap(Properties.Resources.自動簽圖_原圖, 250);
            PushButton editViewSheetNumberBtn = ribbonPanel.AddItem(new PushButtonData("EditViewSheetNumber", "更新圖號", addinAssmeblyPath, "Sinotech.UpdateView.EditViewSheetNumber")) as PushButton;
            editViewSheetNumberBtn.LargeImage = convertFromBitmap(Properties.Resources.更新圖號);
            editViewSheetNumberBtn.ToolTip = "顯示現在圖紙的圖號, 讓使用者可修正後同步更新圖號參數。";
            editViewSheetNumberBtn.ToolTipImage = convertFromBitmap(Properties.Resources.更新圖號_原圖, 250);

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
            viewCopyBtn.ToolTip = "使用者選擇多張視圖, 讓視圖可一次的複製出來, 不用一張張建立。";
            viewCopyBtn.ToolTipImage = convertFromBitmap(Properties.Resources.視圖複製_原圖, 250);
            PushButton moveViewBtn = ribbonPanel.AddItem(new PushButtonData("MoveView", "視圖搬移", addinAssmeblyPath, "Sinotech.Plotting.MoveView")) as PushButton;
            moveViewBtn.LargeImage = convertFromBitmap(Properties.Resources.視圖搬移);
            moveViewBtn.ToolTip = "使用者選擇多張視圖, 可一次移動多張視圖。";
            moveViewBtn.ToolTipImage = convertFromBitmap(Properties.Resources.視圖搬移_原圖, 250);
            PushButton exportViewBtn = ribbonPanel.AddItem(new PushButtonData("ExportView", "圖紙匯出", addinAssmeblyPath, "Sinotech.Plotting.ExportDWG")) as PushButton;
            exportViewBtn.LargeImage = convertFromBitmap(Properties.Resources.圖紙匯出);
            exportViewBtn.ToolTip = "使用者選擇要匯出的多張圖紙, 可一次輸出, 並且統一由圖紙中的參數設定為輸出檔名。";
            exportViewBtn.ToolTipImage = convertFromBitmap(Properties.Resources.圖紙匯出_原圖, 250);

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
            autoJoinBtn.ToolTip = "使用者選擇要接合的品類(柱樑板牆), 會將元件的重疊處接合, 避免重複計算。";
            autoJoinBtn.ToolTipImage = convertFromBitmap(Properties.Resources.自動接合_原圖, 250);
            PushButton parkOrRoomBtn = ribbonPanel.AddItem(new PushButtonData("ParkOrRoom", "雲形線編號", addinAssmeblyPath, "Sinotech.SpeedTool.ParkOrRoom")) as PushButton;
            parkOrRoomBtn.LargeImage = convertFromBitmap(Properties.Resources.雲形線編號);
            parkOrRoomBtn.ToolTip = "依照雲形線的路線, 幫房間與停車格進行連續編號。";
            parkOrRoomBtn.ToolTipImage = convertFromBitmap(Properties.Resources.雲形線編號_原圖, 250);
            PushButton autoUpdateBtn = ribbonPanel.AddItem(new PushButtonData("AutoUpdate", "自動升版", addinAssmeblyPath, "Sinotech.SpeedTool.AutoUpdate")) as PushButton;
            autoUpdateBtn.LargeImage = convertFromBitmap(Properties.Resources.自動升版);
            autoUpdateBtn.ToolTip = "選擇資料夾, 會將裡面所有的.rvt檔使用當前的Revit版本開啟, 並自動另存新檔。";
            autoUpdateBtn.ToolTipImage = convertFromBitmap(Properties.Resources.自動升版_原圖, 250);

            // 添加「CSD」面板
            try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "CSD"); }
            catch
            {
                List<RibbonPanel> panel_list = new List<RibbonPanel>();
                panel_list = application.GetRibbonPanels("中興自動化");
                foreach (RibbonPanel rp in panel_list) { if (rp.Name == "CSD") { ribbonPanel = rp; } }
            }
            PushButton autoPipeTagBtn = ribbonPanel.AddItem(new PushButtonData("AutoPipeTag", "管道標籤", addinAssmeblyPath, "Sinotech.CSDSEM.AutoPipeTag")) as PushButton;
            autoPipeTagBtn.LargeImage = convertFromBitmap(Properties.Resources.管道標籤);
            autoPipeTagBtn.ToolTip = "在視圖中會讀取管道, 並且在管道中放置標籤, 顯示管道相關資訊。";
            autoPipeTagBtn.ToolTipImage = convertFromBitmap(Properties.Resources.管道標籤_原圖, 250);
            PushButton tagArrayBtn = ribbonPanel.AddItem(new PushButtonData("TagArray", "標籤排序", addinAssmeblyPath, "Sinotech.CSDSEM.TagArray")) as PushButton;
            tagArrayBtn.LargeImage = convertFromBitmap(Properties.Resources.標籤排序);
            tagArrayBtn.ToolTip = "使用者框選視圖中的空白區域, 會將視圖中鄰近的標籤移至空白處, 並依序排列。";
            tagArrayBtn.ToolTipImage = convertFromBitmap(Properties.Resources.標籤排序_原圖, 250);

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
            autoPipeOpenBtn.ToolTip = "在模型中讀取所有管道、風管、電纜架等機電設施, 當這些構件與樑、板、牆交接時, 在這些交接的位置進行開口與套管的動作。";
            autoPipeOpenBtn.ToolTipImage = convertFromBitmap(Properties.Resources.自動開口_原圖, 250);
            PushButton autoNumberBtn = ribbonPanel.AddItem(new PushButtonData("AutoNumber", "自動編號", addinAssmeblyPath, "Sinotech.CSDSEM.AutoNumber")) as PushButton;
            autoNumberBtn.LargeImage = convertFromBitmap(Properties.Resources.自動編號);
            autoNumberBtn.ToolTip = "在模型中依每一張視圖的所有開口, 依照網格順序由左而右、由上而下進行數字編號。";
            autoNumberBtn.ToolTipImage = convertFromBitmap(Properties.Resources.自動編號_原圖, 250);
            //PushButton manualPipeTagBtn = ribbonPanel.AddItem(new PushButtonData("ManualPipeTag", "手動編號", addinAssmeblyPath, "Sinotech.CSDSEM.ManualPipeTag")) as PushButton;
            //manualPipeTagBtn.LargeImage = convertFromBitmap(Properties.Resources.手動編號);
            PushButton autoOpeningTagBtn = ribbonPanel.AddItem(new PushButtonData("AutoOpeningTag", "開口標籤", addinAssmeblyPath, "Sinotech.CSDSEM.AutoOpeningTag")) as PushButton;
            autoOpeningTagBtn.LargeImage = convertFromBitmap(Properties.Resources.開口標籤);
            autoOpeningTagBtn.ToolTip = "在開口的中心點放置標籤, 顯示開口的編號為多少。";
            autoOpeningTagBtn.ToolTipImage = convertFromBitmap(Properties.Resources.開口標籤_原圖, 250);
            PushButton openingTagArrayBtn = ribbonPanel.AddItem(new PushButtonData("OpeningTagArrayBtn", "標籤排序", addinAssmeblyPath, "Sinotech.CSDSEM.OpeningTagArray")) as PushButton;
            openingTagArrayBtn.LargeImage = convertFromBitmap(Properties.Resources.標籤排序);
            openingTagArrayBtn.ToolTip = "將開口標籤移至空白處, 盡量避免標籤重疊。";
            openingTagArrayBtn.ToolTipImage = convertFromBitmap(Properties.Resources.標籤排序_原圖, 250);
            PushButton PCCESBtn = ribbonPanel.AddItem(new PushButtonData("OutPutPCCES", "PCCES", addinAssmeblyPath, "Sinotech.CSDSEM.OutPutPCCES")) as PushButton;
            PCCESBtn.LargeImage = convertFromBitmap(Properties.Resources.PCCES);
            PCCESBtn.ToolTip = "計算專案中的開口、套管尺寸與數量, 輸出至安裝檔中的\"工程數量詳細表\"範本。";
            PCCESBtn.ToolTipImage = convertFromBitmap(Properties.Resources.PCCES_原圖, 250);

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
            familyInstanceLockBtn.ToolTip = "將專案中的族群鎖住, 禁止他人修改。";
            familyInstanceLockBtn.ToolTipImage = convertFromBitmap(Properties.Resources.元件鎖定_原圖, 250);
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

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        BitmapSource convertFromBitmap(System.Drawing.Bitmap bitmap, int targetWidth = 0)
        {
            if (bitmap == null) return null;

            IntPtr hBitmap;
            if (targetWidth > 0 && bitmap.Width != targetWidth)
            {
                int targetHeight = (int)((double)bitmap.Height / bitmap.Width * targetWidth);
                using (var resized = new System.Drawing.Bitmap(targetWidth, targetHeight))
                {
                    using (var g = System.Drawing.Graphics.FromImage(resized))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.DrawImage(bitmap, 0, 0, targetWidth, targetHeight);
                    }
                    hBitmap = resized.GetHbitmap();
                    try
                    {
                        return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                    }
                }
            }

            hBitmap = bitmap.GetHbitmap();
            try
            {
                return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
