namespace WeddingGallery.Domain.Entities;

public class Photo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string OriginalPath { get; set; } = string.Empty;
    // Empty when thumbnail generation failed; the feed then renders a placeholder tile
    // rather than losing the upload.
    public string ThumbPath { get; set; } = string.Empty;

    // One of WeddingGallery.Domain.MediaTypes. Drives how the gallery renders the item.
    public string MediaType { get; set; } = MediaTypes.Image;

    public string UploaderName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Event Event { get; set; } = null!;
}
