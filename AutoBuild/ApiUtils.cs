using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;

namespace AutoBuild
{
    public class ApiUtils
    {
        // 儲存CADLink的名稱、長、寬
        public class CADLinkValue
        {
            // CAD Type (PolyLine or Line or Arc or Solid)
            public string type { get; set; }
            // 中心點
            public XYZ center { get; set; }
            // 軸心
            public Line axis { get; set; }
            // 角度
            public double angle { get; set; }
            // 名稱
            public string name { get; set; }
            // 長
            public double length { get; set; }
            // 寬
            public double width { get; set; }
            // 半徑
            public double radius { get; set; }
        }
        // 讀取幾何圖形, 儲存所有的PolyLine與Arc
        public static List<CADLinkValue> SaveCADLinkValue(UIDocument uidoc, Element elem)
        {
            List<CADLinkValue> cadLinkValueList = new List<CADLinkValue>(); // 儲存所有CADLink的長寬

            Options opt = new Options();
            opt.View = uidoc.ActiveView;
            opt.ComputeReferences = true;
            GeometryElement geomElem = elem.get_Geometry(opt);
            foreach (GeometryObject geoObj in geomElem)
            {
                if (geoObj is GeometryInstance)
                {
                    GeometryInstance geoIns = geoObj as GeometryInstance;
                    IEnumerator<GeometryObject> geoList = geoIns.GetInstanceGeometry().GetEnumerator();
                    geoList.Reset();
                    while (geoList.MoveNext())
                    {
                        XYZ center = new XYZ(); // 中心點
                        Line axis = null; // 軸心
                        double angle = 0.0; // 角度
                        double radius = 0.0; // 半徑

                        CADLinkValue cadLinkValue = new CADLinkValue();
                        if (geoList.Current is PolyLine || geoList.Current is Line)
                        {
                            PolyLine polyLine = geoList.Current as PolyLine;
                            Line line = geoList.Current as Line;
                            IList<XYZ> xyzList = new List<XYZ>();
                            if (polyLine != null)
                            {
                                xyzList = polyLine.GetCoordinates();
                            }
                            else
                            {
                                xyzList = line.Tessellate();
                            }
                            if(xyzList.Count >= 4)
                            {
                                center = new XYZ((xyzList[0].X + xyzList[1].X + xyzList[2].X + xyzList[3].X) / 4,
                                                    (xyzList[0].Y + xyzList[1].Y + xyzList[2].Y + xyzList[3].Y) / 4,
                                                    (xyzList[0].Z + xyzList[1].Z + xyzList[2].Z + xyzList[3].Z) / 4);                                
                                axis = Line.CreateBound(center, new XYZ(center.X, center.Y, center.Z + 1)); // 軸心                                
                                angle = PointRotation(xyzList[0], xyzList[1]); // 角度
                            }
                            double length = Math.Round(Line.CreateBound(xyzList[0], xyzList[1]).Length / 0.0328);
                            double width = Math.Round(Line.CreateBound(xyzList[1], xyzList[2]).Length / 0.0328);
                            cadLinkValue.type = "PolyLine"; // Type
                            cadLinkValue.center = center; // 中心點
                            cadLinkValue.axis = axis; // 軸心
                            cadLinkValue.angle = angle; // 角度                                                        
                            cadLinkValue.name = length * 10 + " x " + width * 10 + "mm"; // 名稱                            
                            cadLinkValue.length = length * 10; // 長
                            cadLinkValue.width = width * 10; // 寬
                        }
                        //else if (geoList.Current is Line)
                        //{
                        //    Line line = geoList.Current as Line;
                        //    IList<XYZ> xyzList = line.Tessellate();
                        //    if (xyzList.Count >= 4)
                        //    {
                        //        center = new XYZ((xyzList[0].X + xyzList[1].X + xyzList[2].X + xyzList[3].X) / 4,
                        //                            (xyzList[0].Y + xyzList[1].Y + xyzList[2].Y + xyzList[3].Y) / 4,
                        //                            (xyzList[0].Z + xyzList[1].Z + xyzList[2].Z + xyzList[3].Z) / 4);
                        //        axis = Line.CreateBound(center, new XYZ(center.X, center.Y, center.Z + 1)); // 軸心                                
                        //        angle = PointRotation(xyzList[0], xyzList[1]); // 角度
                        //    }
                        //    double length = Math.Round(Line.CreateBound(xyzList[0], xyzList[1]).Length / 0.0328);
                        //    double width = Math.Round(Line.CreateBound(xyzList[1], xyzList[2]).Length / 0.0328);
                        //    cadLinkValue.type = "Line"; // Type
                        //    cadLinkValue.center = center; // 中心點
                        //    cadLinkValue.axis = axis; // 軸心
                        //    cadLinkValue.angle = angle; // 角度                                                        
                        //    cadLinkValue.name = length * 10 + " x " + width * 10 + "mm"; // 名稱                            
                        //    cadLinkValue.length = length * 10; // 長
                        //    cadLinkValue.width = width * 10; // 寬
                        //}
                        else if (geoList.Current is Arc)
                        {
                            Arc arc = geoList.Current as Arc;
                            center = arc.Center;
                            axis = Line.CreateBound(center, new XYZ(center.X, center.Y, center.Z + 1));
                            angle = 0.0;
                            radius = arc.Radius;
                            cadLinkValue.type = "Arc"; // 型別                            
                            cadLinkValue.axis = axis; // 軸心                            
                            cadLinkValue.angle = angle; // 角度                            
                            cadLinkValue.center = center; // 中心點                            
                            cadLinkValue.name = Math.Round(radius * 2 / 0.032808 * 10) + "mm"; // 名稱                            
                            cadLinkValue.radius = Math.Round(radius, 5); // 半徑
                        }
                        else if (geoList.Current is Solid)
                        {
                            Solid solid = geoList.Current as Solid;
                            if (solid.Faces.Size > 0 && solid.Volume > 0)
                            {
                                cadLinkValue.type = "Solid";
                            }
                        }
                        else if (geoList.Current is Ellipse)
                        {
                            Ellipse ellipse = geoList.Current as Ellipse;
                            cadLinkValue.type = "Ellipse";
                        }
                        else if (geoList.Current is NurbSpline)
                        {
                            NurbSpline nurbSpline = geoList.Current as NurbSpline;
                            cadLinkValue.type = "NurbSpline";
                        }
                        // 如果下一層依然為GeometryInstance, 則return讀取線條資訊 <-- 還沒寫好..
                        else if (geoList.Current is GeometryInstance)
                        {
                            geoIns = geoList.Current as GeometryInstance;
                            if (geoIns != null)
                            {
                                GeometryElement geoElem = geoIns.GetSymbolGeometry();
                                geoList = geoElem.GetEnumerator();
                                geoList.MoveNext();
                                if (geoList is GeometryInstance)
                                {

                                }
                            }
                        }
                        if(cadLinkValue.type != null)
                        {
                            cadLinkValueList.Add(cadLinkValue);
                        }
                    }
                }
            }

            return cadLinkValueList;
        }
        // 旋轉角度
        public static double PointRotation(XYZ pointA, XYZ pointB)
        {
            XYZ pA = new XYZ(pointA.X, pointA.Y, 0);
            XYZ pB = new XYZ(pointB.X, pointB.Y, 0);
            double Dx = pB.X - pA.X;
            double Dy = pB.Y - pA.Y;
            double DRoation = Math.Atan2(Dy, Dx);
            double WRotation = DRoation / Math.PI * 180;

            return WRotation;
        }
    }
}
