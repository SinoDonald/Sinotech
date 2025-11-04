using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Sinotech_2025
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    //[Transaction(TransactionMode.Automatic)]
    public class AutoJoin : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
                               ref string message,
                               ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;

            // 呼叫視窗, 選取要執行接合的Category
            AutoJoinForm autoJoinForm = new AutoJoinForm(uidoc, doc, view);
            autoJoinForm.ShowDialog();
            return Result.Succeeded;
        }
    }
}
