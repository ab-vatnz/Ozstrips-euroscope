using System;
using System.Threading.Tasks;
using MaxRumsey.OzStripsPlugin.GUI.Shared;
using Microsoft.AspNetCore.SignalR.Client;
using static MaxRumsey.OzStripsPlugin.GUI.Shared.ConnectionMetadataDTO;

namespace OzStripsEuroScope.Helper
{
    internal sealed class OzStripsServerClient : IDisposable
    {
        private readonly HubConnection _connection;
        private string _aerodrome = string.Empty;
        private Servers _server = Servers.VATSIM;

        public OzStripsServerClient()
        {
            _connection = new HubConnectionBuilder()
                .WithUrl("https://ozstripsserver.maxrumsey.xyz/ozstrips/hub/v2")
                .WithAutomaticReconnect()
                .Build();

            RegisterListeners();
        }

        public event EventHandler<string>? StatusChanged;

        public event EventHandler<StripDTO>? StripUpdated;

        public event EventHandler<StripDTO[]>? StripCacheReceived;

        public event EventHandler<BayDTO>? BayUpdated;

        public event EventHandler<AerodromeState>? AerodromeStateUpdated;

        public event EventHandler<string>? MessageReceived;

        public bool IsConnected => _connection.State == HubConnectionState.Connected;

        public async Task SubscribeAsync(string aerodrome, string callsign)
        {
            _aerodrome = aerodrome.ToUpperInvariant();
            _server = Servers.VATSIM;

            if (_connection.State == HubConnectionState.Disconnected)
            {
                StatusChanged?.Invoke(this, "Connecting OzStrips server");
                await _connection.StartAsync().ConfigureAwait(false);
            }

            var metadata = new ConnectionMetadataDTO
            {
                Version = "0.9.1-euroscope",
                APIVersion = "2",
                Server = _server,
                AerodromeName = _aerodrome,
                Callsign = string.IsNullOrWhiteSpace(callsign) ? "EUROSCOPE" : callsign,
            };

            var response = await _connection
                .InvokeAsync<AerodromeSubscriptionResponse>("SubscribeToAerodrome", metadata)
                .ConfigureAwait(false);

            if (response.Error != null)
            {
                throw response.Error;
            }

            if (!string.Equals(response.AerodromeICAO, _aerodrome, StringComparison.OrdinalIgnoreCase) ||
                response.Server != _server)
            {
                throw new InvalidOperationException("OzStrips server returned a different aerodrome/server subscription.");
            }

            StripCacheReceived?.Invoke(this, response.StripCache ?? Array.Empty<StripDTO>());
            StatusChanged?.Invoke(this, "OzStrips server connected");
        }

        public Task SyncStripAsync(StripDTO strip)
        {
            return IsConnected
                ? _connection.InvokeAsync("StripChange", strip, MessageMetadata())
                : Task.CompletedTask;
        }

        public Task<StripDTO?> RequestStripAsync(StripKey key)
        {
            return IsConnected
                ? _connection.InvokeAsync<StripDTO?>("RequestStrip", key, MessageMetadata())
                : Task.FromResult<StripDTO?>(null);
        }

        public Task SendPdcAsync(StripDTO strip, string text)
        {
            return IsConnected
                ? _connection.InvokeAsync("SendPDC", strip, text, MessageMetadata())
                : Task.CompletedTask;
        }

        public void Dispose()
        {
            try
            {
                _connection.StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
            }

            _connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        private void RegisterListeners()
        {
            _connection.On<MessageMetadata, StripDTO>("StripUpdate", (metadata, strip) =>
            {
                if (Matches(metadata) && strip != null)
                {
                    StripUpdated?.Invoke(this, strip);
                }
            });

            _connection.On<MessageMetadata, StripDTO[]>("StripCache", (metadata, cache) =>
            {
                if (Matches(metadata))
                {
                    StripCacheReceived?.Invoke(this, cache ?? Array.Empty<StripDTO>());
                }
            });

            _connection.On<MessageMetadata, BayDTO>("BayUpdate", (metadata, bay) =>
            {
                if (Matches(metadata) && bay != null)
                {
                    BayUpdated?.Invoke(this, bay);
                }
            });

            _connection.On<MessageMetadata, AerodromeState>("AerodromeStateUpdate", (metadata, state) =>
            {
                if (Matches(metadata) && state != null)
                {
                    AerodromeStateUpdated?.Invoke(this, state);
                }
            });

            _connection.On<MessageMetadata, string>("Message", (metadata, message) =>
            {
                if (Matches(metadata) && !string.IsNullOrWhiteSpace(message))
                {
                    MessageReceived?.Invoke(this, message);
                }
            });

            _connection.On("OutOfSync", () => StatusChanged?.Invoke(this, "OzStrips server out of sync"));
            _connection.Reconnecting += error =>
            {
                StatusChanged?.Invoke(this, "OzStrips server reconnecting");
                return Task.CompletedTask;
            };
            _connection.Reconnected += id =>
            {
                StatusChanged?.Invoke(this, "OzStrips server reconnected");
                return Task.CompletedTask;
            };
            _connection.Closed += error =>
            {
                StatusChanged?.Invoke(this, "OzStrips server disconnected");
                return Task.CompletedTask;
            };
        }

        private MessageMetadata MessageMetadata()
        {
            return new MessageMetadata(_aerodrome, _server);
        }

        private bool Matches(MessageMetadata metadata)
        {
            return metadata != null &&
                   string.Equals(metadata.AerodromeICAO, _aerodrome, StringComparison.OrdinalIgnoreCase) &&
                   metadata.Server == _server;
        }
    }
}
