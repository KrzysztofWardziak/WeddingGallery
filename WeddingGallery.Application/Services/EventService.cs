using WeddingGallery.Application.Events;
using WeddingGallery.Application.Interfaces;
using WeddingGallery.Domain;
using WeddingGallery.Domain.Entities;
using WeddingGallery.Domain.Interfaces;

namespace WeddingGallery.Application.Services
{
    public class EventService : IEventService
    {
        /// <summary>Mirrors the HasMaxLength configured for Event.Slug.</summary>
        public const int SlugColumnLength = 50;

        // Enough numbered candidates to cover any plausible run of same-named weddings
        // without letting a pathological case loop against the database forever.
        private const int MaxNumberedSlugAttempts = 100;

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
                Slug = await ResolveSlugAsync(name),
                AccessToken = Guid.NewGuid().ToString("N")
            };

            return await _eventRepository.AddAsync(newEvent);
        }

        // A slug read off the name is what makes the printed QR code URL legible and
        // re-typable, so we prefer one and fall back to a random string only when the name
        // leaves nothing usable - an all-emoji name, say.
        private async Task<string> ResolveSlugAsync(string name)
        {
            var baseSlug = SlugGenerator.Generate(name);
            if (baseSlug.Length == 0) return RandomSlug();

            // GetBySlugAsync matches case-insensitively, so this also rejects a candidate
            // that would differ from an existing slug only in capitalisation: two such
            // slugs would make the guest URL ambiguous.
            if (await IsSlugFreeAsync(baseSlug)) return baseSlug;

            for (var suffix = 2; suffix <= MaxNumberedSlugAttempts; suffix++)
            {
                var candidate = $"{baseSlug}-{suffix}";
                if (await IsSlugFreeAsync(candidate)) return candidate;
            }

            return RandomSlug();
        }

        private async Task<bool> IsSlugFreeAsync(string slug) =>
            await _eventRepository.GetBySlugAsync(slug) is null;

        private static string RandomSlug() => Guid.NewGuid().ToString("N").Substring(0, 8);

        public async Task<Event?> GetEventBySlugAsync(string slug)
        {
            return await _eventRepository.GetBySlugAsync(slug);
        }

        public async Task<IReadOnlyList<EventSummary>> GetEventSummariesAsync()
        {
            return await _eventRepository.GetSummariesAsync();
        }

        public async Task<EventSummary?> GetEventSummaryAsync(Guid id)
        {
            return await _eventRepository.GetSummaryByIdAsync(id);
        }
    }
}
