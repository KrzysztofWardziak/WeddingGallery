using System.IO.Compression;
using WeddingGallery.Application.Interfaces;
using WeddingGallery.Application.Media;
using WeddingGallery.Domain;
using WeddingGallery.Domain.Entities;
using WeddingGallery.Domain.Interfaces;

namespace WeddingGallery.Application.Services
{
    public class PhotoService : IPhotoService
    {
        /// <summary>
        /// Stand-in for guests who upload without naming themselves. Normalising here rather
        /// than at each display site keeps the gallery, the admin grid and the ZIP entry names
        /// working off one value instead of three separate fallbacks.
        /// </summary>
        public const string AnonymousUploaderName = "Gość";

        private readonly IPhotoRepository _photoRepository;
        private readonly IMediaFileValidator _validator;
        private readonly IThumbnailGenerator _thumbnailGenerator;
        private readonly string _uploadPath;

        public PhotoService(
            IPhotoRepository photoRepository,
            IMediaFileValidator validator,
            IThumbnailGenerator thumbnailGenerator,
            PhotoStorageOptions storageOptions)
        {
            _photoRepository = photoRepository;
            _validator = validator;
            _thumbnailGenerator = thumbnailGenerator;
            _uploadPath = storageOptions.RootPath;

            if (!Directory.Exists(_uploadPath))
            {
                Directory.CreateDirectory(_uploadPath);
            }
        }

        public async Task<Photo> UploadPhotoAsync(Guid eventId, string? uploaderName, UploadedFile file)
        {
            var mediaType = ValidateOrThrow(file);
            return await SaveAsync(eventId, uploaderName, file, mediaType);
        }

        public async Task<IEnumerable<Photo>> UploadPhotosAsync(Guid eventId, string? uploaderName, IEnumerable<UploadedFile> files)
        {
            // Validate the whole batch up front: validation only needs the name and size, so
            // rejecting late - after some files are already on disk and in the database - would
            // leave the upload half-applied with no way for the guest to tell what landed.
            var validated = files.Select(file => (File: file, MediaType: ValidateOrThrow(file))).ToList();

            var uploaded = new List<Photo>(validated.Count);
            foreach (var (file, mediaType) in validated)
            {
                uploaded.Add(await SaveAsync(eventId, uploaderName, file, mediaType));
            }

            return uploaded;
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
            if (photo == null)
            {
                return;
            }

            DeleteStoredFile(photo.OriginalPath);

            // For images ThumbPath is the original, but a video's poster frame is a separate
            // file that would otherwise be orphaned on the volume forever.
            if (!string.IsNullOrEmpty(photo.ThumbPath) && photo.ThumbPath != photo.OriginalPath)
            {
                DeleteStoredFile(photo.ThumbPath);
            }

            await _photoRepository.DeleteAsync(photo);
        }

        // The name field is optional in the picker, and it arrives with whatever whitespace the
        // phone keyboard added.
        private static string NormaliseUploaderName(string? uploaderName) =>
            string.IsNullOrWhiteSpace(uploaderName) ? AnonymousUploaderName : uploaderName.Trim();

        private string ValidateOrThrow(UploadedFile file)
        {
            var result = _validator.Validate(file.FileName, file.SizeInBytes);
            if (!result.IsValid)
            {
                throw new InvalidMediaFileException(result.Error!);
            }

            return result.MediaType!;
        }

        private async Task<Photo> SaveAsync(Guid eventId, string? uploaderName, UploadedFile file, string mediaType)
        {
            // file.FileName comes straight from IFormFile.FileName, which is attacker-controlled
            // and can contain path separators or ".." segments. Path.GetFileName strips any
            // directory portion so the stored file can never escape _uploadPath, while the
            // original name is still kept as the display name (Photo.FileName).
            var safeFileName = Path.GetFileName(file.FileName);
            var uniqueFileName = $"{Guid.NewGuid()}_{safeFileName}";
            var filePath = Path.Combine(_uploadPath, uniqueFileName);

            await using (var fileStreamOutput = new FileStream(filePath, FileMode.Create))
            {
                await file.Content.CopyToAsync(fileStreamOutput);
            }

            var photo = new Photo
            {
                EventId = eventId,
                FileName = file.FileName,
                UploaderName = NormaliseUploaderName(uploaderName),
                OriginalPath = $"/photos/{uniqueFileName}",
                ThumbPath = mediaType == MediaTypes.Video
                    ? await GenerateVideoThumbnailAsync(uniqueFileName, filePath)
                    : $"/photos/{uniqueFileName}", // Images are served at full size; no separate thumb yet.
                MediaType = mediaType,
                CreatedAt = DateTime.UtcNow
            };

            return await _photoRepository.AddAsync(photo);
        }

        private async Task<string> GenerateVideoThumbnailAsync(string uniqueFileName, string videoPath)
        {
            var thumbFileName = $"{Path.GetFileNameWithoutExtension(uniqueFileName)}_thumb.jpg";
            var thumbFilePath = Path.Combine(_uploadPath, thumbFileName);

            // An empty ThumbPath is a supported state: the gallery falls back to a placeholder
            // tile. Losing the guest's video because ffmpeg had a bad day is not acceptable.
            return await _thumbnailGenerator.TryGenerateVideoThumbnailAsync(videoPath, thumbFilePath)
                ? $"/photos/{thumbFileName}"
                : string.Empty;
        }

        private void DeleteStoredFile(string publicPath)
        {
            var filePath = Path.Combine(_uploadPath, Path.GetFileName(publicPath));
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }
}
