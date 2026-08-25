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

        public async Task<IEnumerable<Photo>> GetByEventIdAsync(Guid eventId)
        {
            return await _dbSet.Where(p => p.EventId == eventId).OrderByDescending(p => p.CreatedAt).ToListAsync();
        }
    }
}
