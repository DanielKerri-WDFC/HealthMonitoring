using System.Collections.Generic;

namespace HealthMonitoring.Monitors.Core.Exchange.Client.Entities
{
    internal class MonitorSettingsModel
    {
        public Dictionary<string, int> Throttling { get; set; }
        public MonitorSettings Monitor { get; set; }
    }
}