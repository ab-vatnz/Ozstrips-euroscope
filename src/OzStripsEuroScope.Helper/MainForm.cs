using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaxRumsey.OzStripsPlugin.GUI.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OzStripsEuroScope.Helper
{
    internal sealed class MainForm : Form
    {
        private readonly StartupOptions _options;
        private readonly IpcClient _ipcClient;
        private readonly OzStripsServerClient _serverClient = new OzStripsServerClient();
        private readonly RouteService _routeService = new RouteService();
        private readonly HelperConfig _config = ConfigLoader.Load();
        private readonly StripLayout _stripLayout = StripLayout.Load();
        private readonly Dictionary<string, FlightPlanSnapshot> _flights =
            new Dictionary<string, FlightPlanSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RouteCheckResult> _routeChecks =
            new Dictionary<string, RouteCheckResult>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pendingRouteChecks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly ToolTip _toolTip = new ToolTip();

        private ToolStripTextBox _aerodromeBox = null!;
        private ToolStripLabel _statusLabel = null!;
        private ToolStripLabel _serverStatusLabel = null!;
        private ToolStripLabel _controllerLabel = null!;
        private ToolStripLabel _configLabel = null!;
        private TableLayoutPanel _board = null!;
        private readonly List<FlowLayoutPanel> _bayPanels = new List<FlowLayoutPanel>();
        private string _controllerCallsign = string.Empty;

        public MainForm(StartupOptions options)
        {
            _options = options;
            _ipcClient = new IpcClient(options.PipeName);
            BuildUi();

            _ipcClient.LineReceived += IpcLineReceived;
            _ipcClient.StatusChanged += (_, status) => SafeUi(() => _statusLabel.Text = status);
            _ipcClient.Start();

            _serverClient.StatusChanged += (_, status) => SafeUi(() => _serverStatusLabel.Text = "Server: " + status);
            _serverClient.StripCacheReceived += (_, cache) => SafeUi(() => _serverStatusLabel.Text = "Server: cache " + cache.Length);
            _serverClient.StripUpdated += (_, strip) => SafeUi(() => _serverStatusLabel.Text = "Server: strip " + strip.StripKey.Callsign);
            _serverClient.MessageReceived += (_, message) => SafeUi(() => MessageBox.Show(this, message, "OzStrips", MessageBoxButtons.OK, MessageBoxIcon.Information));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _ipcClient.Dispose();
                _serverClient.Dispose();
                _toolTip.Dispose();
            }

            base.Dispose(disposing);
        }

        private void BuildUi()
        {
            Text = "OzStrips :: EuroScope";
            MinimumSize = new Size(980, 560);
            Size = new Size(1320, 760);
            BackColor = Color.FromArgb(245, 245, 245);

            var strip = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Stretch = true,
                BackColor = Color.FromArgb(238, 238, 238),
            };

            _aerodromeBox = new ToolStripTextBox
            {
                Text = "NZAA",
                AutoSize = false,
                Width = 72,
                CharacterCasing = CharacterCasing.Upper,
            };
            _aerodromeBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    RefreshBoard();
                }
            };

            var refreshButton = new ToolStripButton("Refresh")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
            };
            refreshButton.Click += (_, _) =>
            {
                _routeChecks.Clear();
                RefreshBoard();
            };

            var connectServerButton = new ToolStripButton("Connect Server")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
            };
            connectServerButton.Click += async (_, _) => await ConnectServerAsync().ConfigureAwait(true);

            _statusLabel = new ToolStripLabel("Starting");
            _serverStatusLabel = new ToolStripLabel("Server: disconnected");
            _controllerLabel = new ToolStripLabel("Controller: -");
            _configLabel = new ToolStripLabel(_config.UseNose ? "NOSE rules" : "Standard alt rules");

            strip.Items.Add(new ToolStripLabel("Aerodrome"));
            strip.Items.Add(_aerodromeBox);
            strip.Items.Add(refreshButton);
            strip.Items.Add(connectServerButton);
            strip.Items.Add(new ToolStripSeparator());
            strip.Items.Add(_statusLabel);
            strip.Items.Add(new ToolStripSeparator());
            strip.Items.Add(_serverStatusLabel);
            strip.Items.Add(new ToolStripSeparator());
            strip.Items.Add(_controllerLabel);
            strip.Items.Add(new ToolStripSeparator());
            strip.Items.Add(_configLabel);

            _board = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(6),
                BackColor = Color.FromArgb(218, 218, 218),
            };

            for (var i = 0; i < 4; i++)
            {
                _board.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            }

            Controls.Add(_board);
            Controls.Add(strip);
            strip.Dock = DockStyle.Top;

            AddBayColumn("Preactive", 0);
            AddBayColumn("Cleared", 1);
            AddBayColumn("Taxi / Runway", 2);
            AddBayColumn("Arrivals", 3);
        }

        private void AddBayColumn(string title, int column)
        {
            var wrapper = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Margin = new Padding(4),
                BackColor = Color.White,
            };
            wrapper.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            wrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var header = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(44, 44, 44),
                ForeColor = Color.White,
                Font = new Font(Font, FontStyle.Bold),
            };

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(4),
                BackColor = Color.FromArgb(250, 250, 250),
            };

            wrapper.Controls.Add(header, 0, 0);
            wrapper.Controls.Add(panel, 0, 1);
            _board.Controls.Add(wrapper, column, 0);
            _bayPanels.Add(panel);
        }

        private void IpcLineReceived(object? sender, string line)
        {
            SafeUi(() => HandleBridgeLine(line));
        }

        private void HandleBridgeLine(string line)
        {
            var token = JObject.Parse(line);
            var type = token.Value<string>("type") ?? string.Empty;

            if (type == "focus")
            {
                WindowState = FormWindowState.Normal;
                Show();
                Activate();
                return;
            }

            if (type == "snapshot")
            {
                var snapshot = token.ToObject<SnapshotMessage>();
                if (snapshot == null)
                {
                    return;
                }

                _controllerCallsign = snapshot.Controller.Callsign ?? string.Empty;
                _controllerLabel.Text = "Controller: " + (string.IsNullOrWhiteSpace(snapshot.Controller.Callsign)
                    ? "-"
                    : snapshot.Controller.Callsign);
                _flights.Clear();

                foreach (var flight in snapshot.Flights.Where(f => !string.IsNullOrWhiteSpace(f.Callsign)))
                {
                    _flights[flight.Callsign] = flight;
                }

                RefreshBoard();
                return;
            }

            if (type == "flightChanged")
            {
                var changed = token.ToObject<FlightChangedMessage>();
                if (changed?.Flight == null || string.IsNullOrWhiteSpace(changed.Flight.Callsign))
                {
                    return;
                }

                _flights[changed.Flight.Callsign] = changed.Flight;
                _routeChecks.Remove(changed.Flight.Callsign);
                RefreshBoard();
            }
        }

        private void RefreshBoard()
        {
            foreach (var panel in _bayPanels)
            {
                panel.SuspendLayout();
                panel.Controls.Clear();
            }

            var aerodrome = _aerodromeBox.Text.Trim().ToUpperInvariant();
            var relevant = _flights.Values
                .Where(f => IsRelevantToAerodrome(f, aerodrome))
                .OrderBy(f => BayIndex(f, aerodrome))
                .ThenBy(f => f.Callsign)
                .ToList();

            foreach (var flight in relevant)
            {
                var bay = BayIndex(flight, aerodrome);
                _bayPanels[bay].Controls.Add(CreateStripPanel(flight, aerodrome));
                EnsureRouteCheck(flight, aerodrome);
            }

            foreach (var panel in _bayPanels)
            {
                panel.ResumeLayout();
            }

            _statusLabel.Text = _ipcClient.IsConnected
                ? "Connected - " + relevant.Count + " strips"
                : _statusLabel.Text;
        }

        private static bool IsRelevantToAerodrome(FlightPlanSnapshot flight, string aerodrome)
        {
            return string.Equals(flight.Adep, aerodrome, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(flight.Ades, aerodrome, StringComparison.OrdinalIgnoreCase);
        }

        private static int BayIndex(FlightPlanSnapshot flight, string aerodrome)
        {
            var arrival = string.Equals(flight.Ades, aerodrome, StringComparison.OrdinalIgnoreCase) &&
                          !string.Equals(flight.Adep, aerodrome, StringComparison.OrdinalIgnoreCase);

            if (arrival)
            {
                return 3;
            }

            var state = (flight.GroundState ?? string.Empty).ToUpperInvariant();
            if (state.Contains("TAXI") || state.Contains("DEPA"))
            {
                return 2;
            }

            if (!string.IsNullOrWhiteSpace(flight.Sid) ||
                !string.IsNullOrWhiteSpace(flight.Runway) ||
                !string.IsNullOrWhiteSpace(flight.Squawk))
            {
                return 1;
            }

            return 0;
        }

        private Panel CreateStripPanel(FlightPlanSnapshot flight, string aerodrome)
        {
            var routeResult = _routeChecks.TryGetValue(flight.Callsign, out var check) ? check : null;
            var isArrival = string.Equals(flight.Ades, aerodrome, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(flight.Adep, aerodrome, StringComparison.OrdinalIgnoreCase);
            var isLocal = string.Equals(flight.Ades, aerodrome, StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(flight.Adep, aerodrome, StringComparison.OrdinalIgnoreCase);

            var panel = new Panel
            {
                Width = _stripLayout.Width + 12,
                Height = _stripLayout.Height + 12,
                Margin = new Padding(2, 2, 2, 5),
                BackColor = isLocal ? Color.FromArgb(230, 174, 221) : isArrival ? Color.FromArgb(255, 255, 160) : Color.FromArgb(193, 230, 242),
                BorderStyle = BorderStyle.FixedSingle,
                Tag = flight,
            };

            if (flight.Selected)
            {
                panel.BackColor = Color.Silver;
            }

            foreach (var element in _stripLayout.Elements)
            {
                var label = AddCell(
                    panel,
                    GetElementText(element.Value, flight),
                    element.X,
                    element.Y,
                    element.W,
                    element.H,
                    element.FontSize,
                    ContentAlignment.MiddleCenter,
                    GetElementBackColor(element.Value, routeResult));

                if (string.Equals(element.Value, "FIRST_WPT", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(element.Value, "ROUTE", StringComparison.OrdinalIgnoreCase))
                {
                    _toolTip.SetToolTip(label, BuildRouteTooltip(flight, routeResult));
                }
            }

            var menu = new ContextMenuStrip();
            menu.Items.Add("Set scratchpad", null, (_, _) => EditScratchpad(flight));
            menu.Items.Add("Set CFL", null, (_, _) => EditCfl(flight));
            menu.Items.Add("Set squawk", null, (_, _) => EditSquawk(flight));
            menu.Items.Add("Show message in EuroScope", null, (_, _) =>
                _ipcClient.SendCommand("ShowMessage", flight.Callsign, flight.Callsign + " " + flight.Route));
            panel.ContextMenuStrip = menu;

            return panel;
        }

        private static string GetElementText(string value, FlightPlanSnapshot flight)
        {
            switch ((value ?? string.Empty).ToUpperInvariant())
            {
                case "ACID":
                    return flight.Callsign;
                case "TYPE":
                    return flight.AircraftType;
                case "WTC":
                    return flight.WakeCategory;
                case "ADEP":
                    return flight.Adep;
                case "ADES":
                    return flight.Ades;
                case "RWY":
                    return flight.Runway;
                case "RFL":
                    return flight.Rfl;
                case "CFL":
                    return flight.Cfl;
                case "SSR":
                    return string.IsNullOrWhiteSpace(flight.Squawk) ? "XXXX" : flight.Squawk;
                case "GLOP":
                case "HDG":
                    return flight.Scratchpad;
                case "SID":
                    return flight.Sid;
                case "FIRST_WPT":
                    return Truncate(!string.IsNullOrWhiteSpace(flight.FirstWaypoint) ? flight.FirstWaypoint : FirstRouteElement(flight.Route), 8);
                case "FRUL":
                    return string.IsNullOrWhiteSpace(flight.FlightRules) ? "I" : flight.FlightRules;
                case "ROUTE":
                    return string.IsNullOrWhiteSpace(flight.Route) ? string.Empty : "R";
                case "STAND":
                case "PDC_INDICATOR":
                case "TOT":
                case "READY":
                case "CLX":
                case "REMARK":
                case "DEPFREQ":
                case "DEP_CHANGED":
                case "NONE":
                    return string.Empty;
                default:
                    return string.Empty;
            }
        }

        private static Color? GetElementBackColor(string value, RouteCheckResult? routeResult)
        {
            switch ((value ?? string.Empty).ToUpperInvariant())
            {
                case "FIRST_WPT":
                case "ROUTE":
                    return routeResult?.IsDodgy == true ? Color.Orange : (Color?)null;
                default:
                    return null;
            }
        }

        private Label AddCell(
            Control parent,
            string text,
            int x,
            int y,
            int width,
            int height,
            float fontSize,
            ContentAlignment alignment,
            Color? backColor)
        {
            var label = new Label
            {
                Text = text,
                Bounds = new Rectangle(x + 6, y + 6, width, height),
                TextAlign = alignment,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = backColor ?? Color.Transparent,
                ForeColor = Color.Black,
                Font = new Font("Segoe UI", fontSize, FontStyle.Bold),
                AutoEllipsis = true,
            };

            parent.Controls.Add(label);
            return label;
        }

        private async void EnsureRouteCheck(FlightPlanSnapshot flight, string aerodrome)
        {
            if (!string.Equals(flight.Adep, aerodrome, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(flight.Route) ||
                _routeChecks.ContainsKey(flight.Callsign) ||
                _pendingRouteChecks.Contains(flight.Callsign))
            {
                return;
            }

            _pendingRouteChecks.Add(flight.Callsign);
            try
            {
                var check = await _routeService.CheckRouteAsync(flight).ConfigureAwait(true);
                _routeChecks[flight.Callsign] = check;
            }
            finally
            {
                _pendingRouteChecks.Remove(flight.Callsign);
                RefreshBoard();
            }
        }

        private void EditScratchpad(FlightPlanSnapshot flight)
        {
            var value = PromptDialog.Show("OzStrips :: " + flight.Callsign, "Scratchpad / GLOP", flight.Scratchpad);
            if (value != null)
            {
                _ipcClient.SendCommand("SetScratchpad", flight.Callsign, value);
            }
        }

        private void EditCfl(FlightPlanSnapshot flight)
        {
            var value = PromptDialog.Show("OzStrips :: " + flight.Callsign, "CFL in feet", flight.CflFeet <= 0 ? string.Empty : flight.CflFeet.ToString());
            if (value != null)
            {
                _ipcClient.SendCommand("SetCfl", flight.Callsign, value);
            }
        }

        private void EditSquawk(FlightPlanSnapshot flight)
        {
            var value = PromptDialog.Show("OzStrips :: " + flight.Callsign, "Squawk", flight.Squawk);
            if (value != null)
            {
                _ipcClient.SendCommand("SetSquawk", flight.Callsign, value);
            }
        }

        private async Task ConnectServerAsync()
        {
            try
            {
                _serverStatusLabel.Text = "Server: connecting";
                await _serverClient.SubscribeAsync(_aerodromeBox.Text.Trim().ToUpperInvariant(), _controllerCallsign).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _serverStatusLabel.Text = "Server: " + ex.Message;
            }
        }

        private static string BuildRouteTooltip(FlightPlanSnapshot flight, RouteCheckResult? result)
        {
            var lines = new List<string>
            {
                flight.Route,
                string.Empty,
                "Parsed route: " + RouteService.CleanRoute(flight.Route),
            };

            if (result == null)
            {
                lines.Add("Checking standard route...");
                return string.Join(Environment.NewLine, lines);
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                lines.Add("Route check error: " + result.Error);
                return string.Join(Environment.NewLine, lines);
            }

            if (result.IsDodgy)
            {
                lines.Add("Potentially non-compliant route detected.");
            }

            if (result.ValidRoutes.Count > 0)
            {
                lines.Add("Accepted routes:");
                lines.AddRange(result.ValidRoutes.Select(route => "(" + route.AircraftType + ") " + route.RouteText));
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FirstRouteElement(string route)
        {
            return (route ?? string.Empty)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(item => !string.Equals(item, "DCT", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        }

        private static string Truncate(string value, int length)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= length)
            {
                return value;
            }

            return value.Substring(0, Math.Max(0, length - 3)) + "...";
        }

        private void SafeUi(Action action)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
    }
}
