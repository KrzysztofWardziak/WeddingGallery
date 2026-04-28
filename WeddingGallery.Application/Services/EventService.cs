using WeddingGallery.Application.Interfaces;
using WeddingGallery.Domain.Entities;
using WeddingGallery.Domain.Interfaces;

namespace WeddingGallery.Application.Services
{
    public class EventService : IEventService
    {
        private readonly IEventRepository _eventRepository;

        public EventService(IEventRepository eventRepository)
        {
            _eventRepository = eventRepository;
        }

        public async Task<Event> CreateEventAsync(string name)
        {
            var newEvent = new Event
            {
                Name = name,
                Slug = Guid.NewGuid().ToString("N").Substring(0, 8),
                AccessToken = Guid.NewGuid().ToString("N")
            };

            return await _eventRepository.AddAsync(newEvent);
        }

        public async Task<Event?> GetEventBySlugAsync(string slug)
        {
            return await _eventRepository.GetBySlugAsync(slug);
        }
    }
}
