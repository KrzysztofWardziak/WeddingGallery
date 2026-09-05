using Microsoft.EntityFrameworkCore;
using WeddingGallery.Domain.Entities;
using WeddingGallery.Domain.Interfaces;
using WeddingGallery.Infrastructure.Data;

namespace WeddingGallery.Infrastructure.Repositories
{
    public class PhotoRepository : Repository<Photo>, IPhotoRepository
    {
        public PhotoRepository(WeddingGalleryDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<Photo>> GetByEventIdAsync(Guid eventId, DateTime? since = null)
        {
            var query = _dbSet.Where(p => p.EventId == eventId);

            // Branching here rather than folding the null check into the predicate keeps the
            // generated SQL free of a constant comparison EF would otherwise carry along.
            if (since.HasValue)
            {
                query = query.Where(p => p.CreatedAt > since.Value);
            }

            return await query.OrderByDescending(p => p.CreatedAt).ToListAsync();
        }
    }
}
