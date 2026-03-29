using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReccePlanner
{
    internal class Location
    {
        public string Code { get; set; }

        public string Name { get; set; }

        public double DistanceMiles { get; set; }

        public Location(string name, string code)
        {
            Name = name;
            Code = code;
        }
    }
}
