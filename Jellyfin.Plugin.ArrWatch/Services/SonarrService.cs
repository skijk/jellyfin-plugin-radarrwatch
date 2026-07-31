using System.Globalization;
using System.Net.Http.Json;
using Jellyfin.Plugin.ArrWatch.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArrWatch.Services;

public sealed class SonarrService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SonarrService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyList<SonarrEpisode> _cache = [];
    private DateTimeOffset _cacheExpiresAt = DateTimeOffset.MinValue;

    public SonarrService(HttpClient httpClient, ILogger<SonarrService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public bool IsConfigured => Plugin.Instance.Configuration.SonarrEnabled
        && !string.IsNullOrWhiteSpace(Plugin.Instance.Configuration.SonarrApiKey);

    public async Task<bool> TestAsync(CancellationToken cancellationToken)
    {
        Invalidate();
        await GetEpisodesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<UpcomingItem>> GetUpcomingAsync(
        CancellationToken cancellationToken)
    {
        var episodes = await GetEpisodesAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        return episodes
            .Where(episode => episode.SeasonNumber > 0
                && episode.EpisodeNumber == 1
                && episode.Monitored
                && !episode.HasFile
                && episode.AirDateUtc is not null
                && episode.AirDateUtc.Value >= now
                && episode.Series is { Monitored: true })
            .GroupBy(episode => new { episode.SeriesId, episode.SeasonNumber })
            .Select(group => group.OrderBy(episode => episode.AirDateUtc).First())
            .OrderBy(episode => episode.AirDateUtc)
            .ThenBy(episode => episode.Series!.Title)
            .Select(episode => new UpcomingItem(
                "series",
                "sonarr",
                episode.SeriesId,
                null,
                episode.Series!.TvdbId > 0 ? episode.Series.TvdbId : null,
                episode.Series.Title,
                episode.Series.Year,
                episode.SeasonNumber,
                episode.AirDateUtc!.Value,
                $"/ArrWatch/UpcomingImage/sonarr/{episode.SeriesId}"))
            .ToArray();
    }

    public async Task<(byte[] Content, string ContentType)?> GetImageAsync(
        int seriesId,
        CancellationToken cancellationToken)
    {
        var episodes = await GetEpisodesAsync(cancellationToken).ConfigureAwait(false);
        var series = episodes.FirstOrDefault(episode => episode.SeriesId == seriesId)?.Series;
        var image = series?.Images.FirstOrDefault(candidate => string.Equals(
            candidate.CoverType,
            "fanart",
            StringComparison.OrdinalIgnoreCase))
            ?? series?.Images.FirstOrDefault(candidate => string.Equals(
                candidate.CoverType,
                "poster",
                StringComparison.OrdinalIgnoreCase));
        if (image is null)
        {
            return null;
        }

        var baseUri = GetBaseUri();
        if (!string.IsNullOrWhiteSpace(image.Url))
        {
            var pathAndQuery = Uri.TryCreate(image.Url, UriKind.Absolute, out var absolute)
                ? absolute.PathAndQuery
                : image.Url;
            var localImage = await FetchImageAsync(
                new Uri(baseUri, pathAndQuery.TrimStart('/')),
                includeApiKey: true,
                cancellationToken).ConfigureAwait(false);
            if (localImage is not null)
            {
                return localImage;
            }
        }

        if (!Uri.TryCreate(image.RemoteUrl, UriKind.Absolute, out var remoteUri)
            || remoteUri.Scheme != Uri.UriSchemeHttps
            || (!string.Equals(remoteUri.Host, "artworks.thetvdb.com", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(remoteUri.Host, "image.tmdb.org", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return await FetchImageAsync(
            remoteUri,
            includeApiKey: false,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<(byte[] Content, string ContentType)?> FetchImageAsync(
        Uri imageUri,
        bool includeApiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, imageUri);
        if (includeApiKey)
        {
            request.Headers.Add("X-Api-Key", Plugin.Instance.Configuration.SonarrApiKey.Trim());
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!response.IsSuccessStatusCode
            || string.IsNullOrWhiteSpace(contentType)
            || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return (
            await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false),
            contentType);
    }

    private async Task<IReadOnlyList<SonarrEpisode>> GetEpisodesAsync(
        CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < _cacheExpiresAt)
        {
            return _cache;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (DateTimeOffset.UtcNow < _cacheExpiresAt)
            {
                return _cache;
            }

            var config = Plugin.Instance.Configuration;
            if (string.IsNullOrWhiteSpace(config.SonarrApiKey))
            {
                throw new InvalidOperationException("Sonarr API key is not configured.");
            }

            var baseUri = GetBaseUri();
            var start = DateTimeOffset.UtcNow.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = DateTimeOffset.UtcNow.Date.AddYears(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var path = $"{baseUri.AbsolutePath.TrimEnd('/')}/api/v3/calendar"
                + $"?start={start}&end={end}&includeSeries=true&includeEpisodeFile=true";
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, path));
            request.Headers.Add("X-Api-Key", config.SonarrApiKey.Trim());
            using var response = await _httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            _cache = await response.Content
                .ReadFromJsonAsync<List<SonarrEpisode>>(cancellationToken: cancellationToken)
                .ConfigureAwait(false) ?? [];
            _logger.LogInformation(
                "Loaded {EpisodeCount} Sonarr calendar episodes; {PremiereCount} are future monitored season premieres.",
                _cache.Count,
                _cache.Count(episode => episode.SeasonNumber > 0
                    && episode.EpisodeNumber == 1
                    && episode.Monitored
                    && episode.AirDateUtc >= DateTimeOffset.UtcNow
                    && episode.Series is { Monitored: true }));
            _cacheExpiresAt = DateTimeOffset.UtcNow.AddSeconds(
                Math.Clamp(config.CacheSeconds, 10, 300));
            return _cache;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not retrieve the Sonarr calendar.");
            throw;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void Invalidate()
    {
        _cacheExpiresAt = DateTimeOffset.MinValue;
    }

    private static Uri GetBaseUri()
    {
        var value = Plugin.Instance.Configuration.SonarrUrl.TrimEnd('/') + "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Sonarr URL must be an absolute HTTP or HTTPS URL.");
        }

        return baseUri;
    }
}
