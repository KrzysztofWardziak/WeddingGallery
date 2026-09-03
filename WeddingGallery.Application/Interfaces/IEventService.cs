using WeddingGallery.Application.Events;
using WeddingGallery.Domain;
using WeddingGallery.Domain.Entities;

namespace WeddingGallery.Application.Interfaces
{
    public interface IEventService
    {
        Task<Event> CreateEventAsync(string name);
        Task<Event?> GetEventBySlugAsync(string slug);

        /// <summary>Backs the admin event list.</summary>
        Task<IReadOnlyList<EventSummary>> GetEventSummariesAsync();

        /// <summary>Backs the admin event detail and QR print views, which address an event by id.</summary>
        Task<EventSummary?> GetEventSummaryAsync(Guid id);

        /// <summary>
        /// Permanently removes an event, every photo and video it holds, the files on disk and
        /// any chunked upload still in flight for it. There is no undo.
        /// <paramref name="confirmName"/> must match the event's name.
        /// </summary>
        Task<EventDeletionResult> DeleteEventAsync(Guid id, string? confirmName);
    }
}
