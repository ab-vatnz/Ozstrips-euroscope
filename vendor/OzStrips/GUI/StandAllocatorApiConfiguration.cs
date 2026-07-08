using System;
using System.IO;
using System.Reflection;

namespace MaxRumsey.OzStripsPlugin.GUI;

internal static class StandAllocatorApiConfiguration
{
    private const string DefaultApiBaseUrl = "http://152.67.96.250:8000";
    private const string EnvironmentVariableName = "VATNZ_STAND_ALLOCATOR_API_URL";
    private const string OverrideFileName = "VATNZ.StandAllocatorPlugin.url.txt";

    public static string GetApiBaseUrl()
    {
        var environmentValue = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue.Trim();
        }

        var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            var overridePath = Path.Combine(assemblyDirectory, OverrideFileName);
            if (File.Exists(overridePath))
            {
                var fileValue = File.ReadAllText(overridePath).Trim();
                if (!string.IsNullOrWhiteSpace(fileValue))
                {
                    return fileValue;
                }
            }
        }

        return DefaultApiBaseUrl;
    }
}
