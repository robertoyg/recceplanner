using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReccePlanner
{
    internal class Route
    {
        public Location Source { get; set; }

        public Location Target { get; set; }


        public int Time { get; set; }

        public Route(Location source, Location target, int time)
        {
            Source = source;
            Target = target;
            Time = time;
        }

    }
}
