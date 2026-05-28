using System.Collections.Generic;
using Newtonsoft.Json;

namespace OzStripsEuroScope.Helper
{
    internal sealed class SnapshotMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("pluginVersion")]
        public string PluginVersion { get; set; } = string.Empty;

        [JsonProperty("controller")]
        public ControllerSnapshot Controller { get; set; } = new ControllerSnapshot();

        [JsonProperty("flights")]
        public List<FlightPlanSnapshot> Flights { get; set; } = new List<FlightPlanSnapshot>();
    }

    internal sealed class FlightChangedMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonProperty("flight")]
        public FlightPlanSnapshot Flight { get; set; } = new FlightPlanSnapshot();
    }

    internal sealed class ControllerSnapshot
    {
        [JsonProperty("callsign")]
        public string Callsign { get; set; } = string.Empty;

        [JsonProperty("isController")]
        public bool IsController { get; set; }
    }

    internal sealed class FlightPlanSnapshot
    {
        [JsonProperty("callsign")]
        public string Callsign { get; set; } = string.Empty;

        [JsonProperty("aircraftType")]
        public string AircraftType { get; set; } = string.Empty;

        [JsonProperty("wakeCategory")]
        public string WakeCategory { get; set; } = string.Empty;

        [JsonProperty("flightRules")]
        public string FlightRules { get; set; } = string.Empty;

        [JsonProperty("adep")]
        public string Adep { get; set; } = string.Empty;

        [JsonProperty("ades")]
        public string Ades { get; set; } = string.Empty;

        [JsonProperty("route")]
        public string Route { get; set; } = string.Empty;

        [JsonProperty("sid")]
        public string Sid { get; set; } = string.Empty;

        [JsonProperty("runway")]
        public string Runway { get; set; } = string.Empty;

        [JsonProperty("firstWaypoint")]
        public string FirstWaypoint { get; set; } = string.Empty;

        [JsonProperty("squawk")]
        public string Squawk { get; set; } = string.Empty;

        [JsonProperty("scratchpad")]
        public string Scratchpad { get; set; } = string.Empty;

        [JsonProperty("groundState")]
        public string GroundState { get; set; } = string.Empty;

        [JsonProperty("rflFeet")]
        public int RflFeet { get; set; }

        [JsonProperty("cflFeet")]
        public int CflFeet { get; set; }

        [JsonProperty("assignedHeading")]
        public int AssignedHeading { get; set; }

        [JsonProperty("selected")]
        public bool Selected { get; set; }

        public string Rfl => FormatLevel(RflFeet);

        public string Cfl => FormatLevel(CflFeet);

        public string TypeAndWake => string.IsNullOrWhiteSpace(WakeCategory) ? AircraftType : AircraftType + "/" + WakeCategory;

        private static string FormatLevel(int feet)
        {
            return feet <= 0 ? string.Empty : (feet / 100).ToString("000");
        }
    }
}
