using Autodesk.Revit.DB;
using System;

namespace Revit_API
{
    public class RevitAPI
    {
        /// <summary>
        /// ElementId位元轉換
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static ElementId NewElementId(string id)
        {
            return new ElementId(Convert.ToInt64(id)); // 2024
        }
        /// <summary>
        /// ElementId轉換為數字
        /// </summary>
        /// <param name="elemId"></param>
        /// <returns></returns>
        public static int GetValue(ElementId elemId)
        {
            return ((int)elemId.Value); // 2024
        }
        /// <summary>
        /// 轉換單位
        /// </summary>
        /// <param name="number"></param>
        /// <param name="unit"></param>
        /// <returns></returns>        
        public static double ConvertFromInternalUnits(double number, string unit)
        {
            if (unit.Equals("meters")) { number = UnitUtils.ConvertFromInternalUnits(number, UnitTypeId.Meters); } // 2022
            else if (unit.Equals("millimeters")) { number = UnitUtils.ConvertFromInternalUnits(number, UnitTypeId.Millimeters); } // 2022
            return number;
        }
        /// <summary>
        /// 轉換單位
        /// </summary>
        /// <param name="number"></param>
        /// <param name="unit"></param>
        /// <returns></returns>
        public static double ConvertToInternalUnits(double number, string unit)
        {
            if (unit.Equals("meters")) { number = UnitUtils.ConvertToInternalUnits(number, UnitTypeId.Meters); } // 2022
            else if (unit.Equals("millimeters")) { number = UnitUtils.ConvertToInternalUnits(number, UnitTypeId.Millimeters); } // 2022
            return number;
        }
    }
}
