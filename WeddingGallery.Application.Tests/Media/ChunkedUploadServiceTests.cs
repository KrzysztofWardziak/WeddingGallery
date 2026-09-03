using System.Text;
using WeddingGallery.Application.Interfaces;
using WeddingGallery.Application.Media;
using WeddingGallery.Application.Services;
using WeddingGallery.Domain;
using WeddingGallery.Domain.Entities;
using WeddingGallery.Domain.Interfaces;

namespace WeddingGallery.Application.Tests.Media;

public class ChunkedUploadServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "weddinggallery-chunked", Guid.NewGuid().ToString("N"));

    private readonly FakePhotoRepository _repository = new();
    private readonly ChunkedUploadService _service;
    private readonly PhotoService _photoService;

    public ChunkedUploadServiceTests()
    {
        var photosRoot = Path.Combine(_root, "photos");
        var tempRoot = Path.Combine(_root, "uploads-tmp");

        _photoService = new PhotoService(
            _repository,
            new MediaFileValidator(),
            new StubThumbnailGenerator(),
            new PhotoStorageOptions(photosRoot));

        _service = new ChunkedUploadService(
            _photoService,
            new MediaFileValidator(),
            new ChunkedUploadStorageOptions(tempRoot));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static Stream Bytes(byte[] payload) => new MemoryStream(payload);

    private static byte[] Payload(int length)
    {
        var payload = new byte[length];
        for (var i = 0; i < length; i++)
        {
            payload[i] = (byte)(i % 251);
        }
        return payload;
    }

    [Fact]
    public async Task Opens_a_session_at_offset_zero()
    {
        var session = await _service.StartAsync(Guid.NewGuid(), "Ania", "dance.mp4", 1024);

        Assert.Equal(0, session.Offset);
        Assert.Equal(1024, session.TotalSize);
        Assert.Equal(MediaTypes.Video, session.MediaType);
        Assert.Equal("Ania", session.UploaderName);
    }

    [Fact]
    public async Task Refuses_an_unsupported_format_before_a_session_exists()
    {
        await Assert.ThrowsAsync<InvalidMediaFileException>(
            () => _service.StartAsync(Guid.NewGuid(), "Ania", "payload.exe", 1024));

        Assert.Empty(Directory.Exists(Path.Combine(_root, "uploads-tmp"))
            ? Directory.GetFiles(Path.Combine(_root, "uploads-tmp"))
            : Array.Empty<string>());
    }

    [Fact]
    public async Task Refuses_a_file_over_the_chunked_cap()
    {
        await Assert.ThrowsAsync<InvalidMediaFileException>(
            () => _service.StartAsync(Guid.NewGuid(), "Ania", "dance.mp4",
                ChunkedUploadService.MaxFileBytes + 1));
    }

    [Fact]
    public async Task Accepts_a_file_far_larger_than_the_single_request_cap()
    {
        // The whole point of chunking: the per-request ceiling no longer bounds the file.
        var session = await _service.StartAsync(
            Guid.NewGuid(), "Ania", "dance.mp4", MediaFileValidator.MaxFileBytes * 4);

        Assert.Equal(0, session.Offset);
    }

    [Fact]
    public async Task Appending_advances_the_offset()
    {
        var session = await _service.StartAsync(Guid.NewGuid(), "Ania", "dance.mp4", 30);

        var offset = await _service.AppendAsync(session.Id, 0, Bytes(Payload(10)));

        Assert.Equal(10, offset);
        Assert.Equal(10, (await _service.GetAsync(session.Id))!.Offset);
    }

    [Fact]
    public async Task Appending_at_the_wrong_offset_is_refused_and_leaves_the_file_untouched()
    {
        var session = await _service.StartAsync(Guid.NewGuid(), "Ania", "dance.mp4", 30);
        await _service.AppendAsync(session.Id, 0, Bytes(Payload(10)));

        // A retry that already landed, or a client that lost track, must not corrupt the file.
        var mismatch = await Assert.ThrowsAsync<UploadOffsetMismatchException>(
            () => _service.AppendAsync(session.Id, 0, Bytes(Payload(10))));

        Assert.Equal(10, mismatch.ActualOffset);
        Assert.Equal(10, (await _service.GetAsync(session.Id))!.Offset);
    }

    [Fact]
    public async Task Reassembles_the_original_bytes_exactly()
    {
        var payload = Payload(2500);
        var session = await _service.StartAsync(Guid.NewGuid(), "Ania", "dance.mp4", payload.Length);

        var offset = 0L;
        foreach (var chunk in payload.Chunk(700))
        {
            offset = await _service.AppendAsync(session.Id, offset, Bytes(chunk));
        }

        var photo = await _service.CompleteAsync(session.Id);

        var storedPath = Path.Combine(_root, "photos", Path.GetFileName(photo.OriginalPath));
        Assert.Equal(payload, await System.IO.File.ReadAllBytesAsync(storedPath));
        Assert.Equal(MediaTypes.Video, photo.MediaType);
        Assert.Equal("dance.mp4", photo.FileName);
    }

    [Fact]
    public async Task Completing_short_of_the_declared_size_is_refused_and_keeps_the_session()
    {
        var session = await _service.StartAsync(Guid.NewGuid(), "Ania", "dance.mp4", 100);
        await _service.AppendAsync(session.Id, 0, Bytes(Payload(40)));

        await Assert.ThrowsAsync<IncompleteUploadException>(() => _service.CompleteAsync(session.Id));

        // Preserved, so the guest resumes the remaining 60 bytes instead of resending everything.
        Assert.Equal(40, (await _service.GetAsync(session.Id))!.Offset);
        Assert.Empty(_repository.Saved);
    }

    [Fact]
    public async Task Completing_clears_the_temporary_files()
    {
        var payload = Payload(50);
        var session = await _service.StartAsync(Guid.NewGuid(), "Ania", "dance.mp4", payload.Length);
        await _service.AppendAsync(session.Id, 0, Bytes(payload));

        await _service.CompleteAsync(session.Id);

        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "uploads-tmp")));
        Assert.Null(await _service.GetAsync(session.Id));
    }

    [Fact]
    public async Task Unknown_sessions_are_reported_rather_than_guessed_at()
    {
        Assert.Null(await _service.GetAsync(Guid.NewGuid()));

        await Assert.ThrowsAsync<UploadSessionNotFoundException>(
            () => _service.AppendAsync(Guid.NewGuid(), 0, Bytes(Payload(10))));

        await Assert.ThrowsAsync<UploadSessionNotFoundException>(
            () => _service.CompleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Abandoning_removes_the_temporary_files()
    {
        var session = await _service.StartAsync(Guid.NewGuid(), "Ania", "dance.mp4", 100);
        await _service.AppendAsync(session.Id, 0, Bytes(Payload(10)));

        await _service.AbandonAsync(session.Id);

        Assert.Null(await _service.GetAsync(session.Id));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "uploads-tmp")));
    }

    [Fact]
    public async Task Sweeper_removes_aged_sessions_and_spares_fresh_ones()
    {
        var stale = await _service.StartAsync(Guid.NewGuid(), "Ania", "stale.mp4", 100);
        var fresh = await _service.StartAsync(Guid.NewGuid(), "Ania", "fresh.mp4", 100);

        // Age is read off the filesystem so orphans survive a restart and still get collected.
        foreach (var file in Directory.GetFiles(Path.Combine(_root, "uploads-tmp"), $"{stale.Id}.*"))
        {
            System.IO.File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-48));
        }

        var removed = await _service.SweepAsync(TimeSpan.FromHours(24));

        Assert.Equal(1, removed);
        Assert.Null(await _service.GetAsync(stale.Id));
        Assert.NotNull(await _service.GetAsync(fresh.Id));
    }

    [Fact]
    public async Task Uses_the_generic_name_when_the_guest_gives_none()
    {
        var payload = Payload(20);
        var session = await _service.StartAsync(Guid.NewGuid(), "  ", "dance.mp4", payload.Length);
        await _service.AppendAsync(session.Id, 0, Bytes(payload));

        var photo = await _service.CompleteAsync(session.Id);

        Assert.Equal(PhotoService.AnonymousUploaderName, photo.UploaderName);
    }

    private sealed class StubThumbnailGenerator : IThumbnailGenerator
    {
        public Task<bool> TryGenerateVideoThumbnailAsync(string videoPath, string thumbnailPath, CancellationToken cancellationToken = default)
        {
            System.IO.File.WriteAllBytes(thumbnailPath, new byte[] { 0xFF, 0xD8 });
            return Task.FromResult(true);
        }
    }

    private sealed class FakePhotoRepository : IPhotoRepository
    {
        public List<Photo> Saved { get; } = new();

        public Task<Photo> AddAsync(Photo entity)
        {
            Saved.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<Photo?> GetByIdAsync(Guid id) => Task.FromResult(Saved.FirstOrDefault(p => p.Id == id));
        public Task<IEnumerable<Photo>> GetAllAsync() => Task.FromResult<IEnumerable<Photo>>(Saved);
        public Task<IEnumerable<Photo>> GetByEventIdAsync(Guid eventId) =>
            Task.FromResult<IEnumerable<Photo>>(Saved.Where(p => p.EventId == eventId).ToList());
        public Task UpdateAsync(Photo entity) => Task.CompletedTask;
        public Task DeleteAsync(Photo entity) { Saved.Remove(entity); return Task.CompletedTask; }
    }
}
