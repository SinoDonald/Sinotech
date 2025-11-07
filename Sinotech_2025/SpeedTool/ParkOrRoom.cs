using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace Sinotech_2025.SpeedTool
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ParkOrRoom : IExternalCommand
    {
        public class ModelCurvePoint
        {
            public double x = 0.0, y = 0.0, z = 0.0;
            public int i = 0;
        }
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            try
            {
                // 點選雲形線, 並儲存所有節點
                Reference modelCurveRef = uidoc.Selection.PickObject(ObjectType.Element, new ModelNurbSplineSelectionFilter()); // 只能點選雲形線
                ModelNurbSpline modelNurbSpline = doc.GetElement(modelCurveRef) as ModelNurbSpline; // 轉型
                IList<XYZ> xyzList = modelNurbSpline.GeometryCurve.Tessellate();

                // 彈跳視窗, 選擇要編號的品類
                ParkOrRoomForm parkOrRoomForm = new ParkOrRoomForm();
                parkOrRoomForm.ShowDialog();
                if(parkOrRoomForm.yesOrNo == true) // 確定
                {
                    List<Element> editElems = new List<Element>(); // 儲存所有要編輯的元件                    
                    foreach (XYZ xyz in xyzList)
                    {
                        BoundingBoxContainsPointFilter CPFilter = new BoundingBoxContainsPointFilter(xyz);
                        FilteredElementCollector parkOrRoomColl = new FilteredElementCollector(doc, doc.ActiveView.Id);
                        IList<Element> elems = null;
                        if (parkOrRoomForm.parkOrRoom.Equals("停車格"))
                        {
                            elems = parkOrRoomColl.OfCategory(BuiltInCategory.OST_Parking).WherePasses(CPFilter).WhereElementIsNotElementType().ToElements();
                        }
                        else
                        {
                            elems = parkOrRoomColl.OfCategory(BuiltInCategory.OST_Rooms).WherePasses(CPFilter).WhereElementIsNotElementType().ToElements();
                        }
                        foreach (Element elem in elems)
                        {
                            editElems.Add(elem);
                        }
                    }

                    // 去除重複值, 留下第一個節點碰觸到的元件
                    var editElemsList = (from x in editElems
                                         select x.Id).Distinct();
                    // 自動編號
                    using(Transaction trans = new Transaction(doc, "自動編號"))
                    {
                        trans.Start();
                        int number = Convert.ToInt32(parkOrRoomForm.textBoxNum);
                        foreach(ElementId editElemId in editElemsList)
                        {
                            Element editElem = doc.GetElement(editElemId);
                            string changeText = string.Empty;
                            if (parkOrRoomForm.noFour == true)
                            {
                                if ((number % 10) == 4 || (number % 10) == -4)
                                {
                                    number++;
                                }
                                changeText = parkOrRoomForm.before + number + parkOrRoomForm.behind;
                                NumberEdit(editElem, changeText, parkOrRoomForm.parkOrRoom);
                                number++;
                            }
                            else if (parkOrRoomForm.changeFour == true)
                            {
                                if ((number % 10) == 4 || (number % 10) == -4)
                                {
                                    number--;
                                    changeText = parkOrRoomForm.before + number + parkOrRoomForm.changeSign + parkOrRoomForm.behind;
                                    NumberEdit(editElem, changeText, parkOrRoomForm.parkOrRoom);
                                    number += 2;
                                }
                                else
                                {
                                    changeText = parkOrRoomForm.before + number + parkOrRoomForm.behind;
                                    NumberEdit(editElem, changeText, parkOrRoomForm.parkOrRoom);
                                    number++;
                                }
                            }
                            else
                            {
                                changeText = parkOrRoomForm.before + number + parkOrRoomForm.behind;
                                NumberEdit(editElem, changeText, parkOrRoomForm.parkOrRoom);
                                number++;
                            }
                        }
                        trans.Commit();
                    }
                }
            }
            catch (OperationCanceledException) // ESC取消
            {

            }
            catch (Exception)
            {
                //TaskDialog.Show("Revit", ex.Message + "\n" + ex.ToString());
            }

            return Result.Succeeded;
        }
        // 修改停車格或房間編號
        public void NumberEdit(Element elem, string changText, string parkOrRoom)
        {
            Parameter para = null;
            if (parkOrRoom.Equals("停車格"))
            {
                para = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                para.Set(changText);
            }
            else // 房間
            {
                para = elem.get_Parameter(BuiltInParameter.ROOM_NUMBER);
                para.Set(changText);
            }
        }
        // 只能點選雲形線
        public class ModelNurbSplineSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return (elem is ModelNurbSpline);
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }
    }
}
