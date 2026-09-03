namespace WeddingGallery.Domain;

/// <summary>
/// Read model for the admin event list: an event plus how much media it holds. Lives in the
/// Domain rather than the Application layer so IEventRepository can return it without the
/// Domain taking a dependency on Application.
/// </summary>
public sealed record EventSummary(
    Guid Id,
    string Name,
    string Slug,
    DateOnly? EventDate,
    int PhotoCount,
    int VideoCount);
