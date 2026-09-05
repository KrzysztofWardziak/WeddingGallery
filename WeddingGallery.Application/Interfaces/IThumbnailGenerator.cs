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

        /// <summary>
        /// Writes a downscaled JPEG of an image to <paramref name="thumbnailPath"/>. Returns
        /// false instead of throwing when it cannot: a phone photo is several megabytes and
        /// the gallery grid shows every one of them at once, but a missing thumbnail must
        /// still cost only bandwidth, never the upload.
        /// </summary>
        Task<bool> TryGenerateImageThumbnailAsync(string imagePath, string thumbnailPath, CancellationToken cancellationToken = default);
    }
}
