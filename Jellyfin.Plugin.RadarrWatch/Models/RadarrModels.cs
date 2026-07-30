using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.RadarrWatch.Models;

public sealed class RadarrMovie
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("monitored")]
    public bool Monitored { get; set; }

    [JsonPropertyName("hasFile")]
    public bool HasFile { get; set; }

    [JsonPropertyName("digitalRelease")]
    public DateTimeOffset? DigitalRelease { get; set; }

    [JsonPropertyName("images")]
    public List<RadarrImage> Images { get; set; } = [];
}

public sealed class RadarrImage
{
    [JsonPropertyName("coverType")]
    public string CoverType { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("remoteUrl")]
    public string RemoteUrl { get; set; } = string.Empty;
}

public sealed record MovieWatchStatus(int TmdbId, bool Monitored, bool HasFile);

public sealed record WatchStatusResponse(string DisplayText, IReadOnlyList<MovieWatchStatus> Movies);

public sealed record UpcomingMovie(
    int TmdbId,
    string Title,
    int Year,
    DateTimeOffset? DigitalRelease,
    string ImageUrl);
