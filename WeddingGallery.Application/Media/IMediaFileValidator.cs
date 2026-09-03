namespace WeddingGallery.Application.Media;

public interface IMediaFileValidator
{
    /// <summary>
    /// Classifies an uploaded file as an image or a video, or rejects it. Judges the file
    /// name's extension and its size only - it never touches the stream.
    /// </summary>
    MediaValidationResult Validate(string fileName, long sizeInBytes);

    /// <summary>
    /// As above, but against a caller-supplied ceiling. Chunked uploads are bounded by disk
    /// rather than by the per-request limit, so they pass a much larger maximum.
    /// </summary>
    MediaValidationResult Validate(string fileName, long sizeInBytes, long maxBytes);
}
