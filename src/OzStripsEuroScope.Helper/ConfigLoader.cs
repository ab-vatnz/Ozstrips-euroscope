using System;
using System.IO;
using System.Xml.Linq;

namespace OzStripsEuroScope.Helper
{
    internal sealed class HelperConfig
    {
        public bool UseNose { get; set; }
    }

    internal static class ConfigLoader
    {
        public static HelperConfig Load()
        {
            var config = new HelperConfig();
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AerodromeSettings.xml");

            if (!File.Exists(path))
            {
                return config;
            }

            try
            {
                var document = XDocument.Load(path);
                var useNose = document.Root?.Element("UseNose")?.Value;
                if (bool.TryParse(useNose, out var parsed))
                {
                    config.UseNose = parsed;
                }
            }
            catch
            {
            }

            return config;
        }
    }
}
