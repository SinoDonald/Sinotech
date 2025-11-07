using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sinotech_2020.CSDSEM
{
    public class LevelElevation
    {
        public Level level { get; set; }
        public string name { get; set; }
        public double elevation { get; set; }
        public double height { get; set; }
    }
    class FindLevel
    {
        public static double meter_conversion = 0.3048; // 公尺單位轉換

        // 找到當前視圖的Level相關資訊
        public Tuple<List<LevelElevation>, LevelElevation, double> FindDocViewLevel(Document doc)
        {
            // 查詢所有Level的高程並排序
            List<Level> levels = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Levels).WhereElementIsNotElementType().Cast<Level>().ToList();
            List<LevelElevation> levelElevList = new List<LevelElevation>();
            foreach (Level level in levels)
            {
                LevelElevation levelElevation = new LevelElevation();
                levelElevation.name = level.Name;
                levelElevation.level = level;
                levelElevation.height = level.get_Parameter(BuiltInParameter.LEVEL_ELEV).AsDouble();
                //levelElevation.elevation = Convert.ToDouble(level.get_Parameter(BuiltInParameter.LEVEL_ELEV).AsValueString());
                double elevation = level.get_Parameter(BuiltInParameter.LEVEL_ELEV).AsDouble();
                levelElevation.elevation = elevation * meter_conversion;
                levelElevList.Add(levelElevation);
            }
            levelElevList = levelElevList.OrderBy(x => x.elevation).ToList();
            double startElev = 0.0;
            double endElev = 0.0;
            double floorHeight = 10;
            // 找到當前樓層
            LevelElevation viewLevel = new LevelElevation();
            try { viewLevel = levelElevList.Where(x => x.level.Id.Equals(doc.ActiveView.GenLevel.Id)).FirstOrDefault(); }
            catch (NullReferenceException) { viewLevel = levelElevList[0]; }
            catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
            int leCount = levelElevList.IndexOf(viewLevel);
            // 查詢當前樓層與上一樓層的高度, 製作火源高度
            if (levelElevList.Count >= 2)
            {
                if (leCount < levelElevList.Count)
                {
                    startElev = levelElevList[leCount].elevation;
                    endElev = levelElevList[leCount + 1].elevation;
                    floorHeight = endElev - startElev;
                }
                else
                {
                    startElev = levelElevList[leCount].elevation;
                    endElev = levelElevList[leCount - 1].elevation;
                    floorHeight = startElev - endElev;
                }
            }

            Tuple<List<LevelElevation>, LevelElevation, double> multiValue = Tuple.Create(levelElevList, viewLevel, floorHeight);

            return multiValue;
        }
    }
}
