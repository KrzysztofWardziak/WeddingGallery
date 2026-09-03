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

        Task<IEnumerable<Photo>> GetPhotosByEventAsync(Guid eventId);
        Task<(byte[] ZipFileBytes, string FileName)> GetZipArchiveOfEventPhotosAsync(Guid eventId);
        Task DeletePhotoAsync(Guid photoId);
    }
}
