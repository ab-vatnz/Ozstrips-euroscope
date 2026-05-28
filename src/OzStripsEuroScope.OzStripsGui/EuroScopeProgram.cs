using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MaxRumsey.OzStripsPlugin.GUI.Shared;
using OzStripsEuroScope.VatSysShim;
using vatsys;

namespace MaxRumsey.OzStripsPlugin.GUI
{
    internal static class EuroScopeProgram
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var context = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            MMI.InstallGuiContext(context);

            var options = StartupOptions.Parse(args);
            EuroScopeBridge.Start(options.PipeName);

            var aerodromeManager = new AerodromeManager();
            using (var form = new MainForm(true, aerodromeManager))
            {
                var bridgeAerodromes = Array.Empty<string>();
                var bridgeSetAerodrome = string.Empty;
                ConnectionMetadataDTO.Servers? bridgeSetServer = null;

                aerodromeManager.OpenGUI += (_, _) =>
                {
                    if (form.WindowState == FormWindowState.Minimized)
                    {
                        form.WindowState = FormWindowState.Normal;
                    }

                    form.Show();
                    form.Activate();
                };

                EuroScopeBridge.FocusRequested += (_, _) =>
                {
                    if (form.IsDisposed)
                    {
                        return;
                    }

                    if (form.WindowState == FormWindowState.Minimized)
                    {
                        form.WindowState = FormWindowState.Normal;
                    }

                    form.Show();
                    form.Activate();
                };

                using var pluginMonitor = CreatePluginMonitor(options.PluginPid, form);

                EuroScopeBridge.ServerSuggested += (_, server) =>
                {
                    var type = server.ToUpperInvariant() switch
                    {
                        "SWEATBOX1" => ConnectionMetadataDTO.Servers.SWEATBOX1,
                        "LOCALHOST" => ConnectionMetadataDTO.Servers.LOCALHOST,
                        _ => (ConnectionMetadataDTO.Servers?)null,
                    };

                    if (form.IsDisposed || !type.HasValue)
                    {
                        return;
                    }

                    var current = form.Controller.CurrentServer;
                    var currentStillBridgeManaged = bridgeSetServer.HasValue && current == bridgeSetServer.Value;
                    var shouldApply =
                        current == ConnectionMetadataDTO.Servers.VATSIM ||
                        currentStillBridgeManaged;

                    if (current != type.Value && shouldApply)
                    {
                        form.Controller.SetServerType(type.Value);
                        bridgeSetServer = type.Value;
                    }
                    else if (current == type.Value && !bridgeSetServer.HasValue)
                    {
                        bridgeSetServer = type.Value;
                    }
                };

                EuroScopeBridge.AerodromesSeenChanged += (_, aerodromes) =>
                {
                    bridgeAerodromes = aerodromes.ToArray();
                    if (!form.IsDisposed)
                    {
                        form.Controller.MergeCustomAerodromeList(bridgeAerodromes);
                    }
                };

                EuroScopeBridge.AerodromeSuggested += (_, aerodrome) =>
                {
                    if (form.IsDisposed || string.IsNullOrWhiteSpace(aerodrome))
                    {
                        return;
                    }

                    var current = form.Controller.CurrentAerodrome;
                    var currentLooksValid = current.Length == 4 && current.All(char.IsLetter) && !current.StartsWith("Y", StringComparison.OrdinalIgnoreCase);
                    var hasBridgeSetAerodrome = !string.IsNullOrWhiteSpace(bridgeSetAerodrome);
                    var currentStillBridgeManaged = hasBridgeSetAerodrome && string.Equals(current, bridgeSetAerodrome, StringComparison.OrdinalIgnoreCase);

                    if (string.Equals(current, aerodrome, StringComparison.OrdinalIgnoreCase) && !hasBridgeSetAerodrome)
                    {
                        bridgeSetAerodrome = aerodrome;
                    }
                    else if (!string.Equals(current, aerodrome, StringComparison.OrdinalIgnoreCase) &&
                        (!hasBridgeSetAerodrome || currentStillBridgeManaged || !currentLooksValid))
                    {
                        form.Controller.SetAerodrome(aerodrome);
                        bridgeSetAerodrome = aerodrome;
                    }
                };

                EuroScopeBridge.FdrUpdated += (_, fdr) =>
                {
                    if (!form.IsDisposed && form.IsHandleCreated)
                    {
                        _ = form.Controller.UpdateFDR(fdr);
                    }
                };

                aerodromeManager.Initialize();
                Application.Run(form);
            }

            EuroScopeBridge.Stop();
        }

        private sealed class StartupOptions
        {
            public string PipeName { get; private set; } = string.Empty;

            public int PluginPid { get; private set; }

            public static StartupOptions Parse(string[] args)
            {
                var options = new StartupOptions();

                for (var i = 0; i < args.Length; i++)
                {
                    var arg = args[i];
                    if (string.Equals(arg, "--pipe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        options.PipeName = args[++i];
                    }
                    else if (arg.StartsWith("--pipe=", StringComparison.OrdinalIgnoreCase))
                    {
                        options.PipeName = arg.Split(new[] { '=' }, 2).Last();
                    }
                    else if (string.Equals(arg, "--plugin-pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        int.TryParse(args[++i], out var pluginPid);
                        options.PluginPid = pluginPid;
                    }
                    else if (arg.StartsWith("--plugin-pid=", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(arg.Split(new[] { '=' }, 2).Last(), out var pluginPid);
                        options.PluginPid = pluginPid;
                    }
                }

                return options;
            }
        }

        private static System.Windows.Forms.Timer? CreatePluginMonitor(int pluginPid, Form form)
        {
            if (pluginPid <= 0)
            {
                return null;
            }

            var timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (_, _) =>
            {
                try
                {
                    using var process = Process.GetProcessById(pluginPid);
                    if (!process.HasExited)
                    {
                        return;
                    }
                }
                catch
                {
                }

                timer.Stop();
                if (!form.IsDisposed)
                {
                    form.Close();
                }
            };
            timer.Start();
            return timer;
        }
    }
}
