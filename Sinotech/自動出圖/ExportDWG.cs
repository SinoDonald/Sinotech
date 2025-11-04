using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinForms = System.Windows.Forms;

namespace Sinotech
{
    [Transaction(TransactionMode.Manual)]
    public class ExportDWG : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elementSet)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            ChooseView chooseView = new ChooseView(uiapp, app, doc);
            chooseView.ShowDialog();

            return Result.Succeeded;
        }
    }
}
