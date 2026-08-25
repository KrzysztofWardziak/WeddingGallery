using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WeddingGallery.Application.Interfaces;

namespace WeddingGallery.Infrastructure.Media
{
    /// <summary>
    /// Pulls a poster frame out of a video by shelling out to ffmpeg, which is installed in
    /// the API image. Kept out of PhotoService so process handling lives in one place and the
    /// service stays testable without a subprocess.
    /// </summary>
    public sealed class FfmpegThumbnailGenerator : IThumbnailGenerator
    {
        private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);

        private readonly ILogger<FfmpegThumbnailGenerator> _logger;

        public FfmpegThumbnailGenerator(ILogger<FfmpegThumbnailGenerator> logger)
        {
            _logger = logger;
        }

        public async Task<bool> TryGenerateVideoThumbnailAsync(string videoPath, string thumbnailPath, CancellationToken cancellationToken = default)
        {
            // Seek a second in to skip the black opening frames phones often record. A clip
            // shorter than that yields no frame at all, so fall back to the very first one.
            if (await TryExtractFrameAsync(videoPath, thumbnailPath, "00:00:01", cancellationToken))
            {
                return true;
            }

            return await TryExtractFrameAsync(videoPath, thumbnailPath, "00:00:00", cancellationToken);
        }

        private async Task<bool> TryExtractFrameAsync(string videoPath, string thumbnailPath, string seekPosition, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // ArgumentList quotes each argument for us, so paths containing spaces or quotes
            // from a guest's file name cannot break out into extra ffmpeg arguments.
            startInfo.ArgumentList.Add("-nostdin");
            startInfo.ArgumentList.Add("-loglevel");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add(seekPosition);
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(videoPath);
            startInfo.ArgumentList.Add("-frames:v");
            startInfo.ArgumentList.Add("1");
            // Never upscale a small clip, and keep both dimensions even for the encoder.
            // The width has to go through the named w= option: as a positional argument the
            // comma inside min() is read as a filtergraph separator and ffmpeg fails with
            // "Invalid size 'min(640'".
            startInfo.ArgumentList.Add("-vf");
            startInfo.ArgumentList.Add("scale=w='min(640,iw)':h=-2");
            startInfo.ArgumentList.Add("-q:v");
            startInfo.ArgumentList.Add("4");
            startInfo.ArgumentList.Add(thumbnailPath);

            try
            {
                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    _logger.LogWarning("ffmpeg could not be started for {VideoPath}.", videoPath);
                    return false;
                }

                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(ProcessTimeout);

                var stderr = process.StandardError.ReadToEndAsync();

                try
                {
                    await process.WaitForExitAsync(timeoutSource.Token);
                }
                catch (OperationCanceledException)
                {
                    // A wedged ffmpeg must not hold the request open; kill the tree and give up.
                    TryKill(process);
                    _logger.LogWarning("ffmpeg timed out after {Timeout} for {VideoPath}.", ProcessTimeout, videoPath);
                    return false;
                }

                if (process.ExitCode != 0)
                {
                    _logger.LogWarning(
                        "ffmpeg exited with {ExitCode} for {VideoPath} at {SeekPosition}: {Error}",
                        process.ExitCode, videoPath, seekPosition, await stderr);
                    return false;
                }

                // A zero exit code is not proof of output: seeking past the end of a short
                // clip produces no frame and still succeeds.
                var thumbnail = new FileInfo(thumbnailPath);
                return thumbnail.Exists && thumbnail.Length > 0;
            }
            catch (Exception ex)
            {
                // ffmpeg missing from the image, a permission problem, a corrupt upload - none
                // of it is worth failing the guest's upload over.
                _logger.LogWarning(ex, "Thumbnail generation failed for {VideoPath}.", videoPath);
                return false;
            }
        }

        private void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not kill the timed-out ffmpeg process.");
            }
        }
    }
}
