using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig;

namespace DocumentProcessing
{
    public class PdfReader
    {        
        private const string Pattern1 = @"\d{3}\.\d{2}\.\d{2}";
        private const string Pattern2 = @"^\d{1,3}$";
                
        private static readonly Regex Regex1 = new Regex(Pattern1);
        private static readonly Regex Regex2 = new Regex(Pattern2);

        
        /// <summary>
        /// Extracts specific text from a PDF file located at the given file path.
        /// </summary>
        /// <returns>
        /// 回傳簽名日期列舉 [設計, 初核, 複核, 技師, 計畫, 工程局年, 工程局月, 工程局日]
        /// </returns>
        public List<string> ExtractDateText(string filepath)
        {
            var ret = new List<string>();
            
            try
            {
                using (var pdf = PdfDocument.Open(filepath))
                {
                    foreach (var page in pdf.GetPages())
                    {
                        var words = GetMatchedWords(page, Regex1);
                        var dates = ExtractSinoSignDate(words);

                        words = GetMatchedWords(page, Regex2);
                        dates.AddRange(ExtractBureauDate(words));

                        ret = dates;
                    }                    
                }
            }
            catch
            {

            }

            return ret;
        }

        private List<Word> GetMatchedWords(Page page, Regex regex)
        {
            return page.GetWords()
                       .Where(word => regex.IsMatch(word.Text))
                       .ToList();
        }

        private List<string> ExtractSinoSignDate(List<Word> words)
        {
            List<string> dates = new List<string>();

            dates = words
                   .Where(x => x.BoundingBox.Bottom < 400)
                   .OrderBy(x => x.BoundingBox.Left)
                   .ThenByDescending(x => x.BoundingBox.Bottom)
                   .Select(x => x.Text)
                   .ToList();

            if (dates.Count < 5)
            {
                dates = new List<string> {
                    words.Find(x => x.BoundingBox.Left < 2250 && x.BoundingBox.Bottom > 315)?.Text ?? "",
                    words.Find(x => x.BoundingBox.Left < 2250 && x.BoundingBox.Bottom < 315)?.Text ?? "",
                    words.Find(x => x.BoundingBox.Left > 2250 && x.BoundingBox.Bottom > 350)?.Text ?? "",
                    words.Find(x => x.BoundingBox.Left > 2250 && x.BoundingBox.Bottom < 350 && x.BoundingBox.Bottom > 330)?.Text ?? "",
                    words.Find(x => x.BoundingBox.Left > 2250 && x.BoundingBox.Bottom < 330)?.Text ?? ""
                };
            }

            return dates;

        }

        private List<string> ExtractBureauDate(List<Word> words)
        {
            List<string> dates = new List<string>
            {
                words.Find(x => x.BoundingBox.Left > 2130 && x.BoundingBox.Left < 2140 && x.BoundingBox.Bottom < 540 && x.BoundingBox.Bottom > 530)?.Text ?? "",
                words.Find(x => x.BoundingBox.Left > 2172 && x.BoundingBox.Left < 2182 && x.BoundingBox.Bottom < 540 && x.BoundingBox.Bottom > 530)?.Text ?? "",
                words.Find(x => x.BoundingBox.Left > 2214 && x.BoundingBox.Left < 2224 && x.BoundingBox.Bottom < 540 && x.BoundingBox.Bottom > 530)?.Text ?? ""
            };

            return dates;
        }
    }
}
