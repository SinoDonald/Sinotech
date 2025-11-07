using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Sinotech_2020.UpdateView
{
    [Transaction(TransactionMode.Manual)]
    public class EditViewSheetNumber : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            EditViewSheetNumberForm editViewSheetNumberForm = new EditViewSheetNumberForm(doc);
            editViewSheetNumberForm.ShowDialog();

            return Result.Succeeded;
        }
    }
}
