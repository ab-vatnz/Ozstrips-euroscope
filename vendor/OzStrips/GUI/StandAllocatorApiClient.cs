using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MaxRumsey.OzStripsPlugin.GUI;

#pragma warning disable CA1822 // Instance methods keep the API client mockable and consistent with existing plugin code.
internal sealed class StandAllocatorApiClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    public async Task<List<StandAllocatorArrivalSummary>> GetArrivalsAsync(string apiBaseUrl, string airport)
    {
        var response = await HttpClient
            .GetAsync(BuildUrl(apiBaseUrl, "/arrivals?airport=" + Uri.EscapeDataString(airport ?? string.Empty)))
            .ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        EnsureSuccess(response, content);

        return JsonConvert.DeserializeObject<List<StandAllocatorArrivalSummary>>(content) ?? [];
    }

    public async Task<StandAllocatorAirportListResponse> GetAirportsAsync(string apiBaseUrl)
    {
        var response = await HttpClient
            .GetAsync(BuildUrl(apiBaseUrl, "/airports"))
            .ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        EnsureSuccess(response, content);

        return JsonConvert.DeserializeObject<StandAllocatorAirportListResponse>(content) ?? new StandAllocatorAirportListResponse();
    }

    public async Task<StandAllocatorStandOptionsResponse> GetStandOptionsAsync(string apiBaseUrl, string callsign, string airport)
    {
        var encodedCallsign = Uri.EscapeDataString(callsign ?? string.Empty);
        var encodedAirport = Uri.EscapeDataString(airport ?? string.Empty);
        var response = await HttpClient
            .GetAsync(BuildUrl(apiBaseUrl, "/arrivals/" + encodedCallsign + "/stand-options?airport=" + encodedAirport))
            .ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        EnsureSuccess(response, content);

        return JsonConvert.DeserializeObject<StandAllocatorStandOptionsResponse>(content) ?? new StandAllocatorStandOptionsResponse();
    }

    public async Task<StandAllocatorAssignmentResponse?> GetAssignmentAsync(string apiBaseUrl, string callsign)
    {
        var encodedCallsign = Uri.EscapeDataString(callsign ?? string.Empty);
        var response = await HttpClient
            .GetAsync(BuildUrl(apiBaseUrl, "/assignments/" + encodedCallsign))
            .ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccess(response, content);

        return JsonConvert.DeserializeObject<StandAllocatorAssignmentResponse>(content);
    }

    public async Task<StandAllocatorReassignResponse> ReassignStandAsync(
        string apiBaseUrl,
        string callsign,
        string standId,
        bool allowReallocate,
        string airport)
    {
        var payload = new StandAllocatorReassignRequest
        {
            Callsign = callsign,
            StandId = standId,
            AllowReallocate = allowReallocate,
            Airport = airport,
        };
        var json = JsonConvert.SerializeObject(payload);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await HttpClient
            .PostAsync(BuildUrl(apiBaseUrl, "/assignments/reallocate"), content)
            .ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        EnsureSuccess(response, responseBody);

        return JsonConvert.DeserializeObject<StandAllocatorReassignResponse>(responseBody) ?? new StandAllocatorReassignResponse();
    }

    private static void EnsureSuccess(HttpResponseMessage response, string content)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw new InvalidOperationException(ExtractErrorMessage(content));
    }

    private static string ExtractErrorMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "The stand allocator API request failed.";
        }

        try
        {
            var body = JObject.Parse(content);
            var detail = body["detail"];

            if (detail == null)
            {
                return content;
            }

            if (detail.Type == JTokenType.String)
            {
                return detail.ToString();
            }

            var message = detail["message"];
            return message != null ? message.ToString() : detail.ToString(Formatting.None);
        }
        catch (JsonException)
        {
            return content;
        }
    }

    private static string BuildUrl(string apiBaseUrl, string relativePath)
    {
        var trimmedBaseUrl = (apiBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmedBaseUrl))
        {
            trimmedBaseUrl = StandAllocatorApiConfiguration.GetApiBaseUrl();
        }

        return trimmedBaseUrl + relativePath;
    }
}
#pragma warning restore CA1822
