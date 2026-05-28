using System.Collections.Generic;
using Newtonsoft.Json;

namespace OzStripsEuroScope.VatSysShim
{
    public sealed class SnapshotMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("pluginVersion")]
        public string PluginVersion { get; set; } = string.Empty;

        [JsonProperty("connectionType")]
        public int ConnectionType { get; set; }

        [JsonProperty("controller")]
        public ControllerSnapshot Controller { get; set; } = new ControllerSnapshot();

        [JsonProperty("flights")]
        public List<FlightPlanSnapshot> Flights { get; set; } = new List<FlightPlanSnapshot>();
    }

    public sealed class FlightChangedMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonProperty("flight")]
        public FlightPlanSnapshot Flight { get; set; } = new FlightPlanSnapshot();
    }

    public sealed class ControllerSnapshot
    {
        [JsonProperty("callsign")]
        public string Callsign { get; set; } = string.Empty;

        [JsonProperty("isController")]
        public bool IsController { get; set; }
    }

    public sealed class FlightPlanSnapshot
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

        [JsonProperty("star")]
        public string Star { get; set; } = string.Empty;

        [JsonProperty("runway")]
        public string Runway { get; set; } = string.Empty;

        [JsonProperty("arrivalRunway")]
        public string ArrivalRunway { get; set; } = string.Empty;

        [JsonProperty("activeDepartureRunway")]
        public string ActiveDepartureRunway { get; set; } = string.Empty;

        [JsonProperty("etd")]
        public string Etd { get; set; } = string.Empty;

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

        [JsonProperty("routePoints")]
        public List<RoutePointSnapshot> RoutePoints { get; set; } = new List<RoutePointSnapshot>();

        [JsonProperty("sidOptions")]
        public List<SidOptionSnapshot> SidOptions { get; set; } = new List<SidOptionSnapshot>();

        [JsonProperty("selected")]
        public bool Selected { get; set; }
    }

    public sealed class SidOptionSnapshot
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("runway")]
        public string Runway { get; set; } = string.Empty;
    }

    public sealed class RoutePointSnapshot
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("latitude")]
        public double Latitude { get; set; }

        [JsonProperty("longitude")]
        public double Longitude { get; set; }
    }
}
