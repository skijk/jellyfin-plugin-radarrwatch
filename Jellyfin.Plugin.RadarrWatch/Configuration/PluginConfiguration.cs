using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.RadarrWatch.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public string RadarrUrl { get; set; } = "http://radarr:7878";
    public string RadarrApiKey { get; set; } = string.Empty;
    public string DisplayText { get; set; } = "Requested";
    public int CacheSeconds { get; set; } = 30;
}
