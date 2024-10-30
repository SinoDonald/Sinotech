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
            return new ElementId(Convert.ToInt32(id)); // 2020
        }
        /// <summary>
        /// ElementId轉換為數字
        /// </summary>
        /// <param name="elemId"></param>
        /// <returns></returns>
        public static int GetValue(ElementId elemId)
        {
            return elemId.IntegerValue; // 2020
        }
        /// <summary>
        /// 轉換單位
        /// </summary>
        /// <param name="number"></param>
        /// <param name="unit"></param>
        /// <returns></returns>        
        public static double ConvertFromInternalUnits(double number, string unit)
        {
            if (unit.Equals("meters")) { number = UnitUtils.ConvertFromInternalUnits(number, DisplayUnitType.DUT_METERS); } // 2020
            else if (unit.Equals("millimeters")) { number = UnitUtils.ConvertFromInternalUnits(number, DisplayUnitType.DUT_MILLIMETERS); } // 2020
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
            if (unit.Equals("meters")) { number = UnitUtils.ConvertToInternalUnits(number, DisplayUnitType.DUT_METERS); } // 2020
            else if (unit.Equals("millimeters")) { number = UnitUtils.ConvertToInternalUnits(number, DisplayUnitType.DUT_MILLIMETERS); } // 2020
            return number;
        }
    }
}
