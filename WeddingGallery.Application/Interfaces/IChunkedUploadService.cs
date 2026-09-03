using WeddingGallery.Application.Media;
using WeddingGallery.Domain.Entities;

namespace WeddingGallery.Application.Interfaces
{
    /// <summary>
    /// Uploads a single large file across several requests, so no one request approaches the
    /// 100 MB body limit Cloudflare enforces on the only route into this deployment.
    /// </summary>
    public interface IChunkedUploadService
    {
        /// <summary>
        /// Opens a session, validating format and declared size before any bytes travel.
        /// Throws <see cref="InvalidMediaFileException"/> when the file is not acceptable.
        /// </summary>
        Task<UploadSession> StartAsync(Guid eventId, string? uploaderName, string fileName, long totalSize);

        /// <summary>The session as it stands, or null when it is unknown. The resume primitive.</summary>
        Task<UploadSession?> GetAsync(Guid uploadId);

        /// <summary>Appends a chunk and returns the new offset.</summary>
        Task<long> AppendAsync(Guid uploadId, long offset, Stream content);

        /// <summary>Moves the finished file into the gallery and records it.</summary>
        Task<Photo> CompleteAsync(Guid uploadId);

        /// <summary>Drops a session the guest gave up on.</summary>
        Task AbandonAsync(Guid uploadId);

        /// <summary>Deletes sessions older than <paramref name="maxAge"/>; returns how many.</summary>
        Task<int> SweepAsync(TimeSpan maxAge);

        /// <summary>
        /// Drops every session belonging to an event, used when that event is deleted.
        /// Completing one afterwards would try to attach a photo to a row that no longer
        /// exists and fail on the foreign key.
        /// </summary>
        Task<int> AbandonForEventAsync(Guid eventId);
    }
}
