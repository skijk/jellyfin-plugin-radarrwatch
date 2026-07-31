using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ArrWatch.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool RadarrEnabled { get; set; } = true;
    public string RadarrUrl { get; set; } = "http://radarr:7878";
    public string RadarrApiKey { get; set; } = string.Empty;
    public bool SonarrEnabled { get; set; }
    public string SonarrUrl { get; set; } = "http://sonarr:8989";
    public string SonarrApiKey { get; set; } = string.Empty;
    public string DisplayText { get; set; } = "Requested";
    public int CacheSeconds { get; set; } = 30;
}
