using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.ApplicationServices;

namespace Sinotech
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
