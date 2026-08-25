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
    }
}
