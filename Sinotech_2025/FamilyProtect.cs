using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System;

namespace Sinotech_2025
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    [Journaling(JournalingMode.NoCommandData)]
    public class FamilyProtect : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            IExternalEventHandler handler_FamilyConversionToDirectShape = new FamilyConversionToDirectShape();
            ExternalEvent externalEvent_FamilyConversionToDirectShape = ExternalEvent.Create(handler_FamilyConversionToDirectShape);
            //commandData.Application.Idling += Application_Idling;
            RevitDocument m_connect = new RevitDocument(commandData.Application);
            ChooseElems chooseElemsform = new ChooseElems(commandData.Application, m_connect, externalEvent_FamilyConversionToDirectShape);
            if (chooseElemsform.trueOrFalse) { chooseElemsform.Show(); }

            return Result.Succeeded;
        }
        private void Application_Idling(object sender, IdlingEventArgs e)
        {
            throw new NotImplementedException();
        }
    }
}
