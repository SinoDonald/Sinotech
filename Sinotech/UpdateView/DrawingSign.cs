using System.Collections.Generic;

namespace Sinotech.UpdateView
{
    public class DrawingSign
    {
        public string FileName { get; set; }
        public List<string> SignDates { get; set; } = new List<string>();
        public List<string> SignNames { get; set; } = new List<string>();
    }
}
