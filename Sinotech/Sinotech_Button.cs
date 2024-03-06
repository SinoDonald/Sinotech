using Autodesk.Revit.UI;
using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Sinotech
{    
    // 預設Excel檔案路徑
    public class LicPath
    {
        public string previous = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        public string pathStr = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\tmp.txt"; // 我的文件
        //public string pathStr = @"C:\ProgramData\Autodesk\Revit\Addins\2018\Sinotech\tmp.txt";
        //public string pathStr = @"C:\ProgramData\Autodesk\Revit\Addins\2019\Sinotech\tmp.txt";
        //public string pathStr = @"C:\ProgramData\Autodesk\Revit\Addins\2020\Sinotech\tmp.txt";
        //public string pathStr = @"C:\ProgramData\Autodesk\Revit\Addins\2021\Sinotech\tmp.txt";
        //public string pathStr = @"C:\ProgramData\Autodesk\Revit\Addins\2022\Sinotech\tmp.txt";
    }
    public class Sinotech_Button : IExternalApplication
    {
        public string assembly = @"C:\ProgramData\Autodesk\Revit\Addins\2020\Sinotech\"; // 封包版路徑位址
        public Result OnStartup(UIControlledApplication application)
        {
            string sinotechAsb = assembly + "Sinotech.dll"; // 圖紙更新
            string autoExportAsb = assembly + "AutoExport.dll"; // 自動出圖
            string speedToolsAsb = assembly + "SpeedTools.dll"; // 快速工具
            string CSDSEMAsb = assembly + "CSDSEM.dll"; // CSD/SEM
            string autoBuildAsb = assembly + "AutoBuild.dll"; // 快速翻模
            string codeViewAsb = assembly + "CodeView.dll"; // 規範校核

            // 創建一個新的選單
            String tabName = "中興自動化";
            application.CreateRibbonTab(tabName);

            //// 添加「圖紙更新」面板
            //RibbonPanel sinotechPanel = application.CreateRibbonPanel(tabName, "圖紙更新");
            //PushButton sinotechBtn = sinotechPanel.AddItem(new PushButtonData("Sinotech_API", "圖紙更新", sinotechAsb, "Sinotech.Sinotech_API")) as PushButton;            
            ////sinotechBtn.LargeImage = new BitmapImage(new Uri(picPath + "圖紙更新.png")); <-- 舊版寫法
            //sinotechBtn.LargeImage = convertFromBitmap(Properties.Resources.圖紙更新);
            //PushButton detectionScaleBtn = sinotechPanel.AddItem(new PushButtonData("DetectionScale", "更新比例尺", sinotechAsb, "Sinotech.DetectionScale")) as PushButton;
            //detectionScaleBtn.LargeImage = convertFromBitmap(Properties.Resources.更新比例尺);
            //PushButton updateExcelBtn = sinotechPanel.AddItem(new PushButtonData("UpdateExcel", "更新Excel", sinotechAsb, "Sinotech.UpdateExcelCell")) as PushButton;
            //updateExcelBtn.LargeImage = convertFromBitmap(Properties.Resources.更新Excel);

            // 添加「自動出圖」面板
            RibbonPanel autoExportPanel = application.CreateRibbonPanel(tabName, "自動出圖");
            //PushButton viewCopyBtn = autoExportPanel.AddItem(new PushButtonData("ViewCopy", "視圖複製", autoExportAsb, "AutoExport.CopyDrawings")) as PushButton;
            //viewCopyBtn.LargeImage = convertFromBitmap(Properties.Resources.視圖複製);
            //PushButton moveViewBtn = autoExportPanel.AddItem(new PushButtonData("MoveView", "視圖搬移", autoExportAsb, "AutoExport.MoveView")) as PushButton;
            //moveViewBtn.LargeImage = convertFromBitmap(Properties.Resources.視圖搬移);
            PushButton exportViewBtn = autoExportPanel.AddItem(new PushButtonData("ExportView", "圖紙匯出", autoExportAsb, "AutoExport.ExportDWG")) as PushButton;
            exportViewBtn.LargeImage = convertFromBitmap(Properties.Resources.圖紙匯出);

            //// 添加「快速工具」面板
            //RibbonPanel speedToolsPanel = application.CreateRibbonPanel(tabName, "快速工具");
            //PushButton autoJoinBtn = speedToolsPanel.AddItem(new PushButtonData("AutoJoin", "自動接合", speedToolsAsb, "SpeedTools.AutoJoin")) as PushButton;
            //autoJoinBtn.LargeImage = convertFromBitmap(Properties.Resources.自動接合);
            //PushButton parkOrRoomBtn = speedToolsPanel.AddItem(new PushButtonData("ParkOrRoom", "雲形線編號", speedToolsAsb, "SpeedTools.ParkOrRoom")) as PushButton;
            //parkOrRoomBtn.LargeImage = convertFromBitmap(Properties.Resources.雲形線編號);
            //PushButton autoUpdateBtn = speedToolsPanel.AddItem(new PushButtonData("AutoUpdate", "自動升版", speedToolsAsb, "SpeedTools.AutoUpdate")) as PushButton;
            //autoUpdateBtn.LargeImage = convertFromBitmap(Properties.Resources.自動升版);

            // 添加「CSD/SEM」面板
            RibbonPanel CSDSEMPanel = application.CreateRibbonPanel(tabName, "CSD/SEM");
            PushButton autoPipeOpenBtn = CSDSEMPanel.AddItem(new PushButtonData("AutoPipeOpen", "自動開口", CSDSEMAsb, "CSDSEM.LinkOpening")) as PushButton;
            autoPipeOpenBtn.LargeImage = convertFromBitmap(Properties.Resources.自動開口);
            //PushButton autoPipeTagBtn = CSDSEMPanel.AddItem(new PushButtonData("AutoPipeTag", "自動標籤", CSDSEMAsb, "CSDSEM.AutoPipeTag")) as PushButton;
            //autoPipeTagBtn.LargeImage = convertFromBitmap(Properties.Resources.自動標籤);
            //PushButton manualPipeTagBtn = CSDSEMPanel.AddItem(new PushButtonData("ManualPipeTag", "手動標籤", CSDSEMAsb, "CSDSEM.ManualPipeTag")) as PushButton;
            //manualPipeTagBtn.LargeImage = convertFromBitmap(Properties.Resources.手動標籤);
            //PushButton PCCESBtn = CSDSEMPanel.AddItem(new PushButtonData("OutPutPCCES", "PCCES", CSDSEMAsb, "CSDSEM.OutPutPCCES")) as PushButton;
            //PCCESBtn.LargeImage = convertFromBitmap(Properties.Resources.PCCES);

            //// 添加「快速翻模」面板
            //RibbonPanel autoBuildPanel = application.CreateRibbonPanel(tabName, "快速翻模");
            //PushButton autoColumnsBtn = autoBuildPanel.AddItem(new PushButtonData("AutoColumns", "自動翻柱", autoBuildAsb, "AutoBuild.AutoColumn")) as PushButton;
            //autoColumnsBtn.LargeImage = convertFromBitmap(Properties.Resources.自動翻柱);
            //PushButton autoPipeBtn = autoBuildPanel.AddItem(new PushButtonData("AutoPipe", "自動建管", autoBuildAsb, "AutoBuild.AutoPipe")) as PushButton;
            //autoPipeBtn.LargeImage = convertFromBitmap(Properties.Resources.自動標籤);

            //// 添加「規範校核」面板
            //RibbonPanel codeViewPanel = application.CreateRibbonPanel(tabName, "規範校核");
            //PushButton crushReportBtn = codeViewPanel.AddItem(new PushButtonData("CrushReport", "干涉報告", codeViewAsb, "CodeView.CrushReport")) as PushButton;
            //crushReportBtn.LargeImage = convertFromBitmap(Properties.Resources.干涉報告);
            //PushButton copyFilesBtn = codeViewPanel.AddItem(new PushButtonData("CopyFiles", "規範詳圖", codeViewAsb, "CodeView.CopyFiles")) as PushButton;
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
