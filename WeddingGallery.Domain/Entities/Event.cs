namespace WeddingGallery.Domain.Entities;

public class Event
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    
    public ICollection<Photo> Photos { get; set; } = new List<Photo>();
}
