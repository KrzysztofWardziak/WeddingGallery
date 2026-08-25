using Microsoft.EntityFrameworkCore;
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

        public async Task<Event?> GetBySlugAsync(string slug)
        {
            return await _dbSet.FirstOrDefaultAsync(e => e.Slug == slug);
        }
    }
}
