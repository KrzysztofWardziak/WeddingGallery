using WeddingGallery.Domain;

namespace WeddingGallery.Application.Media;

public sealed class MediaFileValidator : IMediaFileValidator
{
    // Cloudflare's free plan rejects request bodies over 100 MB and the tunnel is the only
    // way into this deployment, so anything larger can never reach us. The client blocks
    // oversized files before sending, but that check is bypassable - this is the real gate.
    public const long MaxFileBytes = 95L * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = MediaTypes.Image,
        [".jpeg"] = MediaTypes.Image,
        [".png"] = MediaTypes.Image,
        [".webp"] = MediaTypes.Image,
        [".heic"] = MediaTypes.Image,
        [".heif"] = MediaTypes.Image,
        [".gif"] = MediaTypes.Image,
        [".mp4"] = MediaTypes.Video,
        [".mov"] = MediaTypes.Video,
        [".m4v"] = MediaTypes.Video,
        [".webm"] = MediaTypes.Video
    };

    public MediaValidationResult Validate(string fileName, long sizeInBytes)
    {
        if (sizeInBytes <= 0)
        {
            return MediaValidationResult.Invalid($"Plik {fileName} jest pusty.");
        }

        if (sizeInBytes > MaxFileBytes)
        {
            var sizeMb = sizeInBytes / (1024 * 1024);
            var limitMb = MaxFileBytes / (1024 * 1024);
            return MediaValidationResult.Invalid(
                $"Plik {fileName} jest za duży ({sizeMb} MB). Limit to {limitMb} MB — nagraj krótszy film " +
                "albo przełącz aparat na 1080p.");
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.TryGetValue(extension, out var mediaType))
        {
            return MediaValidationResult.Invalid(
                $"Plik {fileName} ma nieobsługiwany format. Wyślij zdjęcie (JPG, PNG, HEIC, WEBP, GIF) " +
                "lub film (MP4, MOV, M4V, WEBM).");
        }

        return MediaValidationResult.Valid(mediaType);
    }
}
