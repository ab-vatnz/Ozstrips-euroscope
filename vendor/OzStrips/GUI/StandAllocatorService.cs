using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaxRumsey.OzStripsPlugin.GUI.Shared;

namespace MaxRumsey.OzStripsPlugin.GUI;

#pragma warning disable CA1001 // Singleton service lives for the process lifetime.
internal sealed class StandAllocatorService
{
    private static readonly TimeSpan AirportCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ArrivalCacheDuration = TimeSpan.FromSeconds(20);
    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    private readonly StandAllocatorApiClient _client = new();
    private readonly SemaphoreSlim _airportLock = new(1, 1);
    private readonly Dictionary<string, ArrivalCacheEntry> _arrivalCache = new(KeyComparer);

    private DateTime _airportCacheExpiresUtc;
    private HashSet<string> _supportedAirports = new(KeyComparer);

    private StandAllocatorService()
    {
    }

    public static StandAllocatorService Instance { get; } = new();

    public async Task<string> GetStandForStripAsync(Strip strip)
    {
        var airport = strip.StandAllocatorAirport;
        if (string.IsNullOrWhiteSpace(airport) || !await IsSupportedAirportAsync(airport).ConfigureAwait(false))
        {
            return string.Empty;
        }

        try
        {
            if (strip.StripType == StripType.ARRIVAL)
            {
                var arrivals = await GetArrivalsAsync(airport).ConfigureAwait(false);
                var arrival = arrivals.FirstOrDefault(x => KeyComparer.Equals(x.Callsign, strip.FDR.Callsign));
                var arrivalStand = BestStand(arrival?.CurrentStand, arrival?.AssignedStand, arrival?.PhysicalStand);
                if (!string.IsNullOrWhiteSpace(arrivalStand))
                {
                    return arrivalStand;
                }
            }

            var options = await _client.GetStandOptionsAsync(GetApiBaseUrl(), strip.FDR.Callsign, airport).ConfigureAwait(false);
            var optionStand = BestStand(options.CurrentStand);
            if (!string.IsNullOrWhiteSpace(optionStand))
            {
                return optionStand;
            }

            var assignment = await _client.GetAssignmentAsync(GetApiBaseUrl(), strip.FDR.Callsign).ConfigureAwait(false);
            return KeyComparer.Equals(assignment?.Airport, airport) ? BestStand(assignment?.StandId) : string.Empty;
        }
        catch (Exception ex)
        {
            Util.LogError(ex, "OzStrips Stand Allocator");
            return strip.AllocatorStand;
        }
    }

    public async Task<IReadOnlyList<StandAllocatorStandOption>> GetStandOptionsAsync(Strip strip)
    {
        var airport = strip.StandAllocatorAirport;
        if (string.IsNullOrWhiteSpace(airport) || !await IsSupportedAirportAsync(airport).ConfigureAwait(false))
        {
            return [];
        }

        var response = await _client.GetStandOptionsAsync(GetApiBaseUrl(), strip.FDR.Callsign, airport).ConfigureAwait(false);
        var candidateOptions = response.PreferredStands.Count > 0 ? response.PreferredStands : response.AllStands;
        var options = new List<StandAllocatorStandOption>();

        if (!string.IsNullOrWhiteSpace(response.CurrentStand))
        {
            options.Add(new StandAllocatorStandOption
            {
                StandId = response.CurrentStand,
                Name = response.CurrentStand,
                DisplayText = response.CurrentStand,
            });
        }

        options.AddRange(candidateOptions);
        return options
            .Where(x => !string.IsNullOrWhiteSpace(x.StandId))
            .GroupBy(x => x.StandId.Trim(), KeyComparer)
            .Select(x => x.First())
            .ToArray();
    }

    public async Task<StandAllocatorReassignResponse> ReassignStandAsync(Strip strip, string standId)
    {
        var airport = strip.StandAllocatorAirport;
        if (string.IsNullOrWhiteSpace(airport))
        {
            throw new InvalidOperationException("This strip does not have a valid allocator airport.");
        }

        return await _client
            .ReassignStandAsync(GetApiBaseUrl(), strip.FDR.Callsign, standId, allowReallocate: true, airport)
            .ConfigureAwait(false);
    }

    private async Task<bool> IsSupportedAirportAsync(string airport)
    {
        if (DateTime.UtcNow < _airportCacheExpiresUtc)
        {
            return _supportedAirports.Contains(airport);
        }

        await _airportLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (DateTime.UtcNow < _airportCacheExpiresUtc)
            {
                return _supportedAirports.Contains(airport);
            }

            var response = await _client.GetAirportsAsync(GetApiBaseUrl()).ConfigureAwait(false);
            _supportedAirports = new HashSet<string>(response.Airports.Where(x => !string.IsNullOrWhiteSpace(x)), KeyComparer);
            _airportCacheExpiresUtc = DateTime.UtcNow.Add(AirportCacheDuration);
            return _supportedAirports.Contains(airport);
        }
        catch (Exception ex)
        {
            Util.LogError(ex, "OzStrips Stand Allocator");
            _airportCacheExpiresUtc = DateTime.UtcNow.Add(TimeSpan.FromSeconds(30));
            return _supportedAirports.Contains(airport);
        }
        finally
        {
            _airportLock.Release();
        }
    }

    private async Task<IReadOnlyList<StandAllocatorArrivalSummary>> GetArrivalsAsync(string airport)
    {
        lock (_arrivalCache)
        {
            if (_arrivalCache.TryGetValue(airport, out var entry) && DateTime.UtcNow < entry.ExpiresUtc)
            {
                return entry.Arrivals;
            }
        }

        var arrivals = await _client.GetArrivalsAsync(GetApiBaseUrl(), airport).ConfigureAwait(false);
        lock (_arrivalCache)
        {
            _arrivalCache[airport] = new ArrivalCacheEntry(DateTime.UtcNow.Add(ArrivalCacheDuration), arrivals);
        }

        return arrivals;
    }

    private static string BestStand(params string?[] standIds)
    {
        foreach (var standId in standIds)
        {
            if (!string.IsNullOrWhiteSpace(standId))
            {
                return standId.Trim();
            }
        }

        return string.Empty;
    }

    private static string GetApiBaseUrl() => StandAllocatorApiConfiguration.GetApiBaseUrl();

    private sealed class ArrivalCacheEntry
    {
        public ArrivalCacheEntry(DateTime expiresUtc, IReadOnlyList<StandAllocatorArrivalSummary> arrivals)
        {
            ExpiresUtc = expiresUtc;
            Arrivals = arrivals;
        }

        public DateTime ExpiresUtc { get; }

        public IReadOnlyList<StandAllocatorArrivalSummary> Arrivals { get; }
    }
}
#pragma warning restore CA1001
