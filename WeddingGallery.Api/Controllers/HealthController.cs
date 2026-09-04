using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WeddingGallery.Infrastructure.Data;

namespace WeddingGallery.Api.Controllers
{
    /// <summary>
    /// Liveness probe. Exists because deployment is automated: the agent on the server
    /// restarts containers and has no way to tell whether the application came back, so
    /// until now the only evidence a deploy worked was a line in the journal.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        // A wedged database must not wedge the probe. Anything watching this endpoint needs
        // an answer within a predictable time, and "no answer" is the one reply it cannot
        // act on.
        private static readonly TimeSpan DatabaseTimeout = TimeSpan.FromSeconds(3);

        private readonly WeddingGalleryDbContext _db;

        public HealthController(WeddingGalleryDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            // Reporting only that the process answers would be worthless: the api container
            // dies on startup if it cannot reach Postgres, so a running process already
            // implies the database was up once. What matters is whether it is up now.
            var databaseReachable = await CanReachDatabaseAsync();

            // Deliberately no exception text, no connection string, no server name. This
            // endpoint is public, and a health probe is not a place to leak topology.
            return databaseReachable
                ? Ok(new { status = "ok", database = "ok" })
                : StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new { status = "degraded", database = "unreachable" });
        }

        private async Task<bool> CanReachDatabaseAsync()
        {
            using var timeout = new CancellationTokenSource(DatabaseTimeout);

            try
            {
                return await _db.Database.CanConnectAsync(timeout.Token);
            }
            catch
            {
                // CanConnectAsync throws rather than returning false for several failure
                // modes, including the cancellation above. A probe that throws becomes a 500
                // the caller cannot interpret, so every failure collapses to "unreachable".
                return false;
            }
        }
    }
}
