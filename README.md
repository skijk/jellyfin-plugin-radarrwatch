# Radarr Watch

A standalone Jellyfin 10.11 plugin that complements Jellyfin Enhanced without
modifying it. Movie request buttons are marked and disabled when the matching
TMDB movie is already monitored in Radarr, even when no Seerr request exists.
It also provides JellySpotlight with monitored, unavailable movies ordered by
their upcoming digital release date. Theatrical release dates are ignored.
Only titles with a confirmed future digital release date are returned; unknown
dates are not labelled as Coming soon.

## Dependencies

| Component | Status | Used for |
| --- | --- | --- |
| Jellyfin Server 10.11.11 | Required | Supported server and plugin ABI |
| [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) | Required | Injects Radarr Watch into Jellyfin Web |
| Radarr with the v3 API | Required service | Monitored/file status, digital release dates and artwork |
| [Jellyfin Enhanced](https://github.com/n00bcodr/Jellyfin-Enhanced) with Seerr search enabled | Optional integration | Marks and disables matching movie request actions |
| [JellySpotlight](https://github.com/skijk/jellyfin-plugin-jellyspotlight) | Optional consumer | Displays the Coming soon row |

Radarr Watch does not require Jelana, Playback Reporting, JellyBulletin or JS
Injector. The Coming soon API works without Jellyfin Enhanced, and the request
marking works without JellySpotlight.

## Install

1. Add and install File Transformation:

   ```text
   https://www.iamparadox.dev/jellyfin/plugins/manifest.json
   ```

2. Add the Radarr Watch repository:

   ```text
   https://raw.githubusercontent.com/skijk/jellyfin-plugin-radarrwatch-repository/main/manifest.json
   ```

3. Install Radarr Watch and restart Jellyfin.
4. Configure the Radarr URL and API key under **Dashboard → Plugins → Radarr
   Watch**, save, test the connection and restart once more so the web
   transformation is registered.

The Radarr API key remains server-side. Authenticated Jellyfin users receive
only the monitored/file status for requested TMDB IDs.
Upcoming artwork is proxied through Jellyfin, so the Radarr API key and internal
Radarr address are never exposed to web clients.

## Build

```bash
dotnet build RadarrWatch.sln --configuration Release
```

The project targets .NET 9 and builds against Jellyfin 10.11.11.
