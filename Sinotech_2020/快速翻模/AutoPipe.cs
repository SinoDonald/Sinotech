using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sinotech_2020
{
    [Transaction(TransactionMode.Manual)]
    public class AutoPipe : IExternalCommand
    {
        public static List<Line> lineList = new List<Line>(); // 收集所有的Line
        public static ICollection<Element> pipeSystemTypes = null; // 找到所有PipeSystemType
        public static ICollection<Element> pipeTypes = null; // 找到所有PipeType
        public static ICollection<Element> levels = null; // 所有的Level
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;
            Level level = doc.ActiveView.GenLevel; // 當前Level

            // 讀取圖面上的dwg圖
            FilteredElementCollector dwgCollector = new FilteredElementCollector(doc, doc.ActiveView.Id);
            ICollection<Element> dwgElems = dwgCollector.OfClass(typeof(ImportInstance)).WhereElementIsNotElementType().ToElements();
            Element dwgElem = dwgElems.FirstOrDefault();
            // 讀取dwg檔
            Options opt = new Options();
            opt.View = uidoc.ActiveView;
            opt.ComputeReferences = true;
            GeometryElement geomElem = dwgElem.get_Geometry(opt);
            SaveCADLinkData(geomElem); // 儲存CADLink資訊

            // 找到所有PipeSystemType = 系統類型
            pipeSystemTypes = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).ToElements();
            ElementId pipeSystemTypeId = (from x in pipeSystemTypes
                                          select x).FirstOrDefault().Id;
            // 找到所有PipeType = 管類型
            pipeTypes = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).ToElements();
            ElementId pipeTypeId = (from x in pipeTypes
                                    select x).FirstOrDefault().Id;
            // 找到所有Level
            levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).ToElements();

            AutoPipeForm autoPipeForm = new AutoPipeForm(doc);
            autoPipeForm.ShowDialog();

            uidoc.RefreshActiveView();
            //doc.Regenerate();

            return Result.Succeeded;
        }
        // 儲存CADLink資訊
        private void SaveCADLinkData(GeometryElement geomElem)
        {
            foreach (GeometryObject geomObj in geomElem)
            {
                if(geomObj is Line)
                {
                    Line line = (Line)geomObj;
                    lineList.Add(line);
                }
                else if (geomObj is PolyLine)
                {
                    PolyLine polyLine = (PolyLine)geomObj;
                    IList<XYZ> xyzs = polyLine.GetCoordinates();
                    for(int i = 0; i < xyzs.Count()-1; i++)
                    {
                        Line line = Line.CreateBound(xyzs[i], xyzs[i + 1]);
                        lineList.Add(line);
                    }
                }
                else if (geomObj is Solid)
                {
                    Solid solid = (Solid)geomObj;
                    if (solid.Edges.Size > 0)
                    {
                        try
                        {
                            EdgeArray edgeArray = solid.Edges;
                            foreach (Edge edge in edgeArray)
                            {
                                Line line = edge.AsCurve() as Line;
                                lineList.Add(line);
                            }
                        }
                        catch (Exception)
                        {

                        }
                    }
                }
                else if (geomObj is GeometryInstance)
                {
                    geomElem = (geomObj as GeometryInstance).GetSymbolGeometry();
                    SaveCADLinkData(geomElem);
                }
                else if (geomObj is GeometryElement)
                {
                    GeometryElement geomElem2 = (GeometryElement)geomObj;
                    SaveCADLinkData(geomElem2);
                }
            }
        }
    }
}
