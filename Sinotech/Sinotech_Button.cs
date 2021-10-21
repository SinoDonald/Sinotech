using Autodesk.Revit.UI;
using System;
using System.Windows.Media.Imaging;

namespace Sinotech
{
    // 封包版路徑位址
    public class PacketPathName
    {
        //// 2016
        //// .dll檔案存取路徑
        //public string assembly = @"C:\ProgramData\Autodesk\Revit\Addins\2016\Sinotech\";
        //// Button圖片存取路徑
        //public string picPath = @"C:\ProgramData\Autodesk\Revit\Addins\2016\Sinotech\pic\";
        //// 2017
        //public string assembly = @"C:\ProgramData\Autodesk\Revit\Addins\2017\Sinotech\";
        //public string picPath = @"C:\ProgramData\Autodesk\Revit\Addins\2017\Sinotech\pic\";
        //// 2018
        public string assembly = @"C:\ProgramData\Autodesk\Revit\Addins\2018\Sinotech\";
        public string picPath = @"C:\ProgramData\Autodesk\Revit\Addins\2018\Sinotech\pic\";
        //// 2019
        //public string assembly = @"C:\ProgramData\Autodesk\Revit\Addins\2019\Sinotech\";
        //public string picPath = @"C:\ProgramData\Autodesk\Revit\Addins\2019\Sinotech\pic\";
        //// 2020
        //public string assembly = @"C:\ProgramData\Autodesk\Revit\Addins\2020\Sinotech\";
        //public string picPath = @"C:\ProgramData\Autodesk\Revit\Addins\2020\Sinotech\pic\";
    }
    // 預設Excel檔案路徑
    public class LicPath
    {
        public string previous = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        public string pathStr = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\tmp.txt"; // 我的文件
        //public string pathStr = @"C:\ProgramData\Autodesk\Revit\Addins\2016\Sinotech\tmp.txt";
        //public string pathStr = @"C:\ProgramData\Autodesk\Revit\Addins\2017\Sinotech\tmp.txt";
        //public string pathStr = @"C:\ProgramData\Autodesk\Revit\Addins\2018\Sinotech\tmp.txt";
        //public string pathStr = @"C:\ProgramData\Autodesk\Revit\Addins\2019\Sinotech\tmp.txt";
        //public string pathStr = @"C:\ProgramData\Autodesk\Revit\Addins\2020\Sinotech\tmp.txt";
    }
    public class Sinotech_Button : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            // 封包檔案
            PacketPathName packetPathName = new PacketPathName();
            string sinotechAsb = packetPathName.assembly + "Sinotech.dll"; // 圖紙更新
            string autoExportAsb = packetPathName.assembly + "AutoExport.dll"; // 自動出圖
            string speedToolsAsb = packetPathName.assembly + "SpeedTools.dll"; // 快速工具
            string CSDSEMAsb = packetPathName.assembly + "CSDSEM.dll"; // CSD/SEM
            string autoBuildAsb = packetPathName.assembly + "AutoBuild.dll"; // 快速翻模
            string codeViewAsb = packetPathName.assembly + "CodeView.dll"; // 規範校核
            string picPath = packetPathName.picPath;

            // 創建一個新的選單
            String tabName = "中興自動化";
            application.CreateRibbonTab(tabName);

            // 添加圖紙更新面板
            RibbonPanel sinotechPanel = application.CreateRibbonPanel(tabName, "圖紙更新");
            // 在面板上添加一個按鈕, 點擊此按鈕觸動CECI.Sinotech_API
            PushButton sinotechBtn = sinotechPanel.AddItem(new PushButtonData("Sinotech_API", "圖紙更新", sinotechAsb, "Sinotech.Sinotech_API")) as PushButton;
            // 給按鈕添加一個圖片
            Uri sinotechImage = new Uri(picPath + "圖紙更新.png");
            BitmapImage sinotechLargeImage = new BitmapImage(sinotechImage);
            sinotechBtn.LargeImage = sinotechLargeImage;
            PushButton detectionScaleBtn = sinotechPanel.AddItem(new PushButtonData("DetectionScale", "更新比例尺", sinotechAsb, "Sinotech.DetectionScale")) as PushButton;
            Uri detectionScaleImage = new Uri(picPath + "更新比例尺.png");
            BitmapImage detectionScaleLargeImage = new BitmapImage(detectionScaleImage);
            detectionScaleBtn.LargeImage = detectionScaleLargeImage;
            PushButton updateExcelBtn = sinotechPanel.AddItem(new PushButtonData("UpdateExcel", "更新Excel", sinotechAsb, "Sinotech.UpdateExcelCell")) as PushButton;
            Uri updateExcelImage = new Uri(picPath + "更新Excel.png");
            BitmapImage updateExcelLargeImage = new BitmapImage(updateExcelImage);
            updateExcelBtn.LargeImage = updateExcelLargeImage;

            // 視圖複製
            RibbonPanel autoExportPanel = application.CreateRibbonPanel(tabName, "自動出圖");
            PushButton viewCopyBtn = autoExportPanel.AddItem(new PushButtonData("ViewCopy", "視圖複製", autoExportAsb, "AutoExport.CopyDrawings")) as PushButton;
            Uri viewCopyImage = new Uri(picPath + "視圖複製.png");
            BitmapImage viewCopyLargeImage = new BitmapImage(viewCopyImage);
            viewCopyBtn.LargeImage = viewCopyLargeImage;
            // 視圖搬移
            PushButton moveViewBtn = autoExportPanel.AddItem(new PushButtonData("MoveView", "視圖搬移", autoExportAsb, "AutoExport.MoveView")) as PushButton;
            Uri moveViewImage = new Uri(picPath + "視圖搬移.png");
            BitmapImage moveViewLargeImage = new BitmapImage(moveViewImage);
            moveViewBtn.LargeImage = moveViewLargeImage;
            // 圖紙匯出
            PushButton exportViewBtn = autoExportPanel.AddItem(new PushButtonData("ExportView", "圖紙匯出", autoExportAsb, "AutoExport.ExportDWG")) as PushButton;
            Uri exportViewImage = new Uri(picPath + "圖紙匯出.png");
            BitmapImage exportViewLargeImage = new BitmapImage(exportViewImage);
            exportViewBtn.LargeImage = exportViewLargeImage;

            // 自動接合
            RibbonPanel speedToolsPanel = application.CreateRibbonPanel(tabName, "快速工具");
            PushButton autoJoinBtn = speedToolsPanel.AddItem(new PushButtonData("AutoJoin", "自動接合", speedToolsAsb, "SpeedTools.AutoJoin")) as PushButton;
            Uri autoJoinImage = new Uri(picPath + "自動接合.png");
            BitmapImage autoJoinLargeImage = new BitmapImage(autoJoinImage);
            autoJoinBtn.LargeImage = autoJoinLargeImage;
            // 停車格編號
            PushButton parkOrRoomBtn = speedToolsPanel.AddItem(new PushButtonData("ParkOrRoom", "雲形線編號", speedToolsAsb, "SpeedTools.ParkOrRoom")) as PushButton;
            Uri parkOrRoomImage = new Uri(picPath + "雲形線編號.png");
            BitmapImage parkOrRoomLargeImage = new BitmapImage(parkOrRoomImage);
            parkOrRoomBtn.LargeImage = parkOrRoomLargeImage;
            // 自動升版
            PushButton autoUpdateBtn = speedToolsPanel.AddItem(new PushButtonData("AutoUpdate", "自動升版", speedToolsAsb, "SpeedTools.AutoUpdate")) as PushButton;
            Uri autoUpdateImage = new Uri(picPath + "自動升版.png");
            BitmapImage autoUpdateLargeImage = new BitmapImage(autoUpdateImage);
            autoUpdateBtn.LargeImage = autoUpdateLargeImage;

            // 自動開口
            RibbonPanel CSDSEMPanel = application.CreateRibbonPanel(tabName, "CSD/SEM");
            PushButton autoPipeOpenBtn = CSDSEMPanel.AddItem(new PushButtonData("AutoPipeOpen", "自動開口", CSDSEMAsb, "CSDSEM.LinkOpening")) as PushButton;
            Uri autoPipeOpenImage = new Uri(picPath + "自動開口.png");
            BitmapImage autoPipeOpenLargeImage = new BitmapImage(autoPipeOpenImage);
            autoPipeOpenBtn.LargeImage = autoPipeOpenLargeImage;
            // 自動標籤
            PushButton autoPipeTagBtn = CSDSEMPanel.AddItem(new PushButtonData("AutoPipeTag", "自動標籤", CSDSEMAsb, "CSDSEM.AutoPipeTag")) as PushButton;
            Uri autoPipeTagImage = new Uri(picPath + "自動標籤.png");
            BitmapImage autoPipeTagLargeImage = new BitmapImage(autoPipeTagImage);
            autoPipeTagBtn.LargeImage = autoPipeTagLargeImage;
            // 手動標籤
            PushButton manualPipeTagBtn = CSDSEMPanel.AddItem(new PushButtonData("ManualPipeTag", "手動標籤", CSDSEMAsb, "CSDSEM.ManualPipeTag")) as PushButton;
            Uri manualPipeTagImage = new Uri(picPath + "手動標籤.png");
            BitmapImage manualPipeTagLargeImage = new BitmapImage(manualPipeTagImage);
            manualPipeTagBtn.LargeImage = manualPipeTagLargeImage;
            // 數量計算
            PushButton PCCESBtn = CSDSEMPanel.AddItem(new PushButtonData("OutPutPCCES", "PCCES", CSDSEMAsb, "CSDSEM.OutPutPCCES")) as PushButton;
            Uri PCCESImage = new Uri(picPath + "PCCES.png");
            BitmapImage PCCESLargeImage = new BitmapImage(PCCESImage);
            PCCESBtn.LargeImage = PCCESLargeImage;

            //// 自動翻柱
            //RibbonPanel autoBuildPanel = application.CreateRibbonPanel(tabName, "快速翻模");
            //PushButton autoColumnsBtn = autoBuildPanel.AddItem(new PushButtonData("AutoColumns", "自動翻柱", autoBuildAsb, "AutoBuild.AutoColumn")) as PushButton;
            //Uri autoColumnsImage = new Uri(picPath + "自動翻柱.png");
            //BitmapImage autoColumnsLargeImage = new BitmapImage(autoColumnsImage);
            //autoColumnsBtn.LargeImage = autoColumnsLargeImage;
            //// 自動建管
            //PushButton autoPipeBtn = autoBuildPanel.AddItem(new PushButtonData("AutoPipe", "自動建管", autoBuildAsb, "AutoBuild.AutoPipe")) as PushButton;
            //Uri autoPipeImage = new Uri(picPath + "自動標籤.png");
            //BitmapImage autoPipeLargeImage = new BitmapImage(autoPipeImage);
            //autoPipeBtn.LargeImage = autoPipeLargeImage;

            //// 干涉報告
            //RibbonPanel codeViewPanel = application.CreateRibbonPanel(tabName, "規範校核");
            //PushButton crushReportBtn = codeViewPanel.AddItem(new PushButtonData("CrushReport", "干涉報告", codeViewAsb, "CodeView.CrushReport")) as PushButton;
            //Uri crushReportImage = new Uri(picPath + "干涉報告.png");
            //BitmapImage crushReportLargeImage = new BitmapImage(crushReportImage);
            //crushReportBtn.LargeImage = crushReportLargeImage;
            //// 規範詳圖
            //PushButton copyFilesBtn = codeViewPanel.AddItem(new PushButtonData("CopyFiles", "規範詳圖", codeViewAsb, "CodeView.CopyFiles")) as PushButton;
            //Uri copyFilesImage = new Uri(picPath + "規範詳圖.png");
            //BitmapImage copyFilesLargeImage = new BitmapImage(copyFilesImage);
            //copyFilesBtn.LargeImage = copyFilesLargeImage;


            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
