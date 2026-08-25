namespace WeddingGallery.Application.Interfaces
{
    public interface IThumbnailGenerator
    {
        /// <summary>
        /// Extracts a single frame from a video into a JPEG at <paramref name="thumbnailPath"/>.
        /// Returns false instead of throwing when the frame cannot be produced: a missing
        /// thumbnail must never cost the guest their upload.
        /// </summary>
        Task<bool> TryGenerateVideoThumbnailAsync(string videoPath, string thumbnailPath, CancellationToken cancellationToken = default);
    }
}
