using System;

namespace OzStripsEuroScope.Helper
{
    internal sealed class StartupOptions
    {
        public string PipeName { get; private set; } = string.Empty;

        public int PluginProcessId { get; private set; }

        public static StartupOptions Parse(string[] args)
        {
            var options = new StartupOptions();

            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--pipe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.PipeName = args[++i];
                }
                else if (string.Equals(args[i], "--plugin-pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int.TryParse(args[++i], out var pid);
                    options.PluginProcessId = pid;
                }
            }

            return options;
        }
    }
}
