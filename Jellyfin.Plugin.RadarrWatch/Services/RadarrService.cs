using System.Net.Http.Json;
using Jellyfin.Plugin.RadarrWatch.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RadarrWatch.Services;

public sealed class RadarrService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RadarrService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyDictionary<int, RadarrMovie> _cache =
        new Dictionary<int, RadarrMovie>();
    private DateTimeOffset _cacheExpiresAt = DateTimeOffset.MinValue;

    public RadarrService(HttpClient httpClient, ILogger<RadarrService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MovieWatchStatus>> GetStatusesAsync(
        IReadOnlyCollection<int> tmdbIds,
        CancellationToken cancellationToken)
    {
        var movies = await GetMoviesAsync(cancellationToken).ConfigureAwait(false);
        return tmdbIds
            .Distinct()
            .Where(movies.ContainsKey)
            .Select(id => movies[id])
            .Where(movie => movie.Monitored)
            .Select(movie => new MovieWatchStatus(movie.TmdbId, movie.Monitored, movie.HasFile))
            .ToArray();
    }

    public async Task<bool> TestAsync(CancellationToken cancellationToken)
    {
        Invalidate();
        await GetMoviesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<UpcomingMovie>> GetUpcomingAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var movies = await GetMoviesAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow.Date;
        return movies.Values
            .Where(movie => movie.Monitored
                && !movie.HasFile
                && movie.DigitalRelease is not null
                && movie.DigitalRelease.Value.Date >= now)
            .OrderBy(movie => movie.DigitalRelease)
            .ThenBy(movie => movie.Title)
            .Take(Math.Clamp(limit, 1, 30))
            .Select(movie => new UpcomingMovie(
                movie.TmdbId,
                movie.Title,
                movie.Year,
                movie.DigitalRelease,
                $"/RadarrWatch/UpcomingImage/{movie.TmdbId}"))
            .ToArray();
    }

    public async Task<(byte[] Content, string ContentType)?> GetImageAsync(
        int tmdbId,
        CancellationToken cancellationToken)
    {
        var movies = await GetMoviesAsync(cancellationToken).ConfigureAwait(false);
        if (!movies.TryGetValue(tmdbId, out var movie))
        {
            return null;
        }

        var image = movie.Images
            .FirstOrDefault(image => string.Equals(
                image.CoverType,
                "fanart",
                StringComparison.OrdinalIgnoreCase))
            ?? movie.Images.FirstOrDefault(image => string.Equals(
                image.CoverType,
                "poster",
                StringComparison.OrdinalIgnoreCase));
        if (image is null)
        {
            return null;
        }

        var baseUri = GetBaseUri();
        if (!string.IsNullOrWhiteSpace(image.Url))
        {
            // Radarr often reports artwork with its own internal hostname. Keep
            // the path but rebase it onto the administrator-configured origin.
            var imagePathAndQuery = Uri.TryCreate(image.Url, UriKind.Absolute, out var absolute)
                ? absolute.PathAndQuery
                : image.Url;
            var localImage = await FetchImageAsync(
                new Uri(baseUri, imagePathAndQuery.TrimStart('/')),
                includeApiKey: true,
                cancellationToken).ConfigureAwait(false);
            if (localImage is not null)
            {
                return localImage;
            }
        }

        if (!Uri.TryCreate(image.RemoteUrl, UriKind.Absolute, out var remoteUri)
            || remoteUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(remoteUri.Host, "image.tmdb.org", StringComparison.OrdinalIgnoreCase))
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
            request.Headers.Add("X-Api-Key", Plugin.Instance.Configuration.RadarrApiKey.Trim());
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken)
            .ConfigureAwait(false);
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

    private async Task<IReadOnlyDictionary<int, RadarrMovie>> GetMoviesAsync(
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
            var baseUri = GetBaseUri();

            if (string.IsNullOrWhiteSpace(config.RadarrApiKey))
            {
                throw new InvalidOperationException("Radarr API key is not configured.");
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(baseUri, $"{baseUri.AbsolutePath.TrimEnd('/')}/api/v3/movie"));
            request.Headers.Add("X-Api-Key", config.RadarrApiKey.Trim());

            using var response = await _httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var result = await response.Content
                .ReadFromJsonAsync<List<RadarrMovie>>(cancellationToken: cancellationToken)
                .ConfigureAwait(false) ?? [];

            _cache = result
                .Where(movie => movie.TmdbId > 0)
                .GroupBy(movie => movie.TmdbId)
                .ToDictionary(group => group.Key, group => group.First());
            _logger.LogInformation(
                "Loaded {MovieCount} Radarr movies; {MonitoredCount} are monitored.",
                _cache.Count,
                _cache.Values.Count(movie => movie.Monitored));
            _cacheExpiresAt = DateTimeOffset.UtcNow.AddSeconds(
                Math.Clamp(config.CacheSeconds, 10, 300));
            return _cache;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not retrieve movies from Radarr.");
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
        var value = Plugin.Instance.Configuration.RadarrUrl.TrimEnd('/') + "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Radarr URL must be an absolute HTTP or HTTPS URL.");
        }

        return baseUri;
    }
}
