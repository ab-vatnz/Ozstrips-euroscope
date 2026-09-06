using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MaxRumsey.OzStripsPlugin.GUI;

/// <summary>
/// Loads procedure legs from the active client data so strip route cells can show SID and STAR waypoints.
/// </summary>
internal static class ProcedureRouteProvider
{
    private static readonly object SyncRoot = new();
    private static readonly List<ProcedureRecord> Procedures = [];
    private static bool _loaded;

    public static IReadOnlyList<string> GetWaypoints(string airport, string procedure, string runway, string route, bool arrival)
    {
        if (string.IsNullOrWhiteSpace(airport) || string.IsNullOrWhiteSpace(procedure))
        {
            return Array.Empty<string>();
        }

        EnsureLoaded();
        var routeTokens = Tokenise(route);
        var candidates = Procedures
            .Where(x => x.Arrival == arrival &&
                string.Equals(x.Airport, airport, StringComparison.OrdinalIgnoreCase) &&
                NamesMatch(x.Name, procedure))
            .OrderByDescending(x => string.Equals(x.Name, procedure, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => string.Equals(x.Runway, runway, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(x => x.Score(routeTokens))
            .ToList();

        return candidates.Count == 0 ? Array.Empty<string>() : candidates[0].BuildWaypoints(routeTokens);
    }

    private static void EnsureLoaded()
    {
        lock (SyncRoot)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            foreach (var path in GetAirspacePaths())
            {
                LoadAirspace(path);
            }

            foreach (var path in GetSectorPaths())
            {
                LoadSector(path);
            }
        }
    }

    private static IEnumerable<string> GetAirspacePaths()
    {
        var pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var profileDirectory = Directory.GetParent(pluginDirectory ?? string.Empty)?.Parent?.FullName;
        var airspace = string.IsNullOrWhiteSpace(profileDirectory) ? string.Empty : Path.Combine(profileDirectory, "Airspace.xml");
        return File.Exists(airspace) ? [airspace] : [];
    }

    private static IEnumerable<string> GetSectorPaths()
    {
        var candidates = new List<string>();
        foreach (var root in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "EuroScope"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EuroScope"),
            AppDomain.CurrentDomain.BaseDirectory,
        })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                candidates.AddRange(Directory.EnumerateFiles(root, "*.ese", SearchOption.AllDirectories)
                    .Where(x => x.IndexOf("VATNZ", StringComparison.OrdinalIgnoreCase) >= 0));
            }
            catch
            {
                // A missing or protected EuroScope directory simply means VATSys profile data remains the source.
            }
        }

        return candidates.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void LoadAirspace(string path)
    {
        try
        {
            var document = XDocument.Load(path);
            foreach (var procedure in document.Descendants().Where(x => x.Name.LocalName is "SID" or "STAR"))
            {
                var airport = Attribute(procedure, "Airport");
                var name = Attribute(procedure, "Name");
                if (airport.Length != 4 || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var transitions = procedure.Elements().Where(x => x.Name.LocalName == "Transition")
                    .Select(x => new TransitionRecord(Attribute(x, "Name"), Tokenise(x.Value)))
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .ToList();

                foreach (var route in procedure.Elements().Where(x => x.Name.LocalName == "Route"))
                {
                    Procedures.Add(new ProcedureRecord(
                        airport,
                        name,
                        Attribute(route, "Runway"),
                        procedure.Name.LocalName == "STAR",
                        Tokenise(route.Value),
                        transitions));
                }
            }
        }
        catch
        {
            // A damaged profile file should not prevent strips using filed route waypoints.
        }
    }

    private static void LoadSector(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var parts = (line ?? string.Empty).Trim().Split(':');
                if (parts.Length < 5 || (!string.Equals(parts[0], "SID", StringComparison.OrdinalIgnoreCase) && !string.Equals(parts[0], "STAR", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var legs = Tokenise(string.Join(" ", parts.Skip(4)));
                if (parts[1].Trim().Length != 4 || string.IsNullOrWhiteSpace(parts[3]) || legs.Count == 0)
                {
                    continue;
                }

                Procedures.Add(new ProcedureRecord(parts[1], parts[3], parts[2], string.Equals(parts[0], "STAR", StringComparison.OrdinalIgnoreCase), legs, []));
            }
        }
        catch
        {
            // EuroScope can lock sector files during profile changes; use the filed route until it is available again.
        }
    }

    private static string Attribute(XElement element, string name)
    {
        return (element.Attribute(name)?.Value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static List<string> Tokenise(string value)
    {
        return (value ?? string.Empty)
            .Split(new[] { '/', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToUpperInvariant())
            .Where(x => Regex.IsMatch(x, @"^[A-Z0-9]{2,8}$") && x is not "VOR" and not "NDB" and not "DME")
            .ToList();
    }

    private static bool NamesMatch(string available, string selected)
    {
        return string.Equals(available, selected, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ShortName(available), ShortName(selected), StringComparison.OrdinalIgnoreCase);
    }

    private static string ShortName(string value)
    {
        var match = Regex.Match((value ?? string.Empty).Trim().ToUpperInvariant(), @"^([A-Z]{4})[A-Z]?(\d[A-Z])");
        return match.Success ? match.Groups[1].Value + match.Groups[2].Value : value ?? string.Empty;
    }

    private sealed class ProcedureRecord(string airport, string name, string runway, bool arrival, List<string> route, List<TransitionRecord> transitions)
    {
        public string Airport { get; } = (airport ?? string.Empty).Trim().ToUpperInvariant();

        public string Name { get; } = (name ?? string.Empty).Trim().ToUpperInvariant();

        public string Runway { get; } = (runway ?? string.Empty).Trim().ToUpperInvariant();

        public bool Arrival { get; } = arrival;

        public List<string> Route { get; } = route;

        public List<TransitionRecord> Transitions { get; } = transitions;

        public int Score(IReadOnlyCollection<string> routeTokens)
        {
            return Route.Count(x => routeTokens.Contains(x, StringComparer.OrdinalIgnoreCase)) +
                Transitions.Sum(x => routeTokens.Contains(x.Name, StringComparer.OrdinalIgnoreCase) ? 10 : 0);
        }

        public List<string> BuildWaypoints(IReadOnlyCollection<string> routeTokens)
        {
            var transition = Transitions
                .OrderByDescending(x => routeTokens.Contains(x.Name, StringComparer.OrdinalIgnoreCase))
                .FirstOrDefault(x => routeTokens.Contains(x.Name, StringComparer.OrdinalIgnoreCase));
            var result = Arrival
                ? (transition?.Waypoints ?? []).Concat(Route)
                : Route.Concat(transition?.Waypoints ?? []);
            return result.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }
    }

    private sealed class TransitionRecord(string name, List<string> waypoints)
    {
        public string Name { get; } = (name ?? string.Empty).Trim().ToUpperInvariant();

        public List<string> Waypoints { get; } = waypoints;
    }
}
