using WeddingGallery.Application.Interfaces;
using WeddingGallery.Application.Media;
using WeddingGallery.Domain.Entities;

namespace WeddingGallery.Application.Tests.Doubles;

/// <summary>
/// Records the one call event deletion makes and refuses the rest, so a test that
/// accidentally exercises an unrelated code path fails loudly instead of passing quietly.
/// </summary>
public sealed class RecordingPhotoService : IPhotoService
{
    public List<Guid> DeletedForEvents { get; } = new();

    public Action? OnDelete { get; set; }

    public Task DeletePhotosForEventAsync(Guid eventId)
    {
        OnDelete?.Invoke();
        DeletedForEvents.Add(eventId);
        return Task.CompletedTask;
    }

    public Task<Photo> UploadPhotoAsync(Guid eventId, string? uploaderName, UploadedFile file) =>
        throw new NotSupportedException();

    public Task<IEnumerable<Photo>> UploadPhotosAsync(Guid eventId, string? uploaderName, IEnumerable<UploadedFile> files) =>
        throw new NotSupportedException();

    public Task<Photo> AdoptFileAsync(Guid eventId, string? uploaderName, string originalFileName, string mediaType, string sourceFilePath) =>
        throw new NotSupportedException();

    public Task<IEnumerable<Photo>> GetPhotosByEventAsync(Guid eventId) =>
        Task.FromResult<IEnumerable<Photo>>(Array.Empty<Photo>());

    public Task<(byte[] ZipFileBytes, string FileName)> GetZipArchiveOfEventPhotosAsync(Guid eventId) =>
        throw new NotSupportedException();

    public Task DeletePhotoAsync(Guid photoId) => throw new NotSupportedException();
}

public sealed class RecordingChunkedUploadService : IChunkedUploadService
{
    public List<Guid> AbandonedForEvents { get; } = new();

    public Task<int> AbandonForEventAsync(Guid eventId)
    {
        AbandonedForEvents.Add(eventId);
        return Task.FromResult(0);
    }

    public Task<UploadSession> StartAsync(Guid eventId, string? uploaderName, string fileName, long totalSize) =>
        throw new NotSupportedException();

    public Task<UploadSession?> GetAsync(Guid uploadId) => throw new NotSupportedException();

    public Task<long> AppendAsync(Guid uploadId, long offset, Stream content) => throw new NotSupportedException();

    public Task<Photo> CompleteAsync(Guid uploadId) => throw new NotSupportedException();

    public Task AbandonAsync(Guid uploadId) => throw new NotSupportedException();

    public Task<int> SweepAsync(TimeSpan maxAge) => throw new NotSupportedException();
}
