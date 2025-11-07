using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace Sinotech_2020.CreateModel
{
    public partial class AutoPipeForm : System.Windows.Forms.Form
    {
        Document formDoc = null;
        private static ICollection<Element> pipeSystemTypes = null;
        private static ICollection<Element> pipeTypes = null;
        private static ICollection<Element> levels = null;
        private static List<Line> lineList = new List<Line>();
        private ElementId pipeSystemTypeId = null;
        private ElementId pipeTypeId = null;
        private ElementId levelId = null;
        public AutoPipeForm(Document doc)
        {
            InitializeComponent();
            formDoc = doc;
            pipeSystemTypes = AutoPipe.pipeSystemTypes;
            pipeTypes = AutoPipe.pipeTypes;
            levels = AutoPipe.levels;
            lineList = AutoPipe.lineList;

            foreach (Element pipeSystemType in pipeSystemTypes)
            {
                pipeSystemTypeCB.Items.Add(pipeSystemType.Name);
            }
            pipeSystemTypeCB.Text = pipeSystemTypeCB.Items[0].ToString(); // 預設第一種系統類型

            foreach (Element pipeType in pipeTypes)
            {
                pipeTypeCB.Items.Add(pipeType.Name);
            }
            pipeTypeCB.Text = pipeTypeCB.Items[0].ToString(); // 預設第一種管類型

            foreach (Element level in levels)
            {
                levelCB.Items.Add(level.Name);
            }
            levelCB.Text = levelCB.Items[0].ToString(); // 預設第一種管類型

            CenterToScreen(); // 畫面置中
        }
        // 確定
        private void sureBtn_Click(object sender, EventArgs e)
        {
            pipeSystemTypeId = (from x in pipeSystemTypes
                                          where x.Name.Equals(pipeSystemTypeCB.Text)
                                          select x).FirstOrDefault().Id;
            pipeTypeId = (from x in pipeTypes
                                    where x.Name.Equals(pipeTypeCB.Text)
                                    select x).FirstOrDefault().Id;
            levelId = (from x in levels
                                 where x.Name.Equals(levelCB.Text)
                                 select x).FirstOrDefault().Id;

            using (Transaction trans = new Transaction(formDoc, "Create Pipe"))
            {
                trans.Start();
                foreach (Line line in lineList)
                {
                    try
                    {
                        Pipe pipe = Pipe.Create(formDoc, pipeSystemTypeId, pipeTypeId, levelId, line.Tessellate()[0], line.Tessellate()[1]);
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                    {

                    }
                    catch (Exception)
                    {

                    }
                }
                trans.Commit();
            }
            Close();
        }
        // 取消
        private void cancelBtn_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
