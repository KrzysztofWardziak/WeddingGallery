namespace WeddingGallery.Application.Media;

/// <summary>
/// Outcome of checking one uploaded file. Either MediaType is set (valid) or Error is
/// set with a guest-facing message; never both.
/// </summary>
public sealed record MediaValidationResult
{
    private MediaValidationResult(string? mediaType, string? error)
    {
        MediaType = mediaType;
        Error = error;
    }

    public string? MediaType { get; }

    public string? Error { get; }

    public bool IsValid => Error is null;

    public static MediaValidationResult Valid(string mediaType) => new(mediaType, null);

    public static MediaValidationResult Invalid(string error) => new(null, error);
}
