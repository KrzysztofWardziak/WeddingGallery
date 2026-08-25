using WeddingGallery.Domain.Entities;

namespace WeddingGallery.Domain.Interfaces
{
    public interface IPhotoRepository : IRepository<Photo>
    {
        Task<IEnumerable<Photo>> GetByEventIdAsync(Guid eventId);
    }
}
