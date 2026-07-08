using System.Collections.Generic;
using Newtonsoft.Json;

namespace MaxRumsey.OzStripsPlugin.GUI;

internal sealed class StandAllocatorArrivalSummary
{
    [JsonProperty("callsign")]
    public string Callsign { get; set; } = string.Empty;

    [JsonProperty("airport")]
    public string Airport { get; set; } = string.Empty;

    [JsonProperty("departure")]
    public string Departure { get; set; } = string.Empty;

    [JsonProperty("arrival")]
    public string Arrival { get; set; } = string.Empty;

    [JsonProperty("assigned_stand")]
    public string AssignedStand { get; set; } = string.Empty;

    [JsonProperty("physical_stand")]
    public string PhysicalStand { get; set; } = string.Empty;

    [JsonProperty("current_stand")]
    public string CurrentStand { get; set; } = string.Empty;
}

internal sealed class StandAllocatorStandOption
{
    [JsonProperty("stand_id")]
    public string StandId { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("terminal")]
    public string Terminal { get; set; } = string.Empty;

    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("display_text")]
    public string DisplayText { get; set; } = string.Empty;

    public string Label => string.IsNullOrWhiteSpace(DisplayText) ? (string.IsNullOrWhiteSpace(Name) ? StandId : Name) : DisplayText;
}

internal sealed class StandAllocatorStandOptionsResponse
{
    [JsonProperty("callsign")]
    public string Callsign { get; set; } = string.Empty;

    [JsonProperty("current_stand")]
    public string CurrentStand { get; set; } = string.Empty;

    [JsonProperty("preferred_stands")]
    public List<StandAllocatorStandOption> PreferredStands { get; set; } = [];

    [JsonProperty("all_stands")]
    public List<StandAllocatorStandOption> AllStands { get; set; } = [];
}

internal sealed class StandAllocatorAirportListResponse
{
    [JsonProperty("airports")]
    public List<string> Airports { get; set; } = [];
}

internal sealed class StandAllocatorAssignmentResponse
{
    [JsonProperty("stand_id")]
    public string StandId { get; set; } = string.Empty;

    [JsonProperty("airport")]
    public string Airport { get; set; } = string.Empty;
}

internal sealed class StandAllocatorReassignRequest
{
    [JsonProperty("callsign")]
    public string Callsign { get; set; } = string.Empty;

    [JsonProperty("stand_id")]
    public string StandId { get; set; } = string.Empty;

    [JsonProperty("allow_reallocate")]
    public bool AllowReallocate { get; set; }

    [JsonProperty("airport")]
    public string Airport { get; set; } = string.Empty;
}

internal sealed class StandAllocatorReassignResponse
{
    [JsonProperty("status")]
    public string Status { get; set; } = string.Empty;

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;

    [JsonProperty("assignment")]
    public StandAllocatorAssignmentResponse? Assignment { get; set; }
}
