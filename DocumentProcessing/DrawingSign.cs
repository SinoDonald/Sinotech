using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocumentProcessing
{
    public class DrawingSign
    {
        public string FileName { get; set; }
        public List<string> SignDates { get; set; } = new List<string>();
        public List<string> SignNames { get; set; } = new List<string>();
    }
}
