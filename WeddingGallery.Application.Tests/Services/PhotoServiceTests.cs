using System.Text;
using WeddingGallery.Application.Interfaces;
using WeddingGallery.Application.Media;
using WeddingGallery.Application.Services;
using WeddingGallery.Domain;
using WeddingGallery.Domain.Entities;
using WeddingGallery.Domain.Interfaces;

namespace WeddingGallery.Application.Tests.Services;

public class PhotoServiceTests : IDisposable
{
    private readonly string _uploadRoot = Path.Combine(
        Path.GetTempPath(), "weddinggallery-tests", Guid.NewGuid().ToString("N"));

    private readonly FakePhotoRepository _repository = new();

    public void Dispose()
    {
        if (Directory.Exists(_uploadRoot))
        {
            Directory.Delete(_uploadRoot, recursive: true);
        }
    }

    private PhotoService CreateService(FakeThumbnailGenerator generator) =>
        new(_repository, new MediaFileValidator(), generator, new PhotoStorageOptions(_uploadRoot));

    private static UploadedFile File(string name, string content = "payload")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new UploadedFile(name, bytes.Length, new MemoryStream(bytes));
    }

    private string StoredPath(string publicPath) =>
        Path.Combine(_uploadRoot, Path.GetFileName(publicPath));

    [Fact]
    public async Task Stores_a_video_with_the_generated_poster_frame_as_its_thumbnail()
    {
        var generator = new FakeThumbnailGenerator(succeed: true);
        var service = CreateService(generator);

        var photo = await service.UploadPhotoAsync(Guid.NewGuid(), "Ania", File("first-dance.mp4"));

        Assert.Equal(MediaTypes.Video, photo.MediaType);
        Assert.EndsWith("_thumb.jpg", photo.ThumbPath);
        Assert.NotEqual(photo.OriginalPath, photo.ThumbPath);
        Assert.True(System.IO.File.Exists(StoredPath(photo.OriginalPath)));
        Assert.Single(generator.Calls);
    }

    [Fact]
    public async Task Keeps_the_video_when_thumbnail_generation_fails()
    {
        // The whole point of the fallback: ffmpeg failing must cost the guest a preview
        // image, never the video they just waited to upload.
        var generator = new FakeThumbnailGenerator(succeed: false);
        var service = CreateService(generator);

        var photo = await service.UploadPhotoAsync(Guid.NewGuid(), "Ania", File("speech.MOV"));

        Assert.Equal(MediaTypes.Video, photo.MediaType);
        Assert.Equal(string.Empty, photo.ThumbPath);
        Assert.True(System.IO.File.Exists(StoredPath(photo.OriginalPath)));
        Assert.Single(_repository.Saved);
    }

    [Fact]
    public async Task Does_not_invoke_ffmpeg_for_images()
    {
        var generator = new FakeThumbnailGenerator(succeed: true);
        var service = CreateService(generator);

        var photo = await service.UploadPhotoAsync(Guid.NewGuid(), "Ania", File("kiss.jpg"));

        Assert.Equal(MediaTypes.Image, photo.MediaType);
        Assert.Equal(photo.OriginalPath, photo.ThumbPath);
        Assert.Empty(generator.Calls);
    }

    [Fact]
    public async Task Rejects_a_batch_before_writing_anything_when_one_file_is_not_allowed()
    {
        var service = CreateService(new FakeThumbnailGenerator(succeed: true));
        var batch = new[] { File("kiss.jpg"), File("payload.exe") };

        await Assert.ThrowsAsync<InvalidMediaFileException>(
            () => service.UploadPhotosAsync(Guid.NewGuid(), "Ania", batch));

        Assert.Empty(_repository.Saved);
        Assert.Empty(Directory.GetFiles(_uploadRoot));
    }

    [Fact]
    public async Task Deleting_a_video_also_removes_its_thumbnail_file()
    {
        var generator = new FakeThumbnailGenerator(succeed: true, writeFile: true);
        var service = CreateService(generator);
        var photo = await service.UploadPhotoAsync(Guid.NewGuid(), "Ania", File("cake.webm"));

        var videoPath = StoredPath(photo.OriginalPath);
        var thumbPath = StoredPath(photo.ThumbPath);
        Assert.True(System.IO.File.Exists(thumbPath));

        await service.DeletePhotoAsync(photo.Id);

        Assert.False(System.IO.File.Exists(videoPath));
        Assert.False(System.IO.File.Exists(thumbPath));
        Assert.Empty(_repository.Saved);
    }

    private sealed class FakeThumbnailGenerator : IThumbnailGenerator
    {
        private readonly bool _succeed;
        private readonly bool _writeFile;

        public FakeThumbnailGenerator(bool succeed, bool writeFile = false)
        {
            _succeed = succeed;
            _writeFile = writeFile;
        }

        public List<(string VideoPath, string ThumbnailPath)> Calls { get; } = new();

        public Task<bool> TryGenerateVideoThumbnailAsync(string videoPath, string thumbnailPath, CancellationToken cancellationToken = default)
        {
            Calls.Add((videoPath, thumbnailPath));
            if (_succeed && _writeFile)
            {
                System.IO.File.WriteAllBytes(thumbnailPath, new byte[] { 0xFF, 0xD8 });
            }
            return Task.FromResult(_succeed);
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

        public Task<Photo?> GetByIdAsync(Guid id) =>
            Task.FromResult(Saved.FirstOrDefault(p => p.Id == id));

        public Task<IEnumerable<Photo>> GetAllAsync() =>
            Task.FromResult<IEnumerable<Photo>>(Saved);

        public Task<IEnumerable<Photo>> GetByEventIdAsync(Guid eventId) =>
            Task.FromResult<IEnumerable<Photo>>(Saved.Where(p => p.EventId == eventId).ToList());

        public Task UpdateAsync(Photo entity) => Task.CompletedTask;

        public Task DeleteAsync(Photo entity)
        {
            Saved.Remove(entity);
            return Task.CompletedTask;
        }
    }
}
