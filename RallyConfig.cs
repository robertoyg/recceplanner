using System;

namespace ReccePlanner
{
    internal class RallyConfig
    {
        public double StageRecceSpeedPassOneMph { get; set; } = 30;
        public double StageRecceSpeedPassTwoMph { get; set; } = 30;
        public TimeSpan? StartTimeFirstStage { get; set; }
    }
}
