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
        private readonly IPhotoService _photoService;
        private readonly IChunkedUploadService _chunkedUploadService;

        // Deleting an event genuinely spans all three concerns - rows, stored files and
        // in-flight uploads - so this service coordinates them. The alternative, orchestrating
        // from the controller, would put that ordering somewhere far harder to test.
        public EventService(
            IEventRepository eventRepository,
            IPhotoService photoService,
            IChunkedUploadService chunkedUploadService)
        {
            _eventRepository = eventRepository;
            _photoService = photoService;
            _chunkedUploadService = chunkedUploadService;
        }

        public async Task<Event> CreateEventAsync(string name, DateOnly? eventDate = null)
        {
            var newEvent = new Event
            {
                Name = name,
                Slug = await ResolveSlugAsync(name),
                EventDate = eventDate,
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

        public async Task<EventDeletionResult> DeleteEventAsync(Guid id, string? confirmName)
        {
            var weddingEvent = await _eventRepository.GetByIdAsync(id);
            if (weddingEvent is null)
            {
                return EventDeletionResult.NotFound;
            }

            // The admin is retyping a name off the screen, often on a keyboard that
            // capitalises for them, so case and padding are forgiven - but nothing else is,
            // and a blank confirmation never matches.
            if (string.IsNullOrWhiteSpace(confirmName) ||
                !string.Equals(confirmName.Trim(), weddingEvent.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return EventDeletionResult.NameMismatch;
            }

            // Media first. Dropping the event row ahead of this lets the cascade take the
            // Photo rows away, and the file paths with them, leaving the bytes unreachable.
            await _chunkedUploadService.AbandonForEventAsync(id);
            await _photoService.DeletePhotosForEventAsync(id);
            await _eventRepository.DeleteAsync(weddingEvent);

            return EventDeletionResult.Deleted;
        }
    }
}
