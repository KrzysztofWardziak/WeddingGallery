using WeddingGallery.Application.Media;
using WeddingGallery.Domain.Entities;

namespace WeddingGallery.Application.Interfaces
{
    public interface IPhotoService
    {
        /// <summary>Throws <see cref="InvalidMediaFileException"/> when the file is not accepted.</summary>
        Task<Photo> UploadPhotoAsync(Guid eventId, string? uploaderName, UploadedFile file);

        /// <summary>
        /// Validates every file before writing any of them, so a rejected file cannot leave
        /// half the batch persisted. Throws <see cref="InvalidMediaFileException"/> on the first
        /// file that is not accepted.
        /// </summary>
        Task<IEnumerable<Photo>> UploadPhotosAsync(Guid eventId, string? uploaderName, IEnumerable<UploadedFile> files);

        /// <summary>
        /// Takes ownership of a file already written to disk elsewhere, moving it into the
        /// gallery and recording it. Used by the chunked upload path, which has already
        /// streamed the bytes and must not pay to copy them a second time.
        /// </summary>
        Task<Photo> AdoptFileAsync(Guid eventId, string? uploaderName, string originalFileName, string mediaType, string sourceFilePath);

        /// <summary>
        /// Newest first. <paramref name="since"/> returns only photos created after that
        /// instant, which is how the guest feed polls without re-fetching the whole gallery
        /// every ten seconds. Must be UTC.
        /// </summary>
        Task<IEnumerable<Photo>> GetPhotosByEventAsync(Guid eventId, DateTime? since = null);
        /// <summary>
        /// Writes the event's media into <paramref name="output"/> as it goes. Nothing is
        /// buffered: the previous version built the whole archive in memory and then copied
        /// it again into a byte array, so a five gigabyte gallery needed ten gigabytes of RAM.
        /// </summary>
        Task WriteZipArchiveToAsync(Guid eventId, Stream output);
        Task DeletePhotoAsync(Guid photoId);

        /// <summary>
        /// Removes every photo of an event, rows and stored files alike. Called when an event
        /// is deleted: the database cascade would drop the rows on its own but leave every
        /// byte on the volume with nothing left pointing at it.
        /// </summary>
        Task DeletePhotosForEventAsync(Guid eventId);
    }
}
