using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System;
using System.Windows.Forms;
using Form = System.Windows.Forms.Form;

namespace Sinotech
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    [Journaling(JournalingMode.NoCommandData)]
    public class FamilyProtect : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            //commandData.Application.Idling += Application_Idling;
            RevitDocument m_connect = new RevitDocument(commandData.Application);

            // 檢查該 Form 是否已經開啟, 需要重整ListViewItem
            Form myForm = Application.OpenForms["ChooseElems"];

            if (myForm == null)  // 尚未開啟
            {
                ChooseElems form1 = new ChooseElems(commandData.Application, m_connect);
                form1.Show();
            }
            else // 已經開啟 -> 讓它跳到最前面
            {
                myForm.BringToFront();
                myForm.WindowState = FormWindowState.Normal;
            }

            return Result.Succeeded;
        }
        private void Application_Idling(object sender, IdlingEventArgs e)
        {
            throw new NotImplementedException();
        }
    }
}
