using Jellyfin.Plugin.ArrWatch.Models;
using Jellyfin.Plugin.ArrWatch.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArrWatch.Controllers;

[ApiController]
[Route("ArrWatch")]
public sealed class ArrWatchController : ControllerBase
{
    private readonly RadarrService _radarr;
    private readonly SonarrService _sonarr;
    private readonly ILogger<ArrWatchController> _logger;

    public ArrWatchController(
        RadarrService radarr,
        SonarrService sonarr,
        ILogger<ArrWatchController> logger)
    {
        _radarr = radarr;
        _sonarr = sonarr;
        _logger = logger;
    }

    [HttpGet("Status")]
    [Authorize]
    [ProducesResponseType(typeof(WatchStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<WatchStatusResponse>> GetStatus(
        [FromQuery] string tmdbIds,
        CancellationToken cancellationToken)
    {
        if (!_radarr.IsConfigured)
        {
            return Ok(new WatchStatusResponse(
                Plugin.Instance.Configuration.DisplayText,
                []));
        }

        var validIds = (tmdbIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
        if (validIds.Length > 100)
        {
            return BadRequest(new { Error = "At most 100 TMDB IDs may be checked at once." });
        }

        var statuses = await _radarr.GetStatusesAsync(validIds, cancellationToken)
            .ConfigureAwait(false);
        return Ok(new WatchStatusResponse(
            Plugin.Instance.Configuration.DisplayText,
            statuses));
    }

    [HttpGet("Upcoming")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<UpcomingItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UpcomingItem>>> GetUpcoming(
        [FromQuery] int limit = 12,
        CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task<IReadOnlyList<UpcomingItem>>>();
        if (_radarr.IsConfigured)
        {
            tasks.Add(LoadUpcomingAsync("Radarr", _radarr.GetUpcomingAsync, cancellationToken));
        }

        if (_sonarr.IsConfigured)
        {
            tasks.Add(LoadUpcomingAsync("Sonarr", _sonarr.GetUpcomingAsync, cancellationToken));
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return Ok(results
            .SelectMany(items => items)
            .OrderBy(item => item.ReleaseDate)
            .ThenBy(item => item.Title)
            .Take(Math.Clamp(limit, 1, 30))
            .ToArray());
    }

    [HttpGet("UpcomingImage/{source}/{sourceId:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetUpcomingImage(
        string source,
        int sourceId,
        CancellationToken cancellationToken)
    {
        var image = string.Equals(source, "radarr", StringComparison.OrdinalIgnoreCase)
            ? await _radarr.GetImageAsync(sourceId, cancellationToken).ConfigureAwait(false)
            : string.Equals(source, "sonarr", StringComparison.OrdinalIgnoreCase)
                ? await _sonarr.GetImageAsync(sourceId, cancellationToken).ConfigureAwait(false)
                : null;
        return image is null
            ? NotFound()
            : File(image.Value.Content, image.Value.ContentType);
    }

    [HttpPost("Test/{source}")]
    [Authorize(Policy = "RequiresElevation")]
    public async Task<IActionResult> Test(string source, CancellationToken cancellationToken)
    {
        try
        {
            if (string.Equals(source, "radarr", StringComparison.OrdinalIgnoreCase))
            {
                await _radarr.TestAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (string.Equals(source, "sonarr", StringComparison.OrdinalIgnoreCase))
            {
                await _sonarr.TestAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                return NotFound();
            }

            return Ok(new { Success = true });
        }
        catch (Exception exception)
        {
            return BadRequest(new { Success = false, Error = exception.Message });
        }
    }

    [HttpGet("Client.js")]
    [AllowAnonymous]
    public IActionResult GetClientScript()
    {
        return EmbeddedFile("Web.arr-watch.js", "text/javascript; charset=utf-8");
    }

    [HttpGet("Client.css")]
    [AllowAnonymous]
    public IActionResult GetClientStyles()
    {
        return EmbeddedFile("Web.arr-watch.css", "text/css; charset=utf-8");
    }

    private FileStreamResult EmbeddedFile(string suffix, string contentType)
    {
        var name = $"{typeof(Plugin).Namespace}.{suffix}";
        var stream = typeof(Plugin).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing resource {name}.");
        return File(stream, contentType);
    }

    private async Task<IReadOnlyList<UpcomingItem>> LoadUpcomingAsync(
        string source,
        Func<CancellationToken, Task<IReadOnlyList<UpcomingItem>>> loader,
        CancellationToken cancellationToken)
    {
        try
        {
            return await loader(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load upcoming titles from {Source}.", source);
            return [];
        }
    }
}
