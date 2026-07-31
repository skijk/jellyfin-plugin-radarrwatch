# Arr Watch

A Jellyfin 10.11 plugin that connects Radarr and Sonarr without exposing either
service to web clients.

Radarr supplies monitored movie status to Jellyfin Enhanced and confirmed
future digital movie releases to JellySpotlight. Sonarr supplies confirmed
future season premieres. Arr Watch combines movie releases and season premieres
into one chronological `Coming soon` feed.

## Coming soon rules

- Movies must be monitored, unavailable and have a confirmed future Radarr
  `digitalRelease` date. Theatrical-only and unknown dates are excluded.
- Series must be monitored and have a monitored, unavailable first regular
  episode (`seasonNumber > 0`, `episodeNumber == 1`) with a confirmed future
  Sonarr `airDateUtc` value.
- Specials, later episodes and mid-season returns are excluded.
- Radarr and Sonarr are optional and fail independently. The feed works with
  either integration or both.

## Dependencies

| Component | Status | Used for |
| --- | --- | --- |
| Jellyfin Server 10.11.11 | Required | Supported server and plugin ABI |
| [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation) | Required | Injects Arr Watch into Jellyfin Web |
| Radarr with the v3 API | Optional service | Movie status, digital release dates and artwork |
| Sonarr with the v3 API | Optional service | Season premiere dates and series artwork |
| [Jellyfin Enhanced](https://github.com/n00bcodr/Jellyfin-Enhanced) with Seerr search enabled | Optional integration | Marks monitored movie request actions |
| [JellySpotlight](https://github.com/skijk/jellyfin-plugin-jellyspotlight) | Optional consumer | Displays the combined Coming soon row |

Arr Watch does not require Jelana, Playback Reporting, JellyBulletin or JS
Injector.

## Installation

1. Install File Transformation.
2. Add the Arr Watch development repository:

   ```text
   https://raw.githubusercontent.com/skijk/jellyfin-plugin-arrwatch-repository/main/manifest.json
   ```

3. Install Arr Watch and restart Jellyfin.
4. Enable and configure Radarr, Sonarr or both under **Dashboard → Plugins →
   Arr Watch**.

API keys remain server-side. Upcoming artwork is proxied through Jellyfin, so
internal Radarr and Sonarr addresses are never exposed to web clients.
If Sonarr's local MediaCover cache cannot serve an image, Arr Watch safely
falls back to the HTTPS artwork URL supplied by Sonarr from a trusted TVDB or
TMDB image host.

## API

- `GET /ArrWatch/Status` returns Radarr monitoring state for requested TMDB IDs.
- `GET /ArrWatch/Upcoming` returns the combined chronological movie and season
  premiere feed.
- `POST /ArrWatch/Test/radarr` and `POST /ArrWatch/Test/sonarr` test each saved
  connection independently.

## Build

```bash
dotnet build ArrWatch.sln --configuration Release
```

The project targets .NET 9 and builds against Jellyfin 10.11.11.
