using WeddingGallery.Domain.Entities;

namespace WeddingGallery.Domain.Interfaces
{
    public interface IPhotoRepository : IRepository<Photo>
    {
        /// <summary>Newest first; <paramref name="since"/> filters to later creations only.</summary>
        Task<IEnumerable<Photo>> GetByEventIdAsync(Guid eventId, DateTime? since = null);
    }
}
