using System.Globalization;
using Jellyfin.Plugin.RadarrWatch.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.RadarrWatch;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => "Radarr Watch";
    public override string Description => "Radarr integration for Jellyfin Web. Requires File Transformation and Radarr; Jellyfin Enhanced and JellySpotlight integrations are optional.";
    public override Guid Id => Guid.Parse("943575e3-77a3-47dc-b1e7-4e17d52442e2");
    public static Plugin Instance { get; private set; } = null!;

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                DisplayName = "Radarr Watch",
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace)
            }
        ];
    }
}
