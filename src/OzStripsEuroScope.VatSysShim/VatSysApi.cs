using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace vatsys
{
    public sealed class FDRParseException : Exception
    {
        public FDRParseException(string message)
            : base(message)
        {
        }
    }

    public sealed class LatLong
    {
        public LatLong()
        {
        }

        public LatLong(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
        }

        public double Latitude { get; set; }

        public double Longitude { get; set; }
    }

    public static class FDP2
    {
        private static readonly List<FDR> FDRList = new List<FDR>();
        private static readonly Random SquawkRandom = new Random();
        private static readonly object SquawkLock = new object();
        private static readonly Regex SidRunwayRegex = new Regex(@"^(?<sid>[^/]+?)(?:/(?<runway>\d{2}[LCR]?))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<FDR> GetFDRs => FDRList;

        public static int GetFDRIndex(string callsign)
        {
            return FDRList.FindIndex(x => string.Equals(x.Callsign, callsign, StringComparison.OrdinalIgnoreCase));
        }

        internal static FDR UpsertFDR(OzStripsEuroScope.VatSysShim.FlightPlanSnapshot snapshot)
        {
            var callsign = (snapshot.Callsign ?? string.Empty).Trim().ToUpperInvariant();
            var fdr = FDRList.FirstOrDefault(x => string.Equals(x.Callsign, callsign, StringComparison.OrdinalIgnoreCase));

            if (fdr == null)
            {
                fdr = new FDR { Callsign = callsign };
                FDRList.Add(fdr);
            }

            fdr.AircraftType = snapshot.AircraftType ?? string.Empty;
            fdr.AircraftWake = string.IsNullOrWhiteSpace(snapshot.WakeCategory) ? string.Empty : snapshot.WakeCategory.Trim().Substring(0, 1);
            fdr.FlightRules = string.IsNullOrWhiteSpace(snapshot.FlightRules) ? "I" : snapshot.FlightRules.Substring(0, 1).ToUpperInvariant();
            fdr.DepAirport = (snapshot.Adep ?? string.Empty).Trim().ToUpperInvariant();
            fdr.DesAirport = (snapshot.Ades ?? string.Empty).Trim().ToUpperInvariant();
            fdr.Route = snapshot.Route ?? string.Empty;
            fdr.RouteNoParse = fdr.Route;
            fdr.RFL = snapshot.RflFeet;
            var cflFeet = snapshot.CflFeet > 2 && snapshot.CflFeet != snapshot.RflFeet ? snapshot.CflFeet : 0;
            fdr.CFL = cflFeet;
            fdr.CFLAssigned = cflFeet > 0;
            fdr.CFLString = FormatFlightLevel(cflFeet);
            fdr.AssignedSSRCode = ParseSquawk(snapshot.Squawk);
            fdr.GlobalOpData = !string.IsNullOrWhiteSpace(snapshot.Scratchpad)
                ? snapshot.Scratchpad.Trim()
                : snapshot.AssignedHeading > 0 ? "H" + snapshot.AssignedHeading.ToString("000", CultureInfo.InvariantCulture) : string.Empty;
            fdr.State = FDR.FDRStates.STATE_PREACTIVE;
            fdr.HavePermission = true;
            var sidAndRunway = ParseSidAndRunway(snapshot.Sid, snapshot.Runway, snapshot.Route ?? string.Empty);
            var runwayName = string.IsNullOrWhiteSpace(sidAndRunway.Runway)
                ? (snapshot.ActiveDepartureRunway ?? string.Empty).Trim().ToUpperInvariant()
                : sidAndRunway.Runway;
            var starAndRunway = ParseStarAndRunway(snapshot.Star, snapshot.ArrivalRunway, snapshot.Route ?? string.Empty);
            fdr.DepartureRunway = string.IsNullOrWhiteSpace(runwayName) ? null : Airspace2.GetOrCreateRunway(fdr.DepAirport, runwayName);
            fdr.DepartureRunwayAssigned = fdr.DepartureRunway != null;
            fdr.ArrivalRunway = string.IsNullOrWhiteSpace(starAndRunway.Runway) ? null : Airspace2.GetOrCreateRunway(fdr.DesAirport, starAndRunway.Runway);
            fdr.SID = string.IsNullOrWhiteSpace(sidAndRunway.Sid) ? null : new SIDSTAR { Name = sidAndRunway.Sid, Runway = runwayName };
            fdr.STAR = string.IsNullOrWhiteSpace(starAndRunway.Star) ? null : new SIDSTAR { Name = starAndRunway.Star, Runway = starAndRunway.Runway };
            fdr.SIDSTARString = fdr.SID?.Name ?? string.Empty;
            EuroScopeSectorData.ApplySidOptions(fdr.DepAirport);
            EuroScopeSectorData.ApplyStarOptions(fdr.DesAirport);
            ApplySidOptions(fdr, snapshot.SidOptions);
            fdr.ParsedRoute = BuildRoute(fdr.DepAirport, fdr.DesAirport, fdr.Route, snapshot.FirstWaypoint, snapshot.RoutePoints);
            fdr.ETD = ParseEuroScopeTime(snapshot.Etd, fdr.ETD == default ? DateTime.UtcNow : fdr.ETD);

            return fdr;
        }

        private static void ApplySidOptions(FDR fdr, IEnumerable<OzStripsEuroScope.VatSysShim.SidOptionSnapshot>? options)
        {
            foreach (var option in options ?? Enumerable.Empty<OzStripsEuroScope.VatSysShim.SidOptionSnapshot>())
            {
                var name = (option.Name ?? string.Empty).Trim();
                var runwayName = (option.Runway ?? string.Empty).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(runwayName))
                {
                    continue;
                }

                var runway = Airspace2.GetOrCreateRunway(fdr.DepAirport, runwayName);
                if (runway.SIDs.Any(x => string.Equals(x.sidStar.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                runway.SIDs.Add(new Airspace2.SIDSTARMapping
                {
                    sidStar = new SIDSTAR
                    {
                        Name = name,
                        Runway = runwayName,
                    },
                });
            }
        }

        private static (string Sid, string Runway) ParseSidAndRunway(string sid, string runway, string route)
        {
            sid = (sid ?? string.Empty).Trim().ToUpperInvariant();
            runway = (runway ?? string.Empty).Trim().ToUpperInvariant();

            var routeSidRunway = ParseRouteSidAndRunway(route);
            if (!string.IsNullOrWhiteSpace(routeSidRunway.Sid))
            {
                sid = routeSidRunway.Sid;
            }

            if (!string.IsNullOrWhiteSpace(routeSidRunway.Runway))
            {
                runway = routeSidRunway.Runway;
            }

            var match = SidRunwayRegex.Match(sid);
            if (match.Success)
            {
                sid = match.Groups["sid"].Value.Trim().ToUpperInvariant();
                if (match.Groups["runway"].Success)
                {
                    runway = match.Groups["runway"].Value.Trim().ToUpperInvariant();
                }
            }

            return (sid, runway);
        }

        private static (string Star, string Runway) ParseStarAndRunway(string star, string runway, string route)
        {
            star = (star ?? string.Empty).Trim().ToUpperInvariant();
            runway = (runway ?? string.Empty).Trim().ToUpperInvariant();

            var routeStarRunway = ParseRouteStarAndRunway(route);
            if (!string.IsNullOrWhiteSpace(routeStarRunway.Star))
            {
                star = routeStarRunway.Star;
            }

            if (!string.IsNullOrWhiteSpace(routeStarRunway.Runway))
            {
                runway = routeStarRunway.Runway;
            }

            var match = SidRunwayRegex.Match(star);
            if (match.Success)
            {
                star = match.Groups["sid"].Value.Trim().ToUpperInvariant();
                if (match.Groups["runway"].Success)
                {
                    runway = match.Groups["runway"].Value.Trim().ToUpperInvariant();
                }
            }

            return (star, runway);
        }

        private static (string Sid, string Runway) ParseRouteSidAndRunway(string route)
        {
            var firstToken = (route ?? string.Empty)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;

            firstToken = firstToken.Trim().ToUpperInvariant();
            var match = SidRunwayRegex.Match(firstToken);
            if (!match.Success || !match.Groups["runway"].Success)
            {
                return (string.Empty, string.Empty);
            }

            var sid = match.Groups["sid"].Value.Trim().ToUpperInvariant();
            if (!LooksLikeSidToken(sid))
            {
                return (string.Empty, string.Empty);
            }

            return (sid, match.Groups["runway"].Value.Trim().ToUpperInvariant());
        }

        private static (string Star, string Runway) ParseRouteStarAndRunway(string route)
        {
            var lastToken = (route ?? string.Empty)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault() ?? string.Empty;

            lastToken = lastToken.Trim().ToUpperInvariant();
            var match = SidRunwayRegex.Match(lastToken);
            if (!match.Success)
            {
                return (string.Empty, string.Empty);
            }

            var star = match.Groups["sid"].Value.Trim().ToUpperInvariant();
            if (!LooksLikeSidToken(star))
            {
                return (string.Empty, string.Empty);
            }

            return (star, match.Groups["runway"].Success ? match.Groups["runway"].Value.Trim().ToUpperInvariant() : string.Empty);
        }

        private static bool LooksLikeSidToken(string value)
        {
            return Regex.IsMatch(value ?? string.Empty, @"^[A-Z]{4,}\d[A-Z][A-Z]*$", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(value ?? string.Empty, @"^[A-Z]{4}\d[A-Z]$", RegexOptions.IgnoreCase);
        }

        internal static void RemoveMissing(IEnumerable<string> activeCallsigns)
        {
            var active = new HashSet<string>(activeCallsigns.Select(x => x.ToUpperInvariant()));
            FDRList.RemoveAll(x => !active.Contains(x.Callsign.ToUpperInvariant()));
        }

        public static void SetCFL(FDR fdr, string value)
        {
            var feet = ParseFlightLevel(value);
            fdr.CFL = feet;
            fdr.CFLAssigned = feet > 0;
            fdr.CFLString = FormatFlightLevel(feet);
            OzStripsEuroScope.VatSysShim.EuroScopeBridge.SendCommand("SetCfl", fdr.Callsign, feet.ToString(CultureInfo.InvariantCulture));
        }

        public static void SetRFL(FDR fdr, string value)
        {
            var feet = ParseFlightLevel(value);
            fdr.RFL = feet;
            fdr.RFLAssigned = feet > 0;
            OzStripsEuroScope.VatSysShim.EuroScopeBridge.SendCommand("SetFinalAltitude", fdr.Callsign, feet.ToString(CultureInfo.InvariantCulture));
        }

        public static void SetGlobalOps(FDR fdr, string value)
        {
            fdr.GlobalOpData = value ?? string.Empty;
            var headingDigits = fdr.GlobalOpData.StartsWith("H", StringComparison.OrdinalIgnoreCase)
                ? new string(fdr.GlobalOpData.Skip(1).TakeWhile(char.IsDigit).ToArray())
                : string.Empty;

            if (headingDigits.Length > 0)
            {
                OzStripsEuroScope.VatSysShim.EuroScopeBridge.SendCommand("SetAssignedHeading", fdr.Callsign, headingDigits);
            }

            OzStripsEuroScope.VatSysShim.EuroScopeBridge.SendCommand("SetScratchpad", fdr.Callsign, fdr.GlobalOpData);
        }

        public static void ModifyRoute(FDR fdr, string route)
        {
            fdr.Route = route ?? string.Empty;
            fdr.RouteNoParse = fdr.Route;
            fdr.ParsedRoute = BuildRoute(fdr.DepAirport, fdr.DesAirport, fdr.Route, null, null);
            OzStripsEuroScope.VatSysShim.EuroScopeBridge.SendCommand("SetRoute", fdr.Callsign, fdr.Route);
        }

        public static void SetDepartureRunway(FDR fdr, Airspace2.SystemRunway? runway)
        {
            fdr.DepartureRunway = runway;
            fdr.DepartureRunwayAssigned = runway != null;
            OzStripsEuroScope.VatSysShim.EuroScopeBridge.SendCommand("SetDepartureRunway", fdr.Callsign, runway?.Name ?? string.Empty);
        }

        public static void SetSID(FDR fdr, SIDSTAR? sid)
        {
            fdr.SID = sid;
            fdr.SIDSTARString = sid?.Name ?? string.Empty;

            if (OzStripsEuroScope.VatSysShim.EuroScopeBridge.IsStarted)
            {
                var route = BuildRouteWithSid(fdr, sid);
                fdr.Route = route;
                fdr.RouteNoParse = route;
                fdr.ParsedRoute = BuildRoute(fdr.DepAirport, fdr.DesAirport, fdr.Route, null, null);
                OzStripsEuroScope.VatSysShim.EuroScopeBridge.SendCommand("SetRoute", fdr.Callsign, fdr.Route);
                return;
            }

            OzStripsEuroScope.VatSysShim.EuroScopeBridge.SendCommand("SetSid", fdr.Callsign, fdr.SIDSTARString);
        }

        public static void SetSTAR(FDR fdr, SIDSTAR? star)
        {
            fdr.STAR = star;

            if (OzStripsEuroScope.VatSysShim.EuroScopeBridge.IsStarted)
            {
                var route = BuildRouteWithStar(fdr, star);
                fdr.Route = route;
                fdr.RouteNoParse = route;
                fdr.ParsedRoute = BuildRoute(fdr.DepAirport, fdr.DesAirport, fdr.Route, null, null);
                OzStripsEuroScope.VatSysShim.EuroScopeBridge.SendCommand("SetRoute", fdr.Callsign, fdr.Route);
            }
        }

        private static string BuildRouteWithSid(FDR fdr, SIDSTAR? sid)
        {
            var tokens = (fdr.RouteNoParse ?? fdr.Route ?? string.Empty)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            var oldRunway = string.Empty;
            if (tokens.Count > 0)
            {
                var oldSidRunway = ParsePossibleSidToken(tokens[0]);
                if (!string.IsNullOrWhiteSpace(oldSidRunway.Sid))
                {
                    oldRunway = oldSidRunway.Runway;
                    tokens.RemoveAt(0);
                }
            }

            if (sid == null || string.IsNullOrWhiteSpace(sid.Name))
            {
                fdr.DepartureRunway = null;
                fdr.DepartureRunwayAssigned = false;
                return string.Join(" ", tokens);
            }

            var rawSid = (sid.Name ?? string.Empty).Trim();
            var selected = SidRunwayRegex.Match(rawSid);
            var runway = (sid.Runway ?? string.Empty).Trim().ToUpperInvariant();

            if (selected.Success)
            {
                rawSid = selected.Groups["sid"].Value.Trim();
                if (string.IsNullOrWhiteSpace(runway) && selected.Groups["runway"].Success)
                {
                    runway = selected.Groups["runway"].Value.Trim().ToUpperInvariant();
                }
            }

            if (string.IsNullOrWhiteSpace(runway))
            {
                runway = (fdr.DepartureRunway?.Name ?? oldRunway).Trim().ToUpperInvariant();
            }

            if (!string.IsNullOrWhiteSpace(runway))
            {
                fdr.DepartureRunway = Airspace2.GetOrCreateRunway(fdr.DepAirport, runway);
                fdr.DepartureRunwayAssigned = true;
            }

            var routeSidToken = string.IsNullOrWhiteSpace(runway)
                ? rawSid
                : rawSid + "/" + runway;
            tokens.Insert(0, routeSidToken);
            return string.Join(" ", tokens);
        }

        private static string BuildRouteWithStar(FDR fdr, SIDSTAR? star)
        {
            var tokens = (fdr.RouteNoParse ?? fdr.Route ?? string.Empty)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            var oldRunway = string.Empty;
            if (tokens.Count > 0)
            {
                var oldStarRunway = ParsePossibleSidToken(tokens[tokens.Count - 1]);
                if (!string.IsNullOrWhiteSpace(oldStarRunway.Sid))
                {
                    oldRunway = oldStarRunway.Runway;
                    tokens.RemoveAt(tokens.Count - 1);
                }
            }

            if (star == null || string.IsNullOrWhiteSpace(star.Name))
            {
                fdr.ArrivalRunway = null;
                return string.Join(" ", tokens);
            }

            var rawStar = (star.Name ?? string.Empty).Trim();
            var selected = SidRunwayRegex.Match(rawStar);
            var runway = (star.Runway ?? string.Empty).Trim().ToUpperInvariant();

            if (selected.Success)
            {
                rawStar = selected.Groups["sid"].Value.Trim();
                if (string.IsNullOrWhiteSpace(runway) && selected.Groups["runway"].Success)
                {
                    runway = selected.Groups["runway"].Value.Trim().ToUpperInvariant();
                }
            }

            if (string.IsNullOrWhiteSpace(runway))
            {
                runway = (fdr.ArrivalRunway?.Name ?? oldRunway).Trim().ToUpperInvariant();
            }

            if (!string.IsNullOrWhiteSpace(runway))
            {
                fdr.ArrivalRunway = Airspace2.GetOrCreateRunway(fdr.DesAirport, runway);
            }

            var routeStarToken = string.IsNullOrWhiteSpace(runway)
                ? rawStar
                : rawStar + "/" + runway;
            tokens.Add(routeStarToken);
            return string.Join(" ", tokens);
        }

        private static (string Sid, string Runway) ParsePossibleSidToken(string token)
        {
            token = (token ?? string.Empty).Trim().ToUpperInvariant();
            var match = SidRunwayRegex.Match(token);
            if (!match.Success)
            {
                return (string.Empty, string.Empty);
            }

            var sid = match.Groups["sid"].Value.Trim().ToUpperInvariant();
            return LooksLikeSidToken(sid)
                ? (sid, match.Groups["runway"].Success ? match.Groups["runway"].Value.Trim().ToUpperInvariant() : string.Empty)
                : (string.Empty, string.Empty);
        }

        public static void SetASSR(FDR fdr)
        {
            if (!IsDefaultSquawk(fdr.AssignedSSRCode))
            {
                return;
            }

            SetSSR(fdr, AllocateSquawk(fdr));
        }

        public static void SetSSR(FDR fdr, string squawk)
        {
            fdr.AssignedSSRCode = ParseSquawk(squawk);
            OzStripsEuroScope.VatSysShim.EuroScopeBridge.SendCommand("SetSquawk", fdr.Callsign, (squawk ?? string.Empty).Trim());
        }

        public static string FormatSSR(int squawk)
        {
            return IsDefaultSquawk(squawk) ? "XXXX" : Convert.ToString(squawk, 8).PadLeft(4, '0');
        }

        public static bool IsDefaultSquawk(int squawk)
        {
            return squawk == -1 || FormatSquawkOctal(squawk) == "2000";
        }

        public static void BackFDR(FDR fdr)
        {
            fdr.State = FDR.FDRStates.STATE_PREACTIVE;
        }

        private static int ParseFlightLevel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            var digits = new string(value.Where(char.IsDigit).ToArray());
            if (digits.Length == 0 || !int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return 0;
            }

            return parsed <= 999 ? parsed * 100 : parsed;
        }

        private static string FormatFlightLevel(int feet)
        {
            return feet <= 0 ? string.Empty : (feet / 100).ToString("000", CultureInfo.InvariantCulture);
        }

        private static DateTime ParseEuroScopeTime(string value, DateTime fallback)
        {
            var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.Length < 4)
            {
                return fallback;
            }

            digits = digits.Substring(0, 4);
            if (!int.TryParse(digits.Substring(0, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour) ||
                !int.TryParse(digits.Substring(2, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minute) ||
                hour > 23 ||
                minute > 59)
            {
                return fallback;
            }

            var now = DateTime.UtcNow;
            return new DateTime(now.Year, now.Month, now.Day, hour, minute, 0, DateTimeKind.Utc);
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

        private static string AllocateSquawk(FDR fdr)
        {
            var ranges = GetSquawkRanges(fdr);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assigned in FDRList.Where(x => !string.Equals(x.Callsign, fdr.Callsign, StringComparison.OrdinalIgnoreCase)))
            {
                var code = FormatSquawkOctal(assigned.AssignedSSRCode);
                if (!string.IsNullOrWhiteSpace(code) && code != "2000")
                {
                    used.Add(code);
                }
            }

            var candidates = ranges
                .SelectMany(range => EnumerateOctalRange(range.Start, range.End))
                .Where(code => !used.Contains(code) && code != "2000")
                .ToList();

            var nonBoundaryCandidates = candidates
                .Where(code => code.Substring(1) != "000")
                .ToList();

            if (nonBoundaryCandidates.Count > 0)
            {
                candidates = nonBoundaryCandidates;
            }

            if (candidates.Count == 0)
            {
                return "2000";
            }

            lock (SquawkLock)
            {
                return candidates[SquawkRandom.Next(candidates.Count)];
            }
        }

        private static IEnumerable<(string Start, string End)> GetSquawkRanges(FDR fdr)
        {
            if (string.Equals(fdr.FlightRules, "V", StringComparison.OrdinalIgnoreCase))
            {
                yield return ("0300", "0377");
                yield return ("3000", "3777");
                yield return ("4000", "4777");
                yield break;
            }

            if (IsNewZealandDomestic(fdr))
            {
                yield return ("5000", "5777");
                yield break;
            }

            yield return ("0200", "0277");
        }

        private static bool IsNewZealandDomestic(FDR fdr)
        {
            return fdr.DepAirport.StartsWith("NZ", StringComparison.OrdinalIgnoreCase) &&
                fdr.DesAirport.StartsWith("NZ", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> EnumerateOctalRange(string start, string end)
        {
            var startValue = Convert.ToInt32(start, 8);
            var endValue = Convert.ToInt32(end, 8);
            for (var value = startValue; value <= endValue; value++)
            {
                yield return Convert.ToString(value, 8).PadLeft(4, '0');
            }
        }

        private static string FormatSquawkOctal(int squawk)
        {
            return squawk < 0 ? string.Empty : Convert.ToString(squawk, 8).PadLeft(4, '0');
        }

        private static List<FDR.ExtractedRoute.Segment> BuildRoute(string dep, string des, string route, string? firstWaypoint, IEnumerable<OzStripsEuroScope.VatSysShim.RoutePointSnapshot>? routePoints)
        {
            var tokens = new List<string>();
            var pointLookup = (routePoints ?? Enumerable.Empty<OzStripsEuroScope.VatSysShim.RoutePointSnapshot>())
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name.Trim().ToUpperInvariant())
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(dep))
            {
                tokens.Add(dep);
            }

            if (!string.IsNullOrWhiteSpace(firstWaypoint))
            {
                tokens.Add(firstWaypoint!);
            }

            tokens.AddRange((route ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

            if (!string.IsNullOrWhiteSpace(des))
            {
                tokens.Add(des);
            }

            return tokens
                .Where(x => !string.Equals(x, "DCT", StringComparison.OrdinalIgnoreCase))
                .Select(token =>
                {
                    var intersection = Airspace2.GetOrCreateIntersection(token);
                    if (pointLookup.TryGetValue(token.Trim().ToUpperInvariant(), out var point) && IsUsableLatLong(point.Latitude, point.Longitude))
                    {
                        intersection.LatLong = new LatLong(point.Latitude, point.Longitude);
                    }

                    return new FDR.ExtractedRoute.Segment
                    {
                        Type = FDR.ExtractedRoute.Segment.SegmentTypes.WAYPOINT,
                        AirwayName = LooksLikeAirway(token) ? token : string.Empty,
                        SIDSTARName = string.Empty,
                        Intersection = intersection,
                    };
                })
                .ToList();
        }

        private static bool IsUsableLatLong(double latitude, double longitude)
        {
            return Math.Abs(latitude) > 0.000001 || Math.Abs(longitude) > 0.000001;
        }

        private static bool LooksLikeAirway(string token)
        {
            return token.Length > 1 && char.IsLetter(token[0]) && token.Skip(1).Any(char.IsDigit);
        }

        public sealed class FDR
        {
            public enum FDRStates
            {
                STATE_UNKNOWN = 0,
                STATE_INACTIVE = 1,
                STATE_SUSPENDED = 2,
                STATE_PREACTIVE = 5,
                STATE_COORDINATED = 6,
                STATE_ACTIVE = 7,
                STATE_FINISHED = 8,
            }

            public string Callsign { get; set; } = string.Empty;

            public string AircraftType { get; set; } = string.Empty;

            public string AircraftWake { get; set; } = string.Empty;

            public string AircraftTypeAndWake => string.IsNullOrWhiteSpace(AircraftWake) ? AircraftType : AircraftType + "/" + AircraftWake;

            public string FlightRules { get; set; } = "I";

            public string DepAirport { get; set; } = string.Empty;

            public string DesAirport { get; set; } = string.Empty;

            public string Route { get; set; } = string.Empty;

            public string RouteNoParse { get; set; } = string.Empty;

            public int RFL { get; set; }

            public bool RFLAssigned { get; set; }

            public int CFL { get; set; }

            public bool CFLAssigned { get; set; }

            public string CFLString { get; set; } = string.Empty;

            public int AssignedSSRCode { get; set; } = -1;

            public string GlobalOpData { get; set; } = string.Empty;

            public DateTime ETD { get; set; } = DateTime.UtcNow;

            public DateTime ATD { get; set; } = DateTime.MaxValue;

            public Airspace2.SystemRunway? DepartureRunway { get; set; }

            public bool DepartureRunwayAssigned { get; set; }

            public Airspace2.SystemRunway? ArrivalRunway { get; set; }

            public SIDSTAR? SID { get; set; }

            public SIDSTAR? STAR { get; set; }

            public string SIDSTARString { get; set; } = string.Empty;

            public List<ExtractedRoute.Segment> ParsedRoute { get; set; } = new List<ExtractedRoute.Segment>();

            public FDRStates State { get; set; } = FDRStates.STATE_PREACTIVE;

            public PredictedPosition? PredictedPosition { get; set; }

            public bool HavePermission { get; set; } = true;

            public bool TextOnly { get; set; }

            public bool ReceiveOnly { get; set; }

            public bool PDCSent { get; set; }

            public sealed class ExtractedRoute
            {
                public sealed class Segment
                {
                    public enum SegmentTypes
                    {
                        WAYPOINT,
                        AIRWAY,
                    }

                    public SegmentTypes Type { get; set; }

                    public string SIDSTARName { get; set; } = string.Empty;

                    public string AirwayName { get; set; } = string.Empty;

                    public Airspace2.Intersection Intersection { get; set; } = new Airspace2.Intersection();
                }
            }
        }

        public sealed class SIDSTAR
        {
            public string Name { get; set; } = string.Empty;

            public string Runway { get; set; } = string.Empty;

            public Dictionary<string, object> Transitions { get; set; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }

        public sealed class PredictedPosition
        {
            public LatLong? Location { get; set; }
        }
    }

    public static class Airspace2
    {
        private static readonly Dictionary<string, List<SystemRunway>> Runways = new Dictionary<string, List<SystemRunway>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Intersection> Intersections = new Dictionary<string, Intersection>(StringComparer.OrdinalIgnoreCase);

        public static List<SystemRunway> GetRunways(string aerodrome)
        {
            if (string.IsNullOrWhiteSpace(aerodrome))
            {
                return new List<SystemRunway>();
            }

            if (!Runways.TryGetValue(aerodrome, out var runways))
            {
                runways = new List<SystemRunway>();
                Runways[aerodrome] = runways;
            }

            return runways;
        }

        public static SystemRunway GetOrCreateRunway(string aerodrome, string runwayName)
        {
            var runways = GetRunways(aerodrome);
            var runway = runways.FirstOrDefault(x => string.Equals(x.Name, runwayName, StringComparison.OrdinalIgnoreCase));
            if (runway == null)
            {
                runway = new SystemRunway { Name = runwayName };
                runways.Add(runway);
            }

            return runway;
        }

        public static Intersection? GetAirport(string icao)
        {
            return string.IsNullOrWhiteSpace(icao) ? null : GetOrCreateIntersection(icao, Intersection.Types.Airport);
        }

        public static Intersection? GetIntersection(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? null : GetOrCreateIntersection(name);
        }

        internal static Intersection GetOrCreateIntersection(string name, Intersection.Types type = Intersection.Types.Waypoint)
        {
            name = (name ?? string.Empty).Trim().ToUpperInvariant();
            if (!Intersections.TryGetValue(name, out var intersection))
            {
                intersection = new Intersection
                {
                    Name = name,
                    FullName = name,
                    Type = type,
                    LatLong = new LatLong(),
                };
                Intersections[name] = intersection;
            }

            if (type == Intersection.Types.Airport)
            {
                intersection.Type = type;
            }

            return intersection;
        }

        public sealed class SystemRunway
        {
            public string Name { get; set; } = string.Empty;

            public List<SIDSTARMapping> SIDs { get; set; } = new List<SIDSTARMapping>();

            public List<SIDSTARMapping> STARs { get; set; } = new List<SIDSTARMapping>();
        }

        public sealed class SIDSTARMapping
        {
            public FDP2.SIDSTAR sidStar { get; set; } = new FDP2.SIDSTAR();
        }

        public sealed class Intersection
        {
            public enum Types
            {
                Airport,
                Waypoint,
                Navaid,
            }

            public string Name { get; set; } = string.Empty;

            public string FullName { get; set; } = string.Empty;

            public Types Type { get; set; } = Types.Waypoint;

            public LatLong LatLong { get; set; } = new LatLong();
        }
    }

    public static class RDP
    {
        public static List<RadarTrack> RadarTracks { get; } = new List<RadarTrack>();

        internal static RadarTrack UpsertTrack(FDP2.FDR fdr, OzStripsEuroScope.VatSysShim.FlightPlanSnapshot snapshot)
        {
            var track = RadarTracks.FirstOrDefault(x => x.ActualAircraft.Callsign == fdr.Callsign);
            if (track == null)
            {
                track = new RadarTrack();
                RadarTracks.Add(track);
            }

            track.FDR = fdr;
            track.ActualAircraft.Callsign = fdr.Callsign;
            track.ActualAircraft.TransponderCode = fdr.AssignedSSRCode;
            track.ActualAircraft.TransponderModeC = true;
            return track;
        }

        internal static void RemoveMissing(IEnumerable<string> activeCallsigns)
        {
            var active = new HashSet<string>(activeCallsigns.Select(x => x.ToUpperInvariant()));
            RadarTracks.RemoveAll(x => !active.Contains(x.ActualAircraft.Callsign.ToUpperInvariant()));
        }

        public sealed class RadarTrack
        {
            public Aircraft ActualAircraft { get; set; } = new Aircraft();

            internal FDP2.FDR? FDR { get; set; }
        }
    }

    public sealed class Aircraft
    {
        public string Callsign { get; set; } = string.Empty;

        public LatLong? Position { get; set; }

        public int TransponderCode { get; set; } = -1;

        public bool TransponderModeC { get; set; }
    }

    public sealed class Track
    {
        public FDP2.FDR? FDR { get; set; }

        public Network.Pilot? Pilot { get; set; }

        public Aircraft? ActualAircraft { get; set; }

        public bool GraphicRTE { get; set; }

        public FDP2.FDR? GetFDR()
        {
            return FDR;
        }

        public Network.Pilot GetPilot()
        {
            return Pilot ?? new Network.Pilot { Callsign = FDR?.Callsign ?? string.Empty };
        }
    }

    public static class Network
    {
        public static event EventHandler? Connected;

        public static event EventHandler? Disconnected;

        public static event EventHandler<PilotUpdateEventArgs>? OnlinePilotsChanged;

        public static Controller Me { get; } = new Controller
        {
            Callsign = "EUROSCOPE",
            Name = "EUROSCOPE",
            IsRealATC = true,
            Frequencies = new List<int>(),
        };

        public static string Callsign => Me.Callsign;

        public static bool IsConnected { get; internal set; } = true;

        public static bool IsOfficialServer { get; set; } = true;

        public static List<Pilot> GetOnlinePilots { get; } = new List<Pilot>();

        public static List<Controller> GetOnlineATCs { get; } = new List<Controller>();

        internal static void SetController(string callsign)
        {
            if (!string.IsNullOrWhiteSpace(callsign))
            {
                Me.Callsign = callsign.Trim().ToUpperInvariant();
                Me.Name = Me.Callsign;
            }

            if (!GetOnlineATCs.Any(x => x.Callsign == Me.Callsign))
            {
                GetOnlineATCs.Add(Me);
            }
        }

        internal static Pilot UpsertPilot(FDP2.FDR fdr)
        {
            var pilot = GetOnlinePilots.FirstOrDefault(x => x.Callsign == fdr.Callsign);
            if (pilot == null)
            {
                pilot = new Pilot { Callsign = fdr.Callsign };
                GetOnlinePilots.Add(pilot);
                OnlinePilotsChanged?.Invoke(null, new PilotUpdateEventArgs(false, pilot));
            }

            return pilot;
        }

        internal static void RemoveMissing(IEnumerable<string> activeCallsigns)
        {
            var active = new HashSet<string>(activeCallsigns.Select(x => x.ToUpperInvariant()));
            foreach (var removed in GetOnlinePilots.Where(x => !active.Contains(x.Callsign.ToUpperInvariant())).ToList())
            {
                GetOnlinePilots.Remove(removed);
                OnlinePilotsChanged?.Invoke(null, new PilotUpdateEventArgs(true, removed));
            }
        }

        internal static void RaiseConnected()
        {
            IsConnected = true;
            Connected?.Invoke(null, EventArgs.Empty);
        }

        internal static void RaiseDisconnected()
        {
            IsConnected = false;
            Disconnected?.Invoke(null, EventArgs.Empty);
        }

        public class Client
        {
            public string Callsign { get; set; } = string.Empty;

            public string Name { get; set; } = string.Empty;

            public List<int>? Frequencies { get; set; }
        }

        public sealed class Controller : Client
        {
            public bool IsRealATC { get; set; }
        }

        public sealed class Pilot : Client
        {
            public int GroundSpeed { get; set; }
        }

        public sealed class PilotUpdateEventArgs : EventArgs
        {
            public PilotUpdateEventArgs(bool removed, Pilot? updatedPilot)
            {
                Removed = removed;
                UpdatedPilot = updatedPilot;
            }

            public bool Removed { get; }

            public Pilot? UpdatedPilot { get; }
        }
    }

    public static class MMI
    {
        private static SynchronizationContext? guiContext;

        public static event EventHandler? PrimePositonChanged;

        public static event EventHandler? SectorsControlledChanged;

        public static event EventHandler? SelectedTrackChanged;

        public static event EventHandler? SelectedGroundTrackChanged;

        public static Network.Controller? PrimePosition { get; internal set; } = Network.Me;

        public static List<SectorsVolumes.Sector>? SectorsControlled { get; internal set; } = new List<SectorsVolumes.Sector>();

        public static Track? SelectedTrack { get; private set; }

        public static Track? SelectedGroundTrack { get; private set; }

        public static void InstallGuiContext(SynchronizationContext context)
        {
            guiContext = context;
        }

        public static void InvokeOnGUI(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (guiContext != null && SynchronizationContext.Current != guiContext)
            {
                guiContext.Post(_ => action(), null);
                return;
            }

            action();
        }

        public static Track? FindTrack(FDP2.FDR? fdr)
        {
            if (fdr == null)
            {
                return null;
            }

            var pilot = Network.GetOnlinePilots.FirstOrDefault(x => x.Callsign == fdr.Callsign);
            var radar = RDP.RadarTracks.FirstOrDefault(x => x.ActualAircraft.Callsign == fdr.Callsign);
            return new Track { FDR = fdr, Pilot = pilot, ActualAircraft = radar?.ActualAircraft };
        }

        public static Track? FindTrack(RDP.RadarTrack? radarTrack)
        {
            return radarTrack == null ? null : FindTrack(radarTrack.FDR);
        }

        public static void SelectOrDeselectTrack(Track? track)
        {
            SelectedTrack = SelectedTrack == track ? null : track;
            SelectedTrackChanged?.Invoke(null, EventArgs.Empty);
        }

        public static void SelectOrDeselectGroundTrack(Track? track)
        {
            SelectedGroundTrack = SelectedGroundTrack == track ? null : track;
            SelectedGroundTrackChanged?.Invoke(null, EventArgs.Empty);
        }

        internal static void SetSelected(FDP2.FDR? fdr)
        {
            SelectedTrack = FindTrack(fdr);
            SelectedTrackChanged?.Invoke(null, EventArgs.Empty);
        }

        internal static void RaisePositionChanged()
        {
            PrimePositonChanged?.Invoke(null, EventArgs.Empty);
            SectorsControlledChanged?.Invoke(null, EventArgs.Empty);
        }

        public static void EstFDR(FDP2.FDR fdr)
        {
            fdr.State = FDP2.FDR.FDRStates.STATE_COORDINATED;
        }

        public static void OpenFPWindow(FDP2.FDR fdr, Point? position = null)
        {
            OzStripsEuroScope.VatSysShim.EuroScopeBridge.SendCommand("OpenFlightPlan", fdr.Callsign, EncodePoint(position ?? Cursor.Position));
        }

        public static void OpenCFLMenu(Track track, Point position)
        {
        }

        public static void OpenRWYMenu(FDP2.FDR fdr, Point position)
        {
            OzStripsEuroScope.VatSysShim.EuroScopeBridge.SendCommand("OpenRunwayMenu", fdr.Callsign, EncodePoint(position));
        }

        public static void OpenSIDSTARMenu(FDP2.FDR fdr, Point position)
        {
            OzStripsEuroScope.VatSysShim.EuroScopeBridge.SendCommand("OpenSidMenu", fdr.Callsign, EncodePoint(position));
        }

        public static void OpenCPDLCWindow(FDP2.FDR fdr, object? unused, CPDLC.MessageCategory? category)
        {
        }

        public static void OpenPMWindow(string callsign)
        {
        }

        public static void ShowGraphicRoute(Track track)
        {
        }

        public static void HideGraphicRoute(Track track)
        {
        }

        public static void EnsureWindowVisible(Form form)
        {
            if (form.WindowState == FormWindowState.Minimized)
            {
                form.WindowState = FormWindowState.Normal;
            }
        }

        public static void AddCustomMenuItem(object item)
        {
        }

        private static string EncodePoint(Point position)
        {
            return position.X.ToString(CultureInfo.InvariantCulture) + "," + position.Y.ToString(CultureInfo.InvariantCulture);
        }
    }

    internal static class EuroScopeSectorData
    {
        private static readonly object SyncRoot = new object();
        private static bool loaded;
        private static readonly List<SidRecord> SidRecords = new List<SidRecord>();
        private static readonly List<SidRecord> StarRecords = new List<SidRecord>();

        public static void ApplySidOptions(string aerodrome)
        {
            aerodrome = (aerodrome ?? string.Empty).Trim().ToUpperInvariant();
            if (aerodrome.Length != 4)
            {
                return;
            }

            EnsureLoaded();

            foreach (var sid in SidRecords.Where(x => string.Equals(x.Aerodrome, aerodrome, StringComparison.OrdinalIgnoreCase)))
            {
                var runway = Airspace2.GetOrCreateRunway(sid.Aerodrome, sid.Runway);
                if (runway.SIDs.Any(x => string.Equals(x.sidStar.Name, sid.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                runway.SIDs.Add(new Airspace2.SIDSTARMapping
                {
                    sidStar = new FDP2.SIDSTAR
                    {
                        Name = sid.Name,
                        Runway = sid.Runway,
                    },
                });
            }
        }

        public static void ApplyStarOptions(string aerodrome)
        {
            aerodrome = (aerodrome ?? string.Empty).Trim().ToUpperInvariant();
            if (aerodrome.Length != 4)
            {
                return;
            }

            EnsureLoaded();

            foreach (var star in StarRecords.Where(x => string.Equals(x.Aerodrome, aerodrome, StringComparison.OrdinalIgnoreCase)))
            {
                var runway = Airspace2.GetOrCreateRunway(star.Aerodrome, star.Runway);
                if (runway.STARs.Any(x => string.Equals(x.sidStar.Name, star.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                runway.STARs.Add(new Airspace2.SIDSTARMapping
                {
                    sidStar = new FDP2.SIDSTAR
                    {
                        Name = star.Name,
                        Runway = star.Runway,
                    },
                });
            }
        }

        private static void EnsureLoaded()
        {
            lock (SyncRoot)
            {
                if (loaded)
                {
                    return;
                }

                loaded = true;
                foreach (var path in FindSectorFiles())
                {
                    LoadSectorFile(path);
                }
            }
        }

        private static IEnumerable<string> FindSectorFiles()
        {
            var candidates = new List<string>();
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            AddCandidate(candidates, Path.Combine(programFilesX86, "EuroScope", "VATNZ-SKYLINE_2412", "VATNZ-NZZC_2604.ese"));
            AddCandidate(candidates, Path.Combine(programFiles, "EuroScope", "VATNZ-SKYLINE_2412", "VATNZ-NZZC_2604.ese"));

            foreach (var root in new[]
            {
                Path.Combine(programFilesX86, "EuroScope"),
                Path.Combine(programFiles, "EuroScope"),
                AppDomain.CurrentDomain.BaseDirectory,
            })
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                try
                {
                    foreach (var file in Directory.EnumerateFiles(root, "*.ese", SearchOption.AllDirectories)
                        .Where(x => x.IndexOf("VATNZ", StringComparison.OrdinalIgnoreCase) >= 0)
                        .OrderByDescending(File.GetLastWriteTimeUtc))
                    {
                        AddCandidate(candidates, file);
                    }
                }
                catch
                {
                    // Some Program Files subfolders can be inaccessible; the known path above is enough in normal VATNZ installs.
                }
            }

            return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static void AddCandidate(List<string> candidates, string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                candidates.Add(path);
            }
        }

        private static void LoadSectorFile(string path)
        {
            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    var trimmed = (line ?? string.Empty).Trim();
                    var isSid = trimmed.StartsWith("SID:", StringComparison.OrdinalIgnoreCase);
                    var isStar = trimmed.StartsWith("STAR:", StringComparison.OrdinalIgnoreCase);
                    if (!isSid && !isStar)
                    {
                        continue;
                    }

                    var parts = trimmed.Split(':');
                    if (parts.Length < 4)
                    {
                        continue;
                    }

                    var aerodrome = parts[1].Trim().ToUpperInvariant();
                    var runway = parts[2].Trim().ToUpperInvariant();
                    var name = parts[3].Trim();
                    if (aerodrome.Length != 4 || string.IsNullOrWhiteSpace(runway) || string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var records = isSid ? SidRecords : StarRecords;
                    if (records.Any(x =>
                            string.Equals(x.Aerodrome, aerodrome, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(x.Runway, runway, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    records.Add(new SidRecord(aerodrome, runway, name));
                }
            }
            catch
            {
                // Missing or locked sector files just mean the bridge snapshot remains the source of SID options.
            }
        }

        private sealed class SidRecord
        {
            public SidRecord(string aerodrome, string runway, string name)
            {
                Aerodrome = aerodrome;
                Runway = runway;
                Name = name;
            }

            public string Aerodrome { get; }

            public string Runway { get; }

            public string Name { get; }
        }
    }

    public static class SectorsVolumes
    {
        public sealed class Sector
        {
            public string Name { get; set; } = string.Empty;

            public List<Sector> SubSectors { get; } = new List<Sector>();

            public bool IsInSector(LatLong point, int buffer)
            {
                return true;
            }
        }
    }

    public class BaseForm : Form
    {
        public bool MiddleClickClose { get; set; }

        public bool HasMinimizeButton { get; set; }

        public bool HasSendBackButton { get; set; }

        public bool Resizeable { get; set; } = true;
    }

    public static class Performance
    {
        public static PerformanceData? GetPerformanceData(string aircraftType)
        {
            if (string.IsNullOrWhiteSpace(aircraftType))
            {
                return null;
            }

            var type = aircraftType.Split('/').First().Trim().ToUpperInvariant();
            return new PerformanceData
            {
                IsJet = IsJetType(type),
            };
        }

        private static bool IsJetType(string type)
        {
            return Regex.IsMatch(
                type,
                @"^(A(20|21|30|31|32|33|34|35|38|3ST)|B(3[789]|7\d{2}|CS)|CRJ|E(135|145|170|175|190|195|290|295)|F28|F70|F100|MD|DC9|GLF|CL(30|35|60)|C17|C5|C25|C56X|LJ|FA|H25|PRM|HDJT)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public sealed class PerformanceData
        {
            public bool IsJet { get; set; }
        }
    }

    public static class Audio
    {
        public static event EventHandler? VSCSFrequenciesChanged;

        public static List<VSCSFrequency> VSCSFrequencies { get; } = new List<VSCSFrequency>();

        internal static void RaiseVSCSFrequenciesChanged()
        {
            VSCSFrequenciesChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public sealed class VSCSFrequency
    {
        public event EventHandler? ReceivingChanged;

        public List<string> ReceivingCallsigns { get; } = new List<string>();

        public void RaiseReceivingChanged()
        {
            ReceivingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public static class Conversions
    {
        public static string FSDFrequencyToString(int frequency)
        {
            if (frequency <= 0)
            {
                return string.Empty;
            }

            if (frequency > 100000)
            {
                return (frequency / 1000.0).ToString("000.000", CultureInfo.InvariantCulture);
            }

            return (frequency / 100.0).ToString("000.000", CultureInfo.InvariantCulture);
        }

        public static double CalculateTrack(LatLong? first, LatLong? last)
        {
            if (first == null || last == null)
            {
                return 0;
            }

            var lat1 = DegreesToRadians(first.Latitude);
            var lat2 = DegreesToRadians(last.Latitude);
            var deltaLon = DegreesToRadians(last.Longitude - first.Longitude);
            var y = Math.Sin(deltaLon) * Math.Cos(lat2);
            var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(deltaLon);
            return (RadiansToDegrees(Math.Atan2(y, x)) + 360) % 360;
        }

        public static double CalculateDistance(LatLong? first, LatLong? last)
        {
            if (first == null || last == null)
            {
                return -1;
            }

            var lat1 = DegreesToRadians(first.Latitude);
            var lat2 = DegreesToRadians(last.Latitude);
            var deltaLat = DegreesToRadians(last.Latitude - first.Latitude);
            var deltaLon = DegreesToRadians(last.Longitude - first.Longitude);
            var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                    Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return 3440.065 * c;
        }

        private static double DegreesToRadians(double value)
        {
            return value * Math.PI / 180.0;
        }

        private static double RadiansToDegrees(double value)
        {
            return value * 180.0 / Math.PI;
        }
    }

    public static class LogicalPositions
    {
        public static List<Position> Positions { get; } = new List<Position>();

        public sealed class Position
        {
            public string Name { get; set; } = string.Empty;

            public int MagneticVariation { get; set; }
        }
    }

    public static class DisplayMaps
    {
        public enum MapTypes
        {
            Ground_RWY,
            Other,
        }

        public static List<DisplayMap> Maps { get; } = new List<DisplayMap>();

        public sealed class DisplayMap
        {
            public string Folder { get; set; } = string.Empty;

            public MapTypes Type { get; set; }

            public string Name { get; set; } = string.Empty;
        }
    }

    public static class CPDLC
    {
        public static List<MessageCategory> MessageCategories { get; } = new List<MessageCategory>
        {
            new MessageCategory { Name = "PDC" },
        };

        public sealed class MessageCategory
        {
            public string Name { get; set; } = string.Empty;
        }
    }

    public static class Errors
    {
        public static void Add(Exception exception, string source = "OzStrips")
        {
            try
            {
                var folder = Helpers.GetFilesFolder();
                File.AppendAllText(Path.Combine(folder, "ozstrips_euroscope.log"), DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " [" + source + "] " + exception + Environment.NewLine);
            }
            catch
            {
            }
        }
    }

    public static class Helpers
    {
        public static string GetFilesFolder()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OzStripsEuroScope");
            Directory.CreateDirectory(folder);
            return folder + Path.DirectorySeparatorChar;
        }
    }

    public static class Profile
    {
#pragma warning disable CS0414
        private static readonly string shortName = "EuroScope";
#pragma warning restore CS0414

        public static bool Loaded => false;
    }
}
