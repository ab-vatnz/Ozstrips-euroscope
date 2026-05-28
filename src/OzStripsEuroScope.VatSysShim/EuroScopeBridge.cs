using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using vatsys;

namespace OzStripsEuroScope.VatSysShim
{
    public static class EuroScopeBridge
    {
        private const int EuroScopeConnectionTypeDirect = 1;
        private const int EuroScopeConnectionTypeViaProxy = 2;
        private const int EuroScopeConnectionTypeSimulatorServer = 3;
        private const int EuroScopeConnectionTypePlayback = 4;
        private const int EuroScopeConnectionTypeSimulatorClient = 5;
        private const int EuroScopeConnectionTypeSweatbox = 6;
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, LocalFlightOverride> LocalOverrides = new Dictionary<string, LocalFlightOverride>(StringComparer.OrdinalIgnoreCase);
        private static IpcClient? client;

        public static event EventHandler<FDP2.FDR>? FdrUpdated;

        public static event EventHandler<string>? StatusChanged;

        public static event EventHandler<string>? AerodromeSuggested;

        public static event EventHandler<IReadOnlyList<string>>? AerodromesSeenChanged;

        public static event EventHandler<string>? ServerSuggested;

        public static event EventHandler? FocusRequested;

        public static event EventHandler? ShutdownRequested;

        public static bool IsStarted { get; private set; }

        public static void Start(string pipeName)
        {
            if (IsStarted)
            {
                return;
            }

            IsStarted = true;
            Network.RaiseConnected();

            client = new IpcClient(pipeName);
            client.LineReceived += (_, line) => HandleLine(line);
            client.StatusChanged += (_, status) =>
            {
                StatusChanged?.Invoke(null, status);
                if (status.StartsWith("Connected", StringComparison.OrdinalIgnoreCase))
                {
                    Network.RaiseConnected();
                }
            };
            client.Start();
        }

        public static void Stop()
        {
            client?.Dispose();
            client = null;
            IsStarted = false;
            Network.RaiseDisconnected();
        }

        public static void SendCommand(string command, string callsign, string value)
        {
            SetLocalOverride(command, callsign, value);
            client?.SendCommand(command, callsign, value);
        }

        private static void HandleLine(string line)
        {
            try
            {
                var type = JObject.Parse(line).Value<string>("type");
                switch (type)
                {
                    case "snapshot":
                        ApplySnapshot(JsonConvert.DeserializeObject<SnapshotMessage>(line));
                        break;
                    case "flightChanged":
                        ApplyFlightChanged(JsonConvert.DeserializeObject<FlightChangedMessage>(line));
                        break;
                    case "focus":
                        MMI.InvokeOnGUI(() => FocusRequested?.Invoke(null, EventArgs.Empty));
                        break;
                    case "shutdown":
                        MMI.InvokeOnGUI(() =>
                        {
                            ShutdownRequested?.Invoke(null, EventArgs.Empty);
                            Application.Exit();
                        });
                        break;
                }
            }
            catch (Exception ex)
            {
                Errors.Add(ex, "OzStrips EuroScope Bridge");
            }
        }

        private static void ApplySnapshot(SnapshotMessage? snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            MMI.InvokeOnGUI(() =>
            {
                lock (SyncRoot)
                {
                    Network.SetController(snapshot.Controller.Callsign);
                    var suggestedServer = InferServer(snapshot.ConnectionType);
                    Network.IsOfficialServer = string.IsNullOrWhiteSpace(suggestedServer);
                    if (!string.IsNullOrWhiteSpace(suggestedServer))
                    {
                        ServerSuggested?.Invoke(null, suggestedServer);
                    }

                    MMI.PrimePosition = Network.Me;
                    MMI.SectorsControlled = BuildSectorList(snapshot);
                    MMI.RaisePositionChanged();

                    var aerodromes = ExtractAerodromes(snapshot);
                    if (aerodromes.Count > 0)
                    {
                        AerodromesSeenChanged?.Invoke(null, aerodromes);
                    }

                    var suggestedAerodrome = InferAerodrome(snapshot, aerodromes);
                    if (!string.IsNullOrWhiteSpace(suggestedAerodrome))
                    {
                        AerodromeSuggested?.Invoke(null, suggestedAerodrome);
                    }

                    var active = snapshot.Flights
                        .Where(x => !string.IsNullOrWhiteSpace(x.Callsign))
                        .Select(x => x.Callsign.Trim().ToUpperInvariant())
                        .ToList();

                    FDP2.RemoveMissing(active);
                    Network.RemoveMissing(active);
                    RDP.RemoveMissing(active);

                    foreach (var flight in snapshot.Flights.Where(x => !string.IsNullOrWhiteSpace(x.Callsign)))
                    {
                        var fdr = ApplyFlight(flight);
                        if (flight.Selected)
                        {
                            MMI.SetSelected(fdr);
                        }

                        FdrUpdated?.Invoke(null, fdr);
                    }
                }
            });
        }

        private static string InferServer(int connectionType)
        {
            switch (connectionType)
            {
                case EuroScopeConnectionTypeSweatbox:
                    return "SWEATBOX1";
                case EuroScopeConnectionTypeSimulatorServer:
                case EuroScopeConnectionTypePlayback:
                case EuroScopeConnectionTypeSimulatorClient:
                    return "LOCALHOST";
                case EuroScopeConnectionTypeDirect:
                case EuroScopeConnectionTypeViaProxy:
                default:
                    return string.Empty;
            }
        }

        private static void ApplyFlightChanged(FlightChangedMessage? message)
        {
            if (message?.Flight == null || string.IsNullOrWhiteSpace(message.Flight.Callsign))
            {
                return;
            }

            MMI.InvokeOnGUI(() =>
            {
                lock (SyncRoot)
                {
                    var fdr = ApplyFlight(message.Flight);
                    if (message.Flight.Selected)
                    {
                        MMI.SetSelected(fdr);
                    }

                    FdrUpdated?.Invoke(null, fdr);
                }
            });
        }

        private static FDP2.FDR ApplyFlight(FlightPlanSnapshot flight)
        {
            var fdr = FDP2.UpsertFDR(flight);
            ApplyLocalOverrides(fdr);
            Network.UpsertPilot(fdr);
            RDP.UpsertTrack(fdr, flight);
            EnsureRunwaySidHints(fdr);
            return fdr;
        }

        private static void SetLocalOverride(string command, string callsign, string value)
        {
            if (string.IsNullOrWhiteSpace(callsign))
            {
                return;
            }

            var key = callsign.Trim().ToUpperInvariant();
            lock (SyncRoot)
            {
                if (!LocalOverrides.TryGetValue(key, out var localOverride))
                {
                    localOverride = new LocalFlightOverride();
                    LocalOverrides[key] = localOverride;
                }

                switch (command)
                {
                    case "SetSid":
                        localOverride.Sid = value ?? string.Empty;
                        break;
                    case "SetDepartureRunway":
                        localOverride.Runway = value ?? string.Empty;
                        break;
                    case "SetFinalAltitude":
                        if (int.TryParse(value, out var rflFeet))
                        {
                            localOverride.RflFeet = rflFeet;
                        }

                        break;
                    case "SetCfl":
                        if (int.TryParse(value, out var cflFeet))
                        {
                            localOverride.CflFeet = cflFeet;
                        }

                        break;
                    case "SetSquawk":
                        localOverride.Squawk = value ?? string.Empty;
                        break;
                    case "SetScratchpad":
                        localOverride.GlobalOpData = value ?? string.Empty;
                        break;
                    case "OpenRunwayMenu":
                        localOverride.RunwayMenuOpened = true;
                        break;
                }
            }
        }

        private static void ApplyLocalOverrides(FDP2.FDR fdr)
        {
            if (!LocalOverrides.TryGetValue(fdr.Callsign, out var localOverride))
            {
                return;
            }

            if (localOverride.RflFeet.HasValue)
            {
                fdr.RFL = localOverride.RflFeet.Value;
                fdr.RFLAssigned = localOverride.RflFeet.Value > 0;
            }

            if (localOverride.CflFeet.HasValue)
            {
                fdr.CFL = localOverride.CflFeet.Value;
                fdr.CFLAssigned = localOverride.CflFeet.Value > 0;
                fdr.CFLString = FormatFlightLevel(localOverride.CflFeet.Value);
            }

            if (localOverride.Squawk is not null)
            {
                fdr.AssignedSSRCode = ParseSquawk(localOverride.Squawk);
            }

            if (localOverride.Runway is not null)
            {
                fdr.DepartureRunway = string.IsNullOrWhiteSpace(localOverride.Runway)
                    ? null
                    : Airspace2.GetOrCreateRunway(fdr.DepAirport, localOverride.Runway.Trim().ToUpperInvariant());
                fdr.DepartureRunwayAssigned = fdr.DepartureRunway != null;
            }
            else if (localOverride.RunwayMenuOpened && fdr.DepartureRunway != null)
            {
                fdr.DepartureRunwayAssigned = true;
            }

            if (localOverride.Sid is not null)
            {
                fdr.SID = string.IsNullOrWhiteSpace(localOverride.Sid)
                    ? null
                    : new FDP2.SIDSTAR
                    {
                        Name = localOverride.Sid.Trim().ToUpperInvariant(),
                        Runway = fdr.DepartureRunway?.Name ?? string.Empty,
                    };
                fdr.SIDSTARString = fdr.SID?.Name ?? string.Empty;
            }

            if (localOverride.GlobalOpData is not null)
            {
                fdr.GlobalOpData = localOverride.GlobalOpData;
            }
        }

        private static string FormatFlightLevel(int feet)
        {
            return feet <= 0 ? string.Empty : (feet / 100).ToString("000");
        }

        private static int ParseSquawk(string squawk)
        {
            if (string.IsNullOrWhiteSpace(squawk))
            {
                return -1;
            }

            try
            {
                return Convert.ToInt32(squawk.Trim(), 8);
            }
            catch
            {
                return -1;
            }
        }

        private static List<SectorsVolumes.Sector> BuildSectorList(SnapshotMessage snapshot)
        {
            var sectors = new List<SectorsVolumes.Sector>();

            if (!string.IsNullOrWhiteSpace(snapshot.Controller.Callsign))
            {
                sectors.Add(new SectorsVolumes.Sector { Name = snapshot.Controller.Callsign.Trim().ToUpperInvariant() });
            }

            foreach (var aerodrome in snapshot.Flights.SelectMany(x => new[] { x.Adep, x.Ades }).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                sectors.Add(new SectorsVolumes.Sector { Name = aerodrome.Trim().ToUpperInvariant() });
            }

            return sectors;
        }

        private static IReadOnlyList<string> ExtractAerodromes(SnapshotMessage snapshot)
        {
            return snapshot.Flights
                .SelectMany(x => new[] { x.Adep, x.Ades })
                .Select(NormaliseAerodrome)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string InferAerodrome(SnapshotMessage snapshot, IReadOnlyList<string> aerodromes)
        {
            var fromController = InferAerodromeFromCallsign(snapshot.Controller.Callsign);
            if (!string.IsNullOrWhiteSpace(fromController))
            {
                return fromController;
            }

            var selected = snapshot.Flights.FirstOrDefault(x => x.Selected);
            var selectedAerodrome = selected == null ? string.Empty : PreferDepartureAerodrome(selected);
            if (!string.IsNullOrWhiteSpace(selectedAerodrome))
            {
                return selectedAerodrome;
            }

            return aerodromes.FirstOrDefault(x => x.StartsWith("NZ", StringComparison.OrdinalIgnoreCase)) ?? aerodromes.FirstOrDefault() ?? string.Empty;
        }

        private static string InferAerodromeFromCallsign(string callsign)
        {
            callsign = (callsign ?? string.Empty).Trim().ToUpperInvariant();
            if (callsign.Length >= 4 && callsign.StartsWith("NZ", StringComparison.Ordinal))
            {
                var candidate = callsign.Substring(0, 4);
                if (candidate.All(char.IsLetter))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static string PreferDepartureAerodrome(FlightPlanSnapshot flight)
        {
            var dep = NormaliseAerodrome(flight.Adep);
            if (!string.IsNullOrWhiteSpace(dep))
            {
                return dep;
            }

            return NormaliseAerodrome(flight.Ades);
        }

        private static string NormaliseAerodrome(string value)
        {
            value = (value ?? string.Empty).Trim().ToUpperInvariant();
            return value.Length == 4 && value.All(char.IsLetter) ? value : string.Empty;
        }

        private static void EnsureRunwaySidHints(FDP2.FDR fdr)
        {
            if (fdr.DepartureRunway == null || fdr.SID == null)
            {
                return;
            }

            if (!fdr.DepartureRunway.SIDs.Any(x => string.Equals(x.sidStar.Name, fdr.SID.Name, StringComparison.OrdinalIgnoreCase)))
            {
                fdr.SID.Runway = fdr.DepartureRunway.Name;
                fdr.DepartureRunway.SIDs.Add(new Airspace2.SIDSTARMapping { sidStar = fdr.SID });
            }
        }

        private sealed class LocalFlightOverride
        {
            public string? Sid { get; set; }

            public string? Runway { get; set; }

            public string? Squawk { get; set; }

            public int? RflFeet { get; set; }

            public int? CflFeet { get; set; }

            public string? GlobalOpData { get; set; }

            public bool RunwayMenuOpened { get; set; }
        }
    }
}
