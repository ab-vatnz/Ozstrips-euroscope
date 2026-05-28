using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MaxRumsey.OzStripsPlugin.GUI.Shared;
using Newtonsoft.Json.Linq;

namespace OzStripsEuroScope.Helper
{
    internal sealed class RouteCheckResult
    {
        public string CleanedRoute { get; set; } = string.Empty;

        public bool IsDodgy { get; set; }

        public string Error { get; set; } = string.Empty;

        public IReadOnlyList<RouteDTO> ValidRoutes { get; set; } = Array.Empty<RouteDTO>();
    }

    internal sealed class RouteService
    {
        private static readonly Regex SidRouteRegex = new Regex(@"^[\w\d]+\/[\d]{2}\w?", RegexOptions.Compiled);
        private static readonly Regex GpsCoordRegex = new Regex(@"^[\d]+\w[\d]+\w", RegexOptions.Compiled);
        private static readonly Regex AirwayRegex = new Regex(@"^\w\d+", RegexOptions.Compiled);

        private readonly HttpClient _httpClient = new HttpClient();
        private readonly ConcurrentDictionary<string, Task<IReadOnlyList<RouteDTO>>> _routeCache =
            new ConcurrentDictionary<string, Task<IReadOnlyList<RouteDTO>>>(StringComparer.OrdinalIgnoreCase);

        public RouteService()
        {
            _httpClient.BaseAddress = new Uri("https://ozstripsserver.maxrumsey.xyz/");
            _httpClient.Timeout = TimeSpan.FromSeconds(8);
        }

        public Task<IReadOnlyList<RouteDTO>> GetRoutesAsync(string adep, string ades)
        {
            var key = (adep + "/" + ades).ToUpperInvariant();
            return _routeCache.GetOrAdd(key, _ => FetchRoutesAsync(adep, ades));
        }

        public async Task<RouteCheckResult> CheckRouteAsync(FlightPlanSnapshot flight)
        {
            var result = new RouteCheckResult
            {
                CleanedRoute = CleanRoute(flight.Route),
            };

            if (string.IsNullOrWhiteSpace(flight.Adep) ||
                string.IsNullOrWhiteSpace(flight.Ades) ||
                string.IsNullOrWhiteSpace(flight.Route))
            {
                return result;
            }

            try
            {
                var validRoutes = await GetRoutesAsync(flight.Adep, flight.Ades).ConfigureAwait(false);
                result.ValidRoutes = validRoutes;

                if (validRoutes.Count == 0)
                {
                    return result;
                }

                result.IsDodgy = true;
                foreach (var route in validRoutes)
                {
                    if (RouteMatchesStandard(result.CleanedRoute, route.RouteText))
                    {
                        result.IsDodgy = false;
                        break;
                    }
                }

                if (result.IsDodgy)
                {
                    var joinedFromSecondElement = string.Join(
                        " ",
                        CleanRoute(flight.Route).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Skip(1));

                    foreach (var route in validRoutes)
                    {
                        if (RouteMatchesStandard(joinedFromSecondElement, route.RouteText))
                        {
                            result.IsDodgy = false;
                            break;
                        }
                    }
                }

                if (!result.IsDodgy && result.CleanedRoute == "\0")
                {
                    result.IsDodgy = true;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        public static string CleanRoute(string rawRoute)
        {
            try
            {
                var rawRouteArr = (rawRoute ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var routeArr = new List<string>();

                foreach (var routeElement in rawRouteArr)
                {
                    if (GpsCoordRegex.Match(routeElement).Success)
                    {
                        continue;
                    }

                    if (routeElement.Contains("/"))
                    {
                        if (!SidRouteRegex.Match(routeElement).Success)
                        {
                            routeArr.Add(routeElement.Split('/').First());
                        }
                    }
                    else if (!string.Equals(routeElement, "DCT", StringComparison.OrdinalIgnoreCase))
                    {
                        if (routeElement.Any(char.IsDigit) && !AirwayRegex.IsMatch(routeElement))
                        {
                            continue;
                        }

                        routeArr.Add(routeElement);
                    }
                }

                if (routeArr.Count < 3)
                {
                    return "\0";
                }

                if (!routeArr.First().Any(char.IsDigit))
                {
                    routeArr.RemoveAt(0);
                }

                if (!routeArr.Last().Any(char.IsDigit))
                {
                    routeArr.RemoveAt(routeArr.Count - 1);
                }

                var finalArr = new List<string>();
                for (var i = 0; i < routeArr.Count; i++)
                {
                    var element = routeArr[i];

                    if (i + 2 < routeArr.Count && element == routeArr[i + 2])
                    {
                        i++;
                        continue;
                    }

                    finalArr.Add(element);
                }

                return string.Join(" ", finalArr);
            }
            catch
            {
                return "\0";
            }
        }

        private static bool RouteMatchesStandard(string filedRoute, string standardRoute)
        {
            if (string.IsNullOrWhiteSpace(filedRoute) || filedRoute == "\0" || filedRoute == "FAIL")
            {
                return false;
            }

            var filedTokens = RouteTokens(filedRoute);
            var standardTokens = RouteTokens(standardRoute);

            return filedTokens.Length > 0 &&
                standardTokens.Length > 0 &&
                (ContainsTokenSequence(standardTokens, filedTokens) || ContainsTokenSequence(filedTokens, standardTokens));
        }

        private static string[] RouteTokens(string route)
        {
            return (route ?? string.Empty)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormaliseRouteToken)
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .ToArray();
        }

        private static string NormaliseRouteToken(string routeElement)
        {
            routeElement = (routeElement ?? string.Empty).Trim().Trim(',', ';').ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(routeElement) ||
                routeElement == "DCT" ||
                GpsCoordRegex.Match(routeElement).Success ||
                SidRouteRegex.Match(routeElement).Success)
            {
                return string.Empty;
            }

            return routeElement.Contains("/") ? routeElement.Split('/').First() : routeElement;
        }

        private static bool ContainsTokenSequence(string[] haystack, string[] needle)
        {
            if (needle.Length == 0 || haystack.Length < needle.Length)
            {
                return false;
            }

            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                var matched = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (!string.Equals(haystack[i + j], needle[j], StringComparison.OrdinalIgnoreCase))
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<IReadOnlyList<RouteDTO>> FetchRoutesAsync(string adep, string ades)
        {
            var relative = "ozstrips/route/" + Uri.EscapeDataString(adep.ToUpperInvariant()) + "/" +
                           Uri.EscapeDataString(ades.ToUpperInvariant());
            var json = await _httpClient.GetStringAsync(relative).ConfigureAwait(false);
            var token = JToken.Parse(json);
            var routesToken = token.Type == JTokenType.Array ? token : token["value"];

            if (routesToken == null)
            {
                return Array.Empty<RouteDTO>();
            }

            return routesToken
                .Select(item => new RouteDTO
                {
                    AircraftType = item.Value<string>("acft") ?? string.Empty,
                    RouteText = item.Value<string>("route") ?? string.Empty,
                })
                .Where(route => !string.IsNullOrWhiteSpace(route.RouteText))
                .ToArray();
        }
    }
}
