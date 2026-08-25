namespace WeddingGallery.Application.Media;

public interface IMediaFileValidator
{
    /// <summary>
    /// Classifies an uploaded file as an image or a video, or rejects it. Judges the file
    /// name's extension and its size only - it never touches the stream.
    /// </summary>
    MediaValidationResult Validate(string fileName, long sizeInBytes);
}
