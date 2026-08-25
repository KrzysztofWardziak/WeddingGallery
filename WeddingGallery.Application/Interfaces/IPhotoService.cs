using WeddingGallery.Domain.Entities;

namespace WeddingGallery.Application.Interfaces
{
    public interface IPhotoService
    {
        Task<Photo> UploadPhotoAsync(Guid eventId, string uploaderName, string fileName, Stream fileStream);
        Task<IEnumerable<Photo>> UploadPhotosAsync(Guid eventId, string uploaderName, IEnumerable<(string fileName, Stream fileStream)> files);
        Task<IEnumerable<Photo>> GetPhotosByEventAsync(Guid eventId);
        Task<(byte[] ZipFileBytes, string FileName)> GetZipArchiveOfEventPhotosAsync(Guid eventId);
        Task DeletePhotoAsync(Guid photoId);
    }
}
