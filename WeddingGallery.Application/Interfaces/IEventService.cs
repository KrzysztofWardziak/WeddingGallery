using WeddingGallery.Domain.Entities;

namespace WeddingGallery.Application.Interfaces
{
    public interface IEventService
    {
        Task<Event> CreateEventAsync(string name);
        Task<Event?> GetEventBySlugAsync(string slug);
    }
}
