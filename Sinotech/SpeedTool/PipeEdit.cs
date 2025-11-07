using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Forms;
using Form = System.Windows.Forms.Form;

namespace Sinotech.SpeedTool
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    [Journaling(JournalingMode.NoCommandData)]
    public class PipeEdit : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // 檢查該 Form 是否已經開啟
            Form myForm = Application.OpenForms["PipeEditForm"];

            if (myForm == null)  // 尚未開啟
            {
                PipeEditForm pipeEditForm = new PipeEditForm(commandData.Application.ActiveUIDocument);
                pipeEditForm.Show();
            }
            else // 已經開啟 -> 讓它跳到最前面
            {
                myForm.BringToFront();
                myForm.WindowState = FormWindowState.Normal;
            }

            return Result.Succeeded;
        }
    }
}
