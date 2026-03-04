using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReccePlanner
{
    internal class Program
    {
        static void Main(string[] args)
        {
            /*Rally rally100AW = new Rally();

            // Load all the stages first - for now focus on solving just stages
            Location ss1 = new Location("1 / 4 - Floyd Tower West Short","1");
            Location ss2 = new Location("2 / 5 - Brazil to Colen to Moses","2");
            Location ss3 = new Location("3 / 6 - Pigeon Roost East Short","3");
            Location ss7 = new Location("7 / 13 - Juniors Crooked Truck","7");
            Location ss8 = new Location("8 / 11 - Nova Scotia South","8");
            Location ss9 = new Location("9 / 12 - Little Southern Hollow","9");
            Location ss10 = new Location("10 / 14 - Deep Ford","10");

            //r.Locations.Add(ss1);
            //r.Locations.Add(ss2);
            //r.Locations.Add(ss3);
            rally100AW.Locations.Add(ss7);
            rally100AW.Locations.Add(ss8);
            rally100AW.Locations.Add(ss9);
            rally100AW.Locations.Add(ss10);

            // Now all the routes
            rally100AW.TravelTimes.Add(new Route(ss1, ss1, 25));
            rally100AW.TravelTimes.Add(new Route(ss1, ss2, 30));
            rally100AW.TravelTimes.Add(new Route(ss1, ss3, 14));
            rally100AW.TravelTimes.Add(new Route(ss1, ss7, 90));
            rally100AW.TravelTimes.Add(new Route(ss1, ss8, 80));
            rally100AW.TravelTimes.Add(new Route(ss1, ss9, 90));
            rally100AW.TravelTimes.Add(new Route(ss1, ss10, 70));
            rally100AW.TravelTimes.Add(new Route(ss2, ss1, 43));
            rally100AW.TravelTimes.Add(new Route(ss2, ss2, 20));
            rally100AW.TravelTimes.Add(new Route(ss2, ss3, 40));
            rally100AW.TravelTimes.Add(new Route(ss2, ss7, 60));
            rally100AW.TravelTimes.Add(new Route(ss2, ss8, 50));
            rally100AW.TravelTimes.Add(new Route(ss2, ss9, 65));
            rally100AW.TravelTimes.Add(new Route(ss2, ss10, 60));
            rally100AW.TravelTimes.Add(new Route(ss3, ss1, 19));
            rally100AW.TravelTimes.Add(new Route(ss3, ss2, 39));
            rally100AW.TravelTimes.Add(new Route(ss3, ss3, 25));
            rally100AW.TravelTimes.Add(new Route(ss3, ss7, 80));
            rally100AW.TravelTimes.Add(new Route(ss3, ss8, 65));
            rally100AW.TravelTimes.Add(new Route(ss3, ss9, 80));
            rally100AW.TravelTimes.Add(new Route(ss3, ss10, 80));
            rally100AW.TravelTimes.Add(new Route(ss7, ss1, 85));
            rally100AW.TravelTimes.Add(new Route(ss7, ss2, 55));
            rally100AW.TravelTimes.Add(new Route(ss7, ss3, 75));
            rally100AW.TravelTimes.Add(new Route(ss7, ss7, 15));
            rally100AW.TravelTimes.Add(new Route(ss7, ss8, 25));
            rally100AW.TravelTimes.Add(new Route(ss7, ss9, 35));
            rally100AW.TravelTimes.Add(new Route(ss7, ss10, 35));
            rally100AW.TravelTimes.Add(new Route(ss8, ss1, 90));
            rally100AW.TravelTimes.Add(new Route(ss8, ss2, 60));
            rally100AW.TravelTimes.Add(new Route(ss8, ss3, 80));
            rally100AW.TravelTimes.Add(new Route(ss8, ss7, 40));
            rally100AW.TravelTimes.Add(new Route(ss8, ss8, 10));
            rally100AW.TravelTimes.Add(new Route(ss8, ss9, 10));
            rally100AW.TravelTimes.Add(new Route(ss8, ss10, 45));
            rally100AW.TravelTimes.Add(new Route(ss9, ss1, 100));
            rally100AW.TravelTimes.Add(new Route(ss9, ss2, 75));
            rally100AW.TravelTimes.Add(new Route(ss9, ss3, 90));
            rally100AW.TravelTimes.Add(new Route(ss9, ss7, 45));
            rally100AW.TravelTimes.Add(new Route(ss9, ss8, 30));
            rally100AW.TravelTimes.Add(new Route(ss9, ss9, 40));
            rally100AW.TravelTimes.Add(new Route(ss9, ss10, 30));
            rally100AW.TravelTimes.Add(new Route(ss10, ss1, 90));
            rally100AW.TravelTimes.Add(new Route(ss10, ss2, 70));
            rally100AW.TravelTimes.Add(new Route(ss10, ss3, 80));
            rally100AW.TravelTimes.Add(new Route(ss10, ss7, 45));
            rally100AW.TravelTimes.Add(new Route(ss10, ss8, 35));
            rally100AW.TravelTimes.Add(new Route(ss10, ss9, 50));
            rally100AW.TravelTimes.Add(new Route(ss10, ss10, 15));

            rally100AW.FindOptimalRecce();*/

            Rally olympus = new Rally();

            // Load all the stages first 
            Location ss1 = new Location("1 / 4 - Kuhnle Short", "1");
            Location ss2 = new Location("2 / 5 - Schafer Long", "2");
            Location ss3 = new Location("3 / 6 - Deckerville 43", "3");
            Location ss7 = new Location("7 / 8 - Nahwatzel", "7");
            Location ss9 = new Location("9 / 13 - Not So Stillwater & 10 / 14 - Plug Mill", "9");
            Location ss11 = new Location("11 / 15 - Wildcat Short", "11");
            Location ss12 = new Location("12 / 16 - PowerStage", "12");

            olympus.Locations.Add(ss1);
            olympus.Locations.Add(ss2);
            olympus.Locations.Add(ss3);
            olympus.Locations.Add(ss7);
            olympus.Locations.Add(ss9);
            olympus.Locations.Add(ss11);
            //olympus.Locations.Add(ss12);

            // Now all the routes
            olympus.TravelTimes.Add(new Route(ss1, ss1, 10));
            olympus.TravelTimes.Add(new Route(ss1, ss2, 5));
            olympus.TravelTimes.Add(new Route(ss1, ss3, 10));
            olympus.TravelTimes.Add(new Route(ss1, ss7, 20));
            olympus.TravelTimes.Add(new Route(ss1, ss9, 40));
            olympus.TravelTimes.Add(new Route(ss1, ss11, 45));
            olympus.TravelTimes.Add(new Route(ss1, ss12, 45));
            olympus.TravelTimes.Add(new Route(ss2, ss1, 5));
            olympus.TravelTimes.Add(new Route(ss2, ss2, 5));
            olympus.TravelTimes.Add(new Route(ss2, ss3, 5));
            olympus.TravelTimes.Add(new Route(ss2, ss7, 15));
            olympus.TravelTimes.Add(new Route(ss2, ss9, 35));
            olympus.TravelTimes.Add(new Route(ss2, ss11, 40));
            olympus.TravelTimes.Add(new Route(ss2, ss12, 40));
            olympus.TravelTimes.Add(new Route(ss3, ss1, 20));
            olympus.TravelTimes.Add(new Route(ss3, ss2, 20));
            olympus.TravelTimes.Add(new Route(ss3, ss3, 15));
            olympus.TravelTimes.Add(new Route(ss3, ss7, 15));
            olympus.TravelTimes.Add(new Route(ss3, ss9, 30));
            olympus.TravelTimes.Add(new Route(ss3, ss11, 30));
            olympus.TravelTimes.Add(new Route(ss3, ss12, 35));
            olympus.TravelTimes.Add(new Route(ss7, ss1, 40));
            olympus.TravelTimes.Add(new Route(ss7, ss2, 40));
            olympus.TravelTimes.Add(new Route(ss7, ss3, 40));
            olympus.TravelTimes.Add(new Route(ss7, ss7, 30));
            olympus.TravelTimes.Add(new Route(ss7, ss9, 30));
            olympus.TravelTimes.Add(new Route(ss7, ss11, 40));
            olympus.TravelTimes.Add(new Route(ss7, ss12, 5));
            olympus.TravelTimes.Add(new Route(ss9, ss1, 35));
            olympus.TravelTimes.Add(new Route(ss9, ss2, 35));
            olympus.TravelTimes.Add(new Route(ss9, ss3, 30));
            olympus.TravelTimes.Add(new Route(ss9, ss7, 35));
            olympus.TravelTimes.Add(new Route(ss9, ss9, 30));
            olympus.TravelTimes.Add(new Route(ss9, ss11, 30));
            olympus.TravelTimes.Add(new Route(ss9, ss12, 50));
            olympus.TravelTimes.Add(new Route(ss11, ss1, 45));
            olympus.TravelTimes.Add(new Route(ss11, ss2, 45));
            olympus.TravelTimes.Add(new Route(ss11, ss3, 45));
            olympus.TravelTimes.Add(new Route(ss11, ss7, 30));
            olympus.TravelTimes.Add(new Route(ss11, ss9, 15));
            olympus.TravelTimes.Add(new Route(ss11, ss11, 20));
            olympus.TravelTimes.Add(new Route(ss11, ss12, 35));
            olympus.TravelTimes.Add(new Route(ss12, ss1, 30));
            olympus.TravelTimes.Add(new Route(ss12, ss2, 30));
            olympus.TravelTimes.Add(new Route(ss12, ss3, 25));
            olympus.TravelTimes.Add(new Route(ss12, ss7, 15));
            olympus.TravelTimes.Add(new Route(ss12, ss9, 20));
            olympus.TravelTimes.Add(new Route(ss12, ss11, 30));
            olympus.TravelTimes.Add(new Route(ss12, ss12, 25));

            olympus.FindOptimalRecce();

        }
    }
}
