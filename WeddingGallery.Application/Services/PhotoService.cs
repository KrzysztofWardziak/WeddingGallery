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

        public async Task<IEnumerable<Photo>> GetPhotosByEventAsync(Guid eventId, DateTime? since = null)
        {
            return await _photoRepository.GetByEventIdAsync(eventId, since);
        }

        public async Task WriteZipArchiveToAsync(Guid eventId, Stream output)
        {
            var photos = await _photoRepository.GetByEventIdAsync(eventId);

            // leaveOpen: the response stream belongs to the caller, not to us.
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var photo in photos)
            {
                var filePath = Path.Combine(_uploadPath, Path.GetFileName(photo.OriginalPath));
                if (!File.Exists(filePath))
                {
                    continue;
                }

                // NoCompression on purpose: JPEG, HEIC and MP4 are already compressed, so
                // Deflate spends the home server's CPU to save nothing, and on video it can
                // even grow the file.
                var entry = archive.CreateEntry(BuildEntryName(photo, usedNames), CompressionLevel.NoCompression);

                await using var entryStream = entry.Open();
                await using var source = File.OpenRead(filePath);
                await source.CopyToAsync(entryStream);
            }
        }

        private static string BuildEntryName(Photo photo, ISet<string> usedNames)
        {
            // Both halves are attacker-controlled. Photo.FileName keeps the guest's original
            // name verbatim - only the on-disk path was ever sanitised - and the uploader name
            // is free text. A ".." or a separator in either produces an archive that a naive
            // extractor follows out of its target directory, on the machine of whoever
            // downloads it. Path.GetFileName strips both.
            var safeName = Path.GetFileName(photo.FileName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "plik";
            }

            var safeUploader = Path.GetFileName(photo.UploaderName);
            if (string.IsNullOrWhiteSpace(safeUploader))
            {
                safeUploader = AnonymousUploaderName;
            }

            var candidate = $"{safeUploader}_{safeName}";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }

            // Two guests can upload a file of the same name. A duplicate entry is legal in a
            // zip but silently overwrites on extraction, so the later copy gets a suffix
            // rather than disappearing from the couple's archive.
            var stem = Path.GetFileNameWithoutExtension(safeName);
            var extension = Path.GetExtension(safeName);

            for (var suffix = 2; ; suffix++)
            {
                candidate = $"{safeUploader}_{stem}-{suffix}{extension}";
                if (usedNames.Add(candidate))
                {
                    return candidate;
                }
            }
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

        public async Task DeletePhotosForEventAsync(Guid eventId)
        {
            var photos = await _photoRepository.GetByEventIdAsync(eventId);

            foreach (var photo in photos)
            {
                DeleteStoredFile(photo.OriginalPath);

                // A video's poster frame is a separate file; for images ThumbPath is the
                // original, so deleting it twice would be wrong.
                if (!string.IsNullOrEmpty(photo.ThumbPath) && photo.ThumbPath != photo.OriginalPath)
                {
                    DeleteStoredFile(photo.ThumbPath);
                }

                await _photoRepository.DeleteAsync(photo);
            }
        }

        // The client-supplied name is attacker-controlled and can contain path separators or
        // ".." segments. Path.GetFileName strips any directory portion so the stored file can
        // never escape _uploadPath, while the original is kept as the display name.
        private static string BuildStoredFileName(string originalFileName) =>
            $"{Guid.NewGuid()}_{Path.GetFileName(originalFileName)}";

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
            var uniqueFileName = BuildStoredFileName(file.FileName);
            var filePath = Path.Combine(_uploadPath, uniqueFileName);

            await using (var fileStreamOutput = new FileStream(filePath, FileMode.Create))
            {
                await file.Content.CopyToAsync(fileStreamOutput);
            }

            return await RecordAsync(eventId, uploaderName, file.FileName, uniqueFileName, filePath, mediaType);
        }

        public async Task<Photo> AdoptFileAsync(
            Guid eventId, string? uploaderName, string originalFileName, string mediaType, string sourceFilePath)
        {
            var uniqueFileName = BuildStoredFileName(originalFileName);
            var filePath = Path.Combine(_uploadPath, uniqueFileName);

            // Move, not copy: the chunked path has already written the whole file once, and
            // reading a 500 MB video back just to write it again is the cost this design exists
            // to avoid. Both directories sit on the same volume, so this is a rename.
            File.Move(sourceFilePath, filePath, overwrite: false);

            return await RecordAsync(eventId, uploaderName, originalFileName, uniqueFileName, filePath, mediaType);
        }

        private async Task<Photo> RecordAsync(
            Guid eventId, string? uploaderName, string originalFileName, string uniqueFileName, string filePath, string mediaType)
        {
            var photo = new Photo
            {
                EventId = eventId,
                FileName = originalFileName,
                UploaderName = NormaliseUploaderName(uploaderName),
                OriginalPath = $"/photos/{uniqueFileName}",
                ThumbPath = await GenerateThumbnailAsync(uniqueFileName, filePath, mediaType),
                MediaType = mediaType,
                CreatedAt = DateTime.UtcNow
            };

            return await _photoRepository.AddAsync(photo);
        }

        // The gallery grid renders every tile at once, so serving originals there meant a
        // guest pulled several megabytes per photo through the couple's home upstream. A
        // 640px thumbnail is roughly fifty times smaller.
        private async Task<string> GenerateThumbnailAsync(string uniqueFileName, string sourcePath, string mediaType)
        {
            var thumbFileName = $"{Path.GetFileNameWithoutExtension(uniqueFileName)}_thumb.jpg";
            var thumbFilePath = Path.Combine(_uploadPath, thumbFileName);

            var generated = mediaType == MediaTypes.Video
                ? await _thumbnailGenerator.TryGenerateVideoThumbnailAsync(sourcePath, thumbFilePath)
                : await _thumbnailGenerator.TryGenerateImageThumbnailAsync(sourcePath, thumbFilePath);

            if (generated)
            {
                return $"/photos/{thumbFileName}";
            }

            // The two media types fail differently on purpose. A video without a poster frame
            // gets an empty ThumbPath and the gallery draws a placeholder, because there is no
            // still to fall back to. An image falls back to its own original - heavier than we
            // want, but a correct picture rather than a broken tile, and the only behaviour
            // available for formats this ffmpeg cannot decode, HEIC among them.
            return mediaType == MediaTypes.Video ? string.Empty : $"/photos/{uniqueFileName}";
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
