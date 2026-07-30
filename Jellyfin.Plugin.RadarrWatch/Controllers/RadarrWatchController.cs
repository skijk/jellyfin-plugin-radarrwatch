using Jellyfin.Plugin.RadarrWatch.Models;
using Jellyfin.Plugin.RadarrWatch.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.RadarrWatch.Controllers;

[ApiController]
[Route("RadarrWatch")]
public sealed class RadarrWatchController : ControllerBase
{
    private readonly RadarrService _radarr;

    public RadarrWatchController(RadarrService radarr)
    {
        _radarr = radarr;
    }

    [HttpGet("Status")]
    [Authorize]
    [ProducesResponseType(typeof(WatchStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<WatchStatusResponse>> GetStatus(
        [FromQuery] string tmdbIds,
        CancellationToken cancellationToken)
    {
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
    [ProducesResponseType(typeof(IReadOnlyList<UpcomingMovie>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UpcomingMovie>>> GetUpcoming(
        [FromQuery] int limit = 12,
        CancellationToken cancellationToken = default)
    {
        var movies = await _radarr.GetUpcomingAsync(limit, cancellationToken)
            .ConfigureAwait(false);
        return Ok(movies);
    }

    [HttpGet("UpcomingImage/{tmdbId:int}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetUpcomingImage(
        int tmdbId,
        CancellationToken cancellationToken)
    {
        var image = await _radarr.GetImageAsync(tmdbId, cancellationToken)
            .ConfigureAwait(false);
        return image is null
            ? NotFound()
            : File(image.Value.Content, image.Value.ContentType);
    }

    [HttpPost("Test")]
    [Authorize(Policy = "RequiresElevation")]
    public async Task<IActionResult> Test(CancellationToken cancellationToken)
    {
        try
        {
            await _radarr.TestAsync(cancellationToken).ConfigureAwait(false);
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
        return EmbeddedFile("Web.radarr-watch.js", "text/javascript; charset=utf-8");
    }

    [HttpGet("Client.css")]
    [AllowAnonymous]
    public IActionResult GetClientStyles()
    {
        return EmbeddedFile("Web.radarr-watch.css", "text/css; charset=utf-8");
    }

    private FileStreamResult EmbeddedFile(string suffix, string contentType)
    {
        var name = $"{typeof(Plugin).Namespace}.{suffix}";
        var stream = typeof(Plugin).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing resource {name}.");
        return File(stream, contentType);
    }
}
