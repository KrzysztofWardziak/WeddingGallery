using System.IO.Compression;
using WeddingGallery.Application.Interfaces;
using WeddingGallery.Domain.Entities;
using WeddingGallery.Domain.Interfaces;

namespace WeddingGallery.Application.Services
{
    public class PhotoService : IPhotoService
    {
        private readonly IPhotoRepository _photoRepository;
        private readonly string _uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "photos");

        public PhotoService(IPhotoRepository photoRepository)
        {
            _photoRepository = photoRepository;
            if (!Directory.Exists(_uploadPath))
            {
                Directory.CreateDirectory(_uploadPath);
            }
        }

        public async Task<Photo> UploadPhotoAsync(Guid eventId, string uploaderName, string fileName, Stream fileStream)
        {
            var uniqueFileName = $"{Guid.NewGuid()}_{fileName}";
            var filePath = Path.Combine(_uploadPath, uniqueFileName);

            using (var fileStreamOutput = new FileStream(filePath, FileMode.Create))
            {
                await fileStream.CopyToAsync(fileStreamOutput);
            }

            var photo = new Photo
            {
                EventId = eventId,
                FileName = fileName,
                OriginalPath = $"/photos/{uniqueFileName}",
                ThumbPath = $"/photos/{uniqueFileName}", // Simplification: thumb is the same file for now
                UploaderName = uploaderName,
                CreatedAt = DateTime.UtcNow
            };

            return await _photoRepository.AddAsync(photo);
        }

        public async Task<IEnumerable<Photo>> UploadPhotosAsync(Guid eventId, string uploaderName, IEnumerable<(string fileName, Stream fileStream)> files)
        {
            var uploadedPhotos = new List<Photo>();
            foreach (var file in files)
            {
                var uniqueFileName = $"{Guid.NewGuid()}_{file.fileName}";
                var filePath = Path.Combine(_uploadPath, uniqueFileName);

                using (var fileStreamOutput = new FileStream(filePath, FileMode.Create))
                {
                    await file.fileStream.CopyToAsync(fileStreamOutput);
                }

                var photo = new Photo
                {
                    EventId = eventId,
                    FileName = file.fileName,
                    OriginalPath = $"/photos/{uniqueFileName}",
                    ThumbPath = $"/photos/{uniqueFileName}",
                    UploaderName = uploaderName,
                    CreatedAt = DateTime.UtcNow
                };

                var savedPhoto = await _photoRepository.AddAsync(photo);
                uploadedPhotos.Add(savedPhoto);
            }
            return uploadedPhotos;
        }

        public async Task<IEnumerable<Photo>> GetPhotosByEventAsync(Guid eventId)
        {
            return await _photoRepository.GetByEventIdAsync(eventId);
        }

        public async Task<(byte[] ZipFileBytes, string FileName)> GetZipArchiveOfEventPhotosAsync(Guid eventId)
        {
            var photos = await _photoRepository.GetByEventIdAsync(eventId);
            using var memoryStream = new MemoryStream();
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                foreach (var photo in photos)
                {
                    var filePath = Path.Combine(_uploadPath, Path.GetFileName(photo.OriginalPath));
                    if (File.Exists(filePath))
                    {
                        var entryName = $"{photo.UploaderName}_{photo.FileName}";
                        archive.CreateEntryFromFile(filePath, entryName);
                    }
                }
            }
            return (memoryStream.ToArray(), $"Wesele_Galeria.zip");
        }

        public async Task DeletePhotoAsync(Guid photoId)
        {
            var photo = await _photoRepository.GetByIdAsync(photoId);
            if (photo != null)
            {
                var filePath = Path.Combine(_uploadPath, Path.GetFileName(photo.OriginalPath));
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                await _photoRepository.DeleteAsync(photo);
            }
        }
    }
}
