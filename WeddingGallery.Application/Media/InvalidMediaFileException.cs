namespace WeddingGallery.Application.Media;

/// <summary>
/// Thrown when an upload contains a file the gallery will not accept. The message is
/// guest-facing and is surfaced verbatim as the 400 response body.
/// </summary>
public sealed class InvalidMediaFileException : Exception
{
    public InvalidMediaFileException(string message) : base(message)
    {
    }
}
