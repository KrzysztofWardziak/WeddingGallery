using Microsoft.EntityFrameworkCore;
using WeddingGallery.Domain;
using WeddingGallery.Domain.Entities;
using WeddingGallery.Domain.Interfaces;
using WeddingGallery.Infrastructure.Data;

namespace WeddingGallery.Infrastructure.Repositories
{
    public class EventRepository : Repository<Event>, IEventRepository
    {
        public EventRepository(WeddingGalleryDbContext context) : base(context)
        {
        }

        // Case-insensitive on both counts: a guest typing the URL off the printed card lands
        // on the gallery whatever they capitalise, and EventService uses the same lookup to
        // avoid minting two slugs that differ only in case. Postgres cannot serve a lowered
        // comparison from the unique index on Slug, but at one row per wedding the scan is free.
        public async Task<Event?> GetBySlugAsync(string slug)
        {
            var lowered = slug.ToLower();
            return await _dbSet.FirstOrDefaultAsync(e => e.Slug.ToLower() == lowered);
        }

        public async Task<IReadOnlyList<EventSummary>> GetSummariesAsync()
        {
            return await ToSummaries(_dbSet.OrderBy(e => e.Name)).ToListAsync();
        }

        public async Task<EventSummary?> GetSummaryByIdAsync(Guid id)
        {
            return await ToSummaries(_dbSet.Where(e => e.Id == id)).FirstOrDefaultAsync();
        }

        // Ordering and filtering happen on the entity query, before the projection: EF cannot
        // translate a Where or OrderBy that reads a property off the constructed EventSummary
        // and throws "could not be translated" at runtime.
        //
        // Projecting the counts inside the query keeps this to one round trip; loading the
        // events and counting their Photos collections would be a query per event.
        private static IQueryable<EventSummary> ToSummaries(IQueryable<Event> events) =>
            events.Select(e => new EventSummary(
                e.Id,
                e.Name,
                e.Slug,
                e.Photos.Count(p => p.MediaType == MediaTypes.Image),
                e.Photos.Count(p => p.MediaType == MediaTypes.Video)));
    }
}
