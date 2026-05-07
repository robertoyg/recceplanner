using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReccePlanner
{
    public class Location
    {
        public string Code { get; set; }

        public string Name { get; set; }

        public double DistanceMiles { get; set; }

        public TimeSpan? OpenTime { get; set; }

        public TimeSpan? CloseTime { get; set; }

        public Location(string name, string code)
        {
            Name = name;
            Code = code;
        }
    }
}
