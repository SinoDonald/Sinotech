using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace DocumentProcessing
{
    public class ExcelReader
    {
        public string FilePath { get; set; }

        static ExcelReader()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        public ExcelReader()
        {
            FilePath = GetFilePath();
        }

        public List<DrawingSign> GetDrawingSigns()
        {
            var ret = new List<DrawingSign>();
            PdfReader pdfReader = new PdfReader();

            using (var package = new ExcelPackage(FilePath))
            {
                var sheets = package.Workbook.Worksheets;
                var sheet = sheets[0];

                for (int i = 2; i <= sheet.Dimension.End.Row; i++)
                {
                    DrawingSign drawSign = new DrawingSign();

                    drawSign.FileName = sheet.Cells[i, 4].Text.Substring(0, sheet.Cells[i, 4].Text.Length - 4);

                    drawSign.SignNames.Add(sheet.Cells[i, 8].Text);
                    drawSign.SignNames.Add(sheet.Cells[i, 9].Text);
                    drawSign.SignNames.Add(sheet.Cells[i, 10].Text);

                    if (!string.IsNullOrEmpty(sheet.Cells[i, 11].Text))
                    {
                        drawSign.SignNames.Add(sheet.Cells[i, 11].Text);
                    }
                    else if (!string.IsNullOrEmpty(sheet.Cells[i, 12].Text))
                    {
                        drawSign.SignNames.Add(sheet.Cells[i, 12].Text);
                    }
                    else if (!string.IsNullOrEmpty(sheet.Cells[i, 13].Text))
                    {
                        drawSign.SignNames.Add(sheet.Cells[i, 13].Text);
                    }
                    else
                    {
                        drawSign.SignNames.Add("");
                    }

                    drawSign.SignNames.Add(sheet.Cells[i, 14].Text);

                    for (int j = 0; j != drawSign.SignNames.Count; j++)
                    {
                        if (!string.IsNullOrEmpty(drawSign.SignNames[j]))
                        {
                            drawSign.SignNames[j] = drawSign.SignNames[j].Substring(drawSign.SignNames[j].IndexOf('/') + 1);
                        }
                    }

                    ret.Add(drawSign);
                }

            }

            return ret;
        }

        private string GetFilePath()
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Title = "Select an Excel File";
                openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                openFileDialog.Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*";
                openFileDialog.FilterIndex = 1;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    return openFileDialog.FileName;
                }

                return null;
            }
        }
    }
}
