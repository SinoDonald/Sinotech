using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Sinotech_2020
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
            PushButton sinotechBtn = ribbonPanel.AddItem(new PushButtonData("Sinotech_API", "圖紙更新", addinAssmeblyPath, "Sinotech_2020.UpdateView.Sinotech_API")) as PushButton;
            //sinotechBtn.LargeImage = new BitmapImage(new Uri(picPath + "圖紙更新.png")); <-- 舊版寫法
            sinotechBtn.LargeImage = convertFromBitmap(Properties.Resources.圖紙更新);
            sinotechBtn.ToolTip = "【功能介紹】\n依 Excel 圖紙清單建立或更新 Revit 圖紙，並將圖名、圖號等欄位寫入圖框參數。";
            sinotechBtn.LongDescription = "【使用步驟】\n1. 開啟專案，確認已載入要使用的圖框，並準備符合既有欄位格式的 Excel 圖紙清單。\n2. 點選「圖紙更新」，選擇 Excel 檔案。\n3. 選擇圖框類型及要處理的 Excel 工作表，按「確定」。\n4. 完成後核對圖紙數量、圖名、圖號及圖框資料，再儲存模型。";
            PushButton detectionScaleBtn = ribbonPanel.AddItem(new PushButtonData("DetectionScale", "更新比例尺", addinAssmeblyPath, "Sinotech_2020.UpdateView.DetectionScale")) as PushButton;
            detectionScaleBtn.LargeImage = convertFromBitmap(Properties.Resources.更新比例尺);
            detectionScaleBtn.ToolTip = "【功能介紹】\n讀取各圖紙上平面、立面及剖面視圖的比例，更新圖框的比例尺參數。";
            detectionScaleBtn.LongDescription = "【使用步驟】\n1. 確認圖紙已放置視圖，並已設定各視圖的比例。\n2. 確認圖框具有 Graphic Scale 參數，且已載入對應的 Metric Scale 一般註解類型。\n3. 點選「更新比例尺」，程式會處理專案中的圖紙。\n4. 核對圖框顯示的比例尺；若提示缺少類型，補齊後再執行。";
            PushButton updateExcelBtn = ribbonPanel.AddItem(new PushButtonData("UpdateExcel", "更新Excel", addinAssmeblyPath, "Sinotech_2020.UpdateView.UpdateExcelCell")) as PushButton;
            updateExcelBtn.LargeImage = convertFromBitmap(Properties.Resources.更新Excel);
            updateExcelBtn.ToolTip = "【功能介紹】\n依既有 Excel 圖紙清單的欄位及圖號對應，將 Revit 圖紙參數回寫至 Excel。";
            updateExcelBtn.LongDescription = "【使用步驟】\n1. 開啟含有圖紙的專案，確認圖號與圖框參數已更新。\n2. 準備符合既有欄位格式的 Excel 清單，先關閉該 Excel 檔案。\n3. 點選「更新Excel」，選擇要回寫的 Excel 檔案。\n4. 完成後開啟 Excel，核對圖號對應及回寫的欄位資料。";
            PushButton batchSignBtn = ribbonPanel.AddItem(new PushButtonData("BatchSign", "自動簽圖", addinAssmeblyPath, "Sinotech_2020.UpdateView.BatchSign")) as PushButton;
            batchSignBtn.LargeImage = convertFromBitmap(Properties.Resources.自動簽圖);
            batchSignBtn.ToolTip = "【功能介紹】\n依 Excel 簽核清單與對應 PDF 的日期資料，批次填入圖紙簽核人員、日期及核備章參數。";
            batchSignBtn.LongDescription = "【使用步驟】\n1. 準備符合既有格式的 Excel 簽核清單，並確認圖紙的「圖框-電腦圖號」可對應清單檔名。\n2. 將對應的 PDF 放在 Excel 同層的 pdf 子資料夾內，檔名須與清單一致。\n3. 點選「自動簽圖」，選擇 Excel 清單，等待完成。\n4. 核對各圖紙的簽核姓名、日期與核備章，再儲存模型。";
            PushButton editViewSheetNumberBtn = ribbonPanel.AddItem(new PushButtonData("EditViewSheetNumber", "更新圖號", addinAssmeblyPath, "Sinotech_2020.UpdateView.EditViewSheetNumber")) as PushButton;
            editViewSheetNumberBtn.LargeImage = convertFromBitmap(Properties.Resources.更新圖號);
            editViewSheetNumberBtn.ToolTip = "【功能介紹】\n列出專案圖紙的圖號，供使用者編修後同步更新相關圖號參數。";
            editViewSheetNumberBtn.LongDescription = "【使用步驟】\n1. 開啟含有圖紙的 Revit 專案，點選「更新圖號」。\n2. 在清單中核對並修改需要調整的圖號。\n3. 確認圖號沒有重複後，按「確定」套用。\n4. 核對專案瀏覽器與圖框顯示的圖號，再儲存模型。";

            // 添加「自動出圖」面板
            try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "自動出圖"); }
            catch
            {
                List<RibbonPanel> panel_list = new List<RibbonPanel>();
                panel_list = application.GetRibbonPanels("中興自動化");
                foreach (RibbonPanel rp in panel_list) { if (rp.Name == "自動出圖") { ribbonPanel = rp; } }
            }
            PushButton viewCopyBtn = ribbonPanel.AddItem(new PushButtonData("ViewCopy", "視圖複製", addinAssmeblyPath, "Sinotech_2020.Plotting.CopyDrawings")) as PushButton;
            viewCopyBtn.LargeImage = convertFromBitmap(Properties.Resources.視圖複製);
            viewCopyBtn.ToolTip = "【功能介紹】\n批次複製選取的視圖，可設定複製數量及一般複製、含細節或從屬視圖等方式。";
            viewCopyBtn.LongDescription = "【使用步驟】\n1. 開啟專案，點選「視圖複製」，先選擇圖框類型。\n2. 在視圖清單勾選要複製的視圖。\n3. 設定複製方式與數量，按「確定」。\n4. 核對新增視圖的名稱、數量及內容，必要時調整後儲存。";
            PushButton moveViewBtn = ribbonPanel.AddItem(new PushButtonData("MoveView", "視圖搬移", addinAssmeblyPath, "Sinotech_2020.Plotting.MoveView")) as PushButton;
            moveViewBtn.LargeImage = convertFromBitmap(Properties.Resources.視圖搬移);
            moveViewBtn.ToolTip = "【功能介紹】\n將選取的多張視圖放置到指定圖紙，供使用者集中安排出圖內容。";
            moveViewBtn.LongDescription = "【使用步驟】\n1. 開啟專案，確認目標圖紙已建立，點選「視圖搬移」。\n2. 勾選要放置的視圖，並選擇目標圖紙。\n3. 按「確定」，程式會將可放置的視圖置於圖紙中央。\n4. 開啟目標圖紙，調整視埠位置與比例；無法加入的視圖請檢查是否已放置於其他圖紙。";
            PushButton exportViewBtn = ribbonPanel.AddItem(new PushButtonData("ExportView", "圖紙匯出", addinAssmeblyPath, "Sinotech_2020.Plotting.ExportDWG")) as PushButton;
            exportViewBtn.LargeImage = convertFromBitmap(Properties.Resources.圖紙匯出);
            exportViewBtn.ToolTip = "【功能介紹】\n批次匯出選取的 Revit 圖紙，並依圖紙參數組合輸出檔名。";
            exportViewBtn.LongDescription = "【使用步驟】\n1. 確認圖紙內容已完成，點選「圖紙匯出」。\n2. 勾選要匯出的圖紙，選擇匯出格式及對應設定。\n3. 選擇要作為輸出檔名的圖紙參數。\n4. 執行匯出並選擇輸出資料夾，完成後核對檔案數量、名稱與圖面內容。";

            PushButton batchNwcExportBtn = ribbonPanel.AddItem(new PushButtonData("BatchNwcExport", "批次匯出", addinAssmeblyPath, "Sinotech.BatchExport.BatchNwcExport")
            {
                AvailabilityClassName = "Sinotech.BatchExport.BatchNwcExportAvailability"
            }) as PushButton;
            batchNwcExportBtn.LargeImage = convertFromBitmap(Properties.Resources.批次匯出);
            batchNwcExportBtn.ToolTip = "【功能介紹】\n將資料夾內與目前 Revit 同年度的 RVT 批次匯出為 NWC；先集中確認主檔 3D 視圖，成果存於各 RVT 旁。";
            batchNwcExportBtn.LongDescription = "【使用步驟】\n1. 先安裝並啟用相容的 Navisworks NWC Export Utility，已開啟的主檔會使用目前內容（含未儲存變更）。\n2. 點選「批次匯出」，貼上或選擇資料夾（會記住上次路徑）、優先視圖名稱（預設 {3D}），設定子資料夾、連結、座標及覆寫選項。\n3. 按「讀取視圖」，集中確認各主檔的 3D 視圖；無法唯一配對的項目需手動選擇或取消勾選。\n4. 按「開始匯出」，完成後核對 NWC 與 CSV 紀錄；主檔版本不符會略過；有問題的連結會列出提醒，原始 RVT 不儲存。";
            batchNwcExportBtn.ToolTipImage = convertFromBitmap(Properties.Resources.批次匯出_原圖);

            // 添加「快速工具」面板
            try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "快速工具"); }
            catch
            {
                List<RibbonPanel> panel_list = new List<RibbonPanel>();
                panel_list = application.GetRibbonPanels("中興自動化");
                foreach (RibbonPanel rp in panel_list) { if (rp.Name == "快速工具") { ribbonPanel = rp; } }
            }
            PushButton autoJoinBtn = ribbonPanel.AddItem(new PushButtonData("AutoJoin", "自動接合", addinAssmeblyPath, "Sinotech_2020.SpeedTool.AutoJoin")) as PushButton;
            autoJoinBtn.LargeImage = convertFromBitmap(Properties.Resources.自動接合);
            autoJoinBtn.ToolTip = "【功能介紹】\n依目前視圖中的主體元件與指定品類，批次執行柱、樑、牆及板的幾何接合或取消接合。";
            autoJoinBtn.LongDescription = "【使用步驟】\n1. 切換至包含要處理元件的視圖，點選「自動接合」。\n2. 在「主體元件」選擇柱、樑、牆或板，並勾選「接合品類」。\n3. 按「確定」執行接合；需要解除時使用「取消接合」。\n4. 核對交界處的幾何與接合結果，再儲存模型。";
            PushButton parkOrRoomBtn = ribbonPanel.AddItem(new PushButtonData("ParkOrRoom", "雲形線編號", addinAssmeblyPath, "Sinotech_2020.SpeedTool.ParkOrRoom")) as PushButton;
            parkOrRoomBtn.LargeImage = convertFromBitmap(Properties.Resources.雲形線編號);
            parkOrRoomBtn.ToolTip = "【功能介紹】\n沿選取的模型雲形線路徑，依序更新目前視圖中房間或停車格的編號。";
            parkOrRoomBtn.LongDescription = "【使用步驟】\n1. 在目前視圖準備穿過目標房間或停車格的模型雲形線。\n2. 點選「雲形線編號」，選取該雲形線。\n3. 選擇房間或停車格，設定起始編號、前後綴及數字 4 的處理方式，按「確定」。\n4. 核對沿線的編號順序與結果，再儲存模型。";
            PushButton autoUpdateBtn = ribbonPanel.AddItem(new PushButtonData("AutoUpdate", "自動升版", addinAssmeblyPath, "Sinotech_2020.SpeedTool.AutoUpdate")) as PushButton;
            autoUpdateBtn.LargeImage = convertFromBitmap(Properties.Resources.自動升版);
            autoUpdateBtn.ToolTip = "【功能介紹】\n以目前 Revit 版本逐一開啟來源資料夾及子資料夾內的 RVT，並另存至指定資料夾。";
            autoUpdateBtn.LongDescription = "【使用步驟】\n1. 先儲存目前開啟的模型；處理過程會切換文件並關閉前一份文件。\n2. 點選「自動升版」，選擇來源 RVT 資料夾。\n3. 選擇另存的目標資料夾，等待逐檔開啟與儲存。\n4. 查看失敗清單並核對目標檔案；高於目前 Revit 年度的檔案無法降版。";

            // 添加「CSD」面板
            try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "CSD"); }
            catch
            {
                List<RibbonPanel> panel_list = new List<RibbonPanel>();
                panel_list = application.GetRibbonPanels("中興自動化");
                foreach (RibbonPanel rp in panel_list) { if (rp.Name == "CSD") { ribbonPanel = rp; } }
            }
            PushButton autoPipeTagBtn = ribbonPanel.AddItem(new PushButtonData("AutoPipeTag", "自動標籤", addinAssmeblyPath, "Sinotech_2020.CSD.AutoPipeTag")) as PushButton;
            autoPipeTagBtn.LargeImage = convertFromBitmap(Properties.Resources.自動標籤);
            autoPipeTagBtn.ToolTip = "【功能介紹】\n依選取的模型、出圖視圖與長度條件，批次建立管道、風管及電纜架標籤。";
            autoPipeTagBtn.LongDescription = "【使用步驟】\n1. 確認所需標籤族群已載入，點選按鈕並選擇主檔或已載入的連結模型。\n2. 選擇視圖類型與要處理的出圖視圖，設定必標與不標的長度條件。\n3. 依出現的選項確認引線設定，執行批次標籤。\n4. 查看完成結果，核對各視圖的標籤內容與位置，再儲存模型。";
            PushButton tagArrayBtn = ribbonPanel.AddItem(new PushButtonData("TagArray", "標籤排序", addinAssmeblyPath, "Sinotech_2020.CSD.TagArray")) as PushButton;
            tagArrayBtn.LargeImage = convertFromBitmap(Properties.Resources.標籤排序);
            tagArrayBtn.ToolTip = "【功能介紹】\n批次調整選取的 CSD 出圖視圖中的標籤位置，整理排列並保留原有引線設定。";
            tagArrayBtn.LongDescription = "【使用步驟】\n1. 先完成管線標籤，點選 CSD 面板的「標籤排序」。\n2. 選擇視圖類型，再勾選要處理的出圖視圖。\n3. 按「確定」執行標籤排列。\n4. 核對各視圖的文字位置與引線，必要時手動微調後儲存。";

            // 添加「SEM」面板
            try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "SEM"); }
            catch
            {
                List<RibbonPanel> panel_list = new List<RibbonPanel>();
                panel_list = application.GetRibbonPanels("中興自動化");
                foreach (RibbonPanel rp in panel_list) { if (rp.Name == "SEM") { ribbonPanel = rp; } }
            }
            PushButton autoPipeOpenBtn = ribbonPanel.AddItem(new PushButtonData("AutoPipeOpen", "自動開口", addinAssmeblyPath, "Sinotech_2020.SEM.LinkOpening")) as PushButton;
            autoPipeOpenBtn.LargeImage = convertFromBitmap(Properties.Resources.自動開口);
            autoPipeOpenBtn.ToolTip = "【功能介紹】\n檢查已載入連結模型中機電管線與牆、樑、板的交會，於主檔建立對應的開口與套管。";
            autoPipeOpenBtn.LongDescription = "【使用步驟】\n1. 開啟開口作業主檔，確認檔名符合專案規則，並載入、定位相關模型及所需開口族群。\n2. 點選「自動開口」，在設定視窗選擇連結模型及專案、專業代碼等資料。\n3. 核對設定後執行，等待干涉檢查與開口建立。\n4. 依完成訊息核對開口數量、尺寸、高程與位置，再儲存模型。";
            PushButton autoNumberBtn = ribbonPanel.AddItem(new PushButtonData("AutoNumber", "自動編號", addinAssmeblyPath, "Sinotech_2020.SEM.AutoNumber")) as PushButton;
            autoNumberBtn.LargeImage = convertFromBitmap(Properties.Resources.自動編號);
            autoNumberBtn.ToolTip = "【功能介紹】\n依樓層、相關連結管線與開口位置，批次更新開口及套管的流水號。";
            autoNumberBtn.LongDescription = "【使用步驟】\n1. 先確認主檔開口、套管及相關連結模型已準備完成。\n2. 點選「自動編號」，程式會直接開始處理。\n3. 等待完成訊息，查看無法編號的項目。\n4. 核對各樓層的流水號與重複情況，再儲存模型。";
            PushButton manualPipeTagBtn = ribbonPanel.AddItem(new PushButtonData("ManualPipeTag", "手動編號", addinAssmeblyPath, "Sinotech_2020.SEM.ManualPipeTag")) as PushButton;
            manualPipeTagBtn.LargeImage = convertFromBitmap(Properties.Resources.手動編號);
            manualPipeTagBtn.ToolTip = "【功能介紹】\n依使用者點選順序，從指定起始值逐一更新開口或套管的流水號。";
            manualPipeTagBtn.LongDescription = "【使用步驟】\n1. 確認主檔已建立開口或套管，點選「手動編號」。\n2. 輸入起始流水號，按「確定」。\n3. 依需要的編號順序逐一點選開口或套管，流水號會自動遞增。\n4. 按 Esc 結束點選，核對編號結果後儲存模型。";
            PushButton PCCESBtn = ribbonPanel.AddItem(new PushButtonData("OutPutPCCES", "PCCES", addinAssmeblyPath, "Sinotech_2020.SEM.OutPutPCCES")) as PushButton;
            PCCESBtn.LargeImage = convertFromBitmap(Properties.Resources.PCCES);
            PCCESBtn.ToolTip = "【功能介紹】\n統計主檔中的開口、套管及相關數量，依工程項目對照寫入工程數量詳細表 Excel。";
            PCCESBtn.LongDescription = "【使用步驟】\n1. 準備含有 All Sheet 對照資料的工程數量詳細表 Excel 範本，先關閉該檔案。\n2. 開啟模型，點選「PCCES」，選擇要寫入的 Excel 範本。\n3. 等待程式統計並寫入各工作表。\n4. 開啟 Excel，核對工程項目、數量、單位及計算式。";

            // 添加「元件保護」面板
            try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "元件保護"); }
            catch
            {
                List<RibbonPanel> panel_list = new List<RibbonPanel>();
                panel_list = application.GetRibbonPanels("中興自動化");
                foreach (RibbonPanel rp in panel_list) { if (rp.Name == "元件保護") { ribbonPanel = rp; } }
            }
            PushButton familyInstanceLockBtn = ribbonPanel.AddItem(new PushButtonData("FamilyInstanceLock", "元件鎖定", addinAssmeblyPath, "Sinotech_2020.FamilyProtect.FamilyProtect")) as PushButton;
            familyInstanceLockBtn.LargeImage = convertFromBitmap(Properties.Resources.元件鎖定);
            familyInstanceLockBtn.ToolTip = "【功能介紹】\n開啟元件保護視窗，依選取的族群鎖定對應元件，降低誤改模型的機會。";
            familyInstanceLockBtn.LongDescription = "【使用步驟】\n1. 開啟要保護的專案，點選「元件鎖定」。\n2. 在元件保護視窗勾選要鎖定的族群，可使用「全選」或「全部取消」。\n3. 按「確定」套用至所選族群的元件。\n4. 核對鎖定結果後關閉視窗，再儲存模型。";
            //PushButton lockOneBtn = ribbonPanel.AddItem(new PushButtonData("LockOne", "單一元件鎖定", addinAssmeblyPath, "Sinotech_2020.FamilyProtect.LockOne")) as PushButton;
            //lockOneBtn.LargeImage = convertFromBitmap(Properties.Resources.元件鎖定);

            //// 添加「快速翻模」面板
            //try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "快速翻模"); }
            //catch
            //{
            //    List<RibbonPanel> panel_list = new List<RibbonPanel>();
            //    panel_list = application.GetRibbonPanels("中興自動化");
            //    foreach (RibbonPanel rp in panel_list) { if (rp.Name == "快速翻模") { ribbonPanel = rp; } }
            //}
            //PushButton autoColumnsBtn = ribbonPanel.AddItem(new PushButtonData("AutoColumns", "自動翻柱", addinAssmeblyPath, "Sinotech_2020.CreateModel.AutoColumn")) as PushButton;
            //autoColumnsBtn.LargeImage = convertFromBitmap(Properties.Resources.自動翻柱);
            //PushButton autoPipeBtn = ribbonPanel.AddItem(new PushButtonData("AutoPipe", "自動建管", addinAssmeblyPath, "Sinotech_2020.CreateModel.AutoPipe")) as PushButton;
            //autoPipeBtn.LargeImage = convertFromBitmap(Properties.Resources.自動標籤);

            //// 添加「規範校核」面板
            //try { ribbonPanel = application.CreateRibbonPanel("中興自動化", "規範校核"); }
            //catch
            //{
            //    List<RibbonPanel> panel_list = new List<RibbonPanel>();
            //    panel_list = application.GetRibbonPanels("中興自動化");
            //    foreach (RibbonPanel rp in panel_list) { if (rp.Name == "規範校核") { ribbonPanel = rp; } }
            //}
            //PushButton crushReportBtn = ribbonPanel.AddItem(new PushButtonData("CrushReport", "干涉報告", addinAssmeblyPath, "Sinotech_2020.Verification.CrushReport")) as PushButton;
            //crushReportBtn.LargeImage = convertFromBitmap(Properties.Resources.干涉報告);
            //PushButton copyFilesBtn = ribbonPanel.AddItem(new PushButtonData("CopyFiles", "規範詳圖", addinAssmeblyPath, "Sinotech_2020.Verification.CopyFiles")) as PushButton;
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
