using WeddingGallery.Domain.Entities;

namespace WeddingGallery.Domain.Interfaces
{
    public interface IEventRepository : IRepository<Event>
    {
        Task<Event?> GetBySlugAsync(string slug);
    }
}
