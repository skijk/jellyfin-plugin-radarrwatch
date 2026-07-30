# Radarr Watch

A standalone Jellyfin 10.11 plugin that complements Jellyfin Enhanced without
modifying it. Movie request buttons are marked and disabled when the matching
TMDB movie is already monitored in Radarr, even when no Seerr request exists.
It also provides JellySpotlight with monitored, unavailable movies ordered by
their upcoming digital release date. Theatrical release dates are ignored.

## Requirements

- Jellyfin 10.11.11
- Jellyfin Enhanced with Seerr search enabled
- File Transformation plugin
- Radarr v3 API

## Install

Build in Release mode, copy `Jellyfin.Plugin.RadarrWatch.dll` to a plugin
directory on the Jellyfin server, and restart Jellyfin. Configure the Radarr URL
and API key under Dashboard → Plugins → Radarr Watch, save, and restart once
more so the web transformation is registered.

The Radarr API key remains server-side. Authenticated Jellyfin users receive
only the monitored/file status for requested TMDB IDs.
Upcoming artwork is proxied through Jellyfin, so the Radarr API key and internal
Radarr address are never exposed to web clients.
