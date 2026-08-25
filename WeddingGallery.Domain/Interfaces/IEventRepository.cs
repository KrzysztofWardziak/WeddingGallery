using WeddingGallery.Domain.Entities;

namespace WeddingGallery.Domain.Interfaces
{
    public interface IEventRepository : IRepository<Event>
    {
        Task<Event?> GetBySlugAsync(string slug);

        /// <summary>Every event with its media counts, resolved in a single round trip.</summary>
        Task<IReadOnlyList<EventSummary>> GetSummariesAsync();

        Task<EventSummary?> GetSummaryByIdAsync(Guid id);
    }
}
