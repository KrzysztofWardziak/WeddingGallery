namespace WeddingGallery.Domain;

/// <summary>
/// The kinds of media a guest can contribute. Stored as text on Photo.MediaType so the
/// column stays readable in psql and needs no enum-conversion migration when the set grows.
/// </summary>
public static class MediaTypes
{
    public const string Image = "image";
    public const string Video = "video";
}
