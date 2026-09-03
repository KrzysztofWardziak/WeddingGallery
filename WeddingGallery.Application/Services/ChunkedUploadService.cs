using System.Collections.Concurrent;
using System.Text.Json;
using WeddingGallery.Application.Interfaces;
using WeddingGallery.Application.Media;
using WeddingGallery.Domain.Entities;

namespace WeddingGallery.Application.Services
{
    public sealed class ChunkedUploadService : IChunkedUploadService
    {
        /// <summary>
        /// Advisory chunk size handed to the client. Comfortably under the 100 MB request limit
        /// Cloudflare enforces, and small enough that a chunk dropped on venue wifi costs
        /// little to resend. The server appends whatever arrives, so this is guidance only.
        /// </summary>
        public const int ChunkSizeBytes = 25 * 1024 * 1024;

        /// <summary>
        /// Per-file ceiling, roughly three minutes of 4K/30. Cloudflare no longer bounds the
        /// file once it is chunked, so this exists only to stop one guest filling the volume.
        /// </summary>
        public const long MaxFileBytes = 500L * 1024 * 1024;

        private const string PartExtension = ".part";
        private const string MetadataExtension = ".json";

        // Two appends to one partial file would interleave and corrupt it. The offset check
        // narrows that race but does not close it. A single API container makes an in-process
        // lock sufficient; scaling out is where this assumption would first break.
        //
        // Entries are never removed. Evicting one while another request still holds it would
        // hand the next caller a different semaphore for the same upload, which is precisely
        // the interleaved write this guards against. The cost of keeping them is one small
        // object per upload session for the process lifetime.
        private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

        private readonly IPhotoService _photoService;
        private readonly IMediaFileValidator _validator;
        private readonly string _root;

        public ChunkedUploadService(
            IPhotoService photoService,
            IMediaFileValidator validator,
            ChunkedUploadStorageOptions storageOptions)
        {
            _photoService = photoService;
            _validator = validator;
            _root = storageOptions.RootPath;

            if (!Directory.Exists(_root))
            {
                Directory.CreateDirectory(_root);
            }
        }

        public async Task<UploadSession> StartAsync(Guid eventId, string? uploaderName, string fileName, long totalSize)
        {
            // Validate before a single byte travels: rejecting a 500 MB file only after it
            // arrived would burn the guest's whole connection for nothing.
            var validation = _validator.Validate(fileName, totalSize, MaxFileBytes);
            if (!validation.IsValid)
            {
                throw new InvalidMediaFileException(validation.Error!);
            }

            var metadata = new UploadMetadata(
                Guid.NewGuid(), eventId, uploaderName ?? string.Empty, fileName, totalSize, validation.MediaType!);

            await File.WriteAllTextAsync(MetadataPath(metadata.Id), JsonSerializer.Serialize(metadata));

            // Create the partial file immediately so its length is the single source of truth
            // for the offset from the very first request onwards.
            await using (File.Create(PartPath(metadata.Id))) { }

            return metadata.ToSession(offset: 0);
        }

        public async Task<UploadSession?> GetAsync(Guid uploadId)
        {
            var metadata = await ReadMetadataAsync(uploadId);
            return metadata?.ToSession(CurrentOffset(uploadId));
        }

        public async Task<long> AppendAsync(Guid uploadId, long offset, Stream content)
        {
            var gate = Locks.GetOrAdd(uploadId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                var metadata = await ReadMetadataAsync(uploadId) ?? throw new UploadSessionNotFoundException(uploadId);

                var actualOffset = CurrentOffset(uploadId);
                if (offset != actualOffset)
                {
                    // Usually a retry of a chunk that already landed. Reporting the true offset
                    // lets the client re-point instead of restarting the whole file.
                    throw new UploadOffsetMismatchException(offset, actualOffset);
                }

                long newOffset;
                await using (var part = new FileStream(PartPath(uploadId), FileMode.Append, FileAccess.Write))
                {
                    await content.CopyToAsync(part);
                    newOffset = part.Length;
                }

                if (newOffset > metadata.TotalSize)
                {
                    // More data than declared. Drop the session rather than keep a file whose
                    // contents nobody can vouch for.
                    await DeleteSessionAsync(uploadId);
                    throw new InvalidMediaFileException(
                        $"Plik {metadata.FileName} przysłał więcej danych niż zapowiedział. Wyślij go ponownie.");
                }

                return newOffset;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<Photo> CompleteAsync(Guid uploadId)
        {
            var gate = Locks.GetOrAdd(uploadId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                var metadata = await ReadMetadataAsync(uploadId) ?? throw new UploadSessionNotFoundException(uploadId);

                var received = CurrentOffset(uploadId);
                if (received != metadata.TotalSize)
                {
                    // The session stays put so the guest resumes the remainder rather than
                    // resending everything.
                    throw new IncompleteUploadException(received, metadata.TotalSize);
                }

                var photo = await _photoService.AdoptFileAsync(
                    metadata.EventId, metadata.UploaderName, metadata.FileName, metadata.MediaType, PartPath(uploadId));

                // AdoptFileAsync moved the partial file into the gallery; only the sidecar left.
                DeleteIfExists(MetadataPath(uploadId));

                return photo;
            }
            finally
            {
                gate.Release();
            }
        }

        public Task AbandonAsync(Guid uploadId) => DeleteSessionAsync(uploadId);

        public Task<int> SweepAsync(TimeSpan maxAge)
        {
            if (!Directory.Exists(_root))
            {
                return Task.FromResult(0);
            }

            var cutoff = DateTime.UtcNow - maxAge;
            var removed = 0;

            // Age comes from the filesystem rather than process memory, so sessions orphaned
            // by a restart are collected too.
            foreach (var metadataPath in Directory.GetFiles(_root, $"*{MetadataExtension}"))
            {
                if (File.GetLastWriteTimeUtc(metadataPath) > cutoff)
                {
                    continue;
                }

                if (!Guid.TryParse(Path.GetFileNameWithoutExtension(metadataPath), out var uploadId))
                {
                    continue;
                }

                DeleteIfExists(PartPath(uploadId));
                DeleteIfExists(metadataPath);
                removed++;
            }

            return Task.FromResult(removed);
        }

        public async Task<int> AbandonForEventAsync(Guid eventId)
        {
            if (!Directory.Exists(_root))
            {
                return 0;
            }

            var removed = 0;

            foreach (var metadataPath in Directory.GetFiles(_root, $"*{MetadataExtension}"))
            {
                if (!Guid.TryParse(Path.GetFileNameWithoutExtension(metadataPath), out var uploadId))
                {
                    continue;
                }

                var metadata = await ReadMetadataAsync(uploadId);
                if (metadata is null || metadata.EventId != eventId)
                {
                    continue;
                }

                await DeleteSessionAsync(uploadId);
                removed++;
            }

            return removed;
        }

        private Task DeleteSessionAsync(Guid uploadId)
        {
            DeleteIfExists(PartPath(uploadId));
            DeleteIfExists(MetadataPath(uploadId));
            return Task.CompletedTask;
        }

        private long CurrentOffset(Guid uploadId)
        {
            var part = new FileInfo(PartPath(uploadId));
            return part.Exists ? part.Length : 0;
        }

        private async Task<UploadMetadata?> ReadMetadataAsync(Guid uploadId)
        {
            var path = MetadataPath(uploadId);
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<UploadMetadata>(await File.ReadAllTextAsync(path));
        }

        private string PartPath(Guid uploadId) => Path.Combine(_root, $"{uploadId}{PartExtension}");

        private string MetadataPath(Guid uploadId) => Path.Combine(_root, $"{uploadId}{MetadataExtension}");

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private sealed record UploadMetadata(
            Guid Id,
            Guid EventId,
            string UploaderName,
            string FileName,
            long TotalSize,
            string MediaType)
        {
            public UploadSession ToSession(long offset) =>
                new(Id, EventId, UploaderName, FileName, TotalSize, MediaType, offset);
        }
    }
}
