using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FamilyInstanceLock
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FamilyConversionToDirectShape : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            FilteredElementCollector collector = new FilteredElementCollector(doc).OfClass(typeof(Family));
            SortedList<string, FamilySymbol> sortedList = new SortedList<string, FamilySymbol>();
            int num = 0, num2 = 0, num3 = 0;

            using(IEnumerator<Element> enumerator = collector.GetEnumerator())
            {
                while (enumerator.MoveNext())
                { 
                    Element element = enumerator.Current;
                }
            }

            return Result.Succeeded;
        }
    }
}
