using System;
using System.IO;

namespace MaxRumsey.OzStripsPlugin.GUI;

/// <summary>
/// Resolves the OzStrips server endpoint, allowing a private test server per installation.
/// </summary>
internal static class ServerEndpoint
{
    private const string OverrideFileName = "OzStripsServerUrl.txt";

    public static string BaseUrl
    {
        get
        {
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, OverrideFileName);
                if (File.Exists(path))
                {
                    var overrideUrl = File.ReadAllText(path).Trim();
                    if (Uri.TryCreate(overrideUrl, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        return uri.AbsoluteUri.TrimEnd('/') + "/";
                    }
                }
            }
            catch
            {
                // A missing or unreadable optional override must not stop OzStrips connecting normally.
            }

            return OzStripsConfig.socketioaddr.TrimEnd('/') + "/";
        }
    }
}
