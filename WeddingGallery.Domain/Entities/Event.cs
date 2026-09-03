namespace WeddingGallery.Domain.Entities;

public class Event
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// The wedding day. DateOnly rather than DateTime because the day has no clock and no
    /// offset: stored as a timestamp it could round-trip through UTC and render as the day
    /// before for anyone east or west of the couple. Null for events created before the
    /// field existed, and for anyone who simply did not fill it in.
    /// </summary>
    public DateOnly? EventDate { get; set; }
    
    public ICollection<Photo> Photos { get; set; } = new List<Photo>();
}
