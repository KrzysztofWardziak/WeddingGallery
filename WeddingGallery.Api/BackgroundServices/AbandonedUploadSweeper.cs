using WeddingGallery.Application.Interfaces;

namespace WeddingGallery.Api.BackgroundServices
{
    /// <summary>
    /// Deletes chunked uploads nobody finished. A guest who closes the browser mid-upload
    /// leaves a partial file that nothing will ever complete, and at up to 500 MB each those
    /// would quietly consume the volume over the course of a wedding.
    /// </summary>
    public sealed class AbandonedUploadSweeper : BackgroundService
    {
        // Long enough that a guest fighting bad wifi for an hour still finds their session,
        // short enough that the volume is not carrying yesterday's abandoned uploads.
        private static readonly TimeSpan MaxSessionAge = TimeSpan.FromHours(24);
        private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AbandonedUploadSweeper> _logger;

        public AbandonedUploadSweeper(IServiceScopeFactory scopeFactory, ILogger<AbandonedUploadSweeper> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Runs immediately on startup as well as on the interval, so files orphaned by a
            // restart are collected without waiting an hour.
            while (!stoppingToken.IsCancellationRequested)
            {
                await SweepAsync();

                try
                {
                    await Task.Delay(SweepInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task SweepAsync()
        {
            try
            {
                // IChunkedUploadService is scoped (it reaches the photo repository), so the
                // sweeper needs its own scope per run rather than a captured instance.
                using var scope = _scopeFactory.CreateScope();
                var uploads = scope.ServiceProvider.GetRequiredService<IChunkedUploadService>();

                var removed = await uploads.SweepAsync(MaxSessionAge);
                if (removed > 0)
                {
                    _logger.LogInformation("Removed {Count} abandoned upload session(s).", removed);
                }
            }
            catch (Exception ex)
            {
                // A failed sweep must never take the API down with it; the next pass retries.
                _logger.LogWarning(ex, "Sweeping abandoned uploads failed.");
            }
        }
    }
}
