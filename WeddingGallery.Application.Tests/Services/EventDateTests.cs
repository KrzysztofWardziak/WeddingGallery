using WeddingGallery.Application.Services;
using WeddingGallery.Application.Tests.Doubles;
using WeddingGallery.Domain;
using WeddingGallery.Domain.Entities;
using WeddingGallery.Domain.Interfaces;

namespace WeddingGallery.Application.Tests.Services;

public class EventDateTests
{
    private readonly FakeEventRepository _repository = new();
    private readonly EventService _service;

    public EventDateTests()
    {
        _service = new EventService(_repository, new RecordingPhotoService(), new RecordingChunkedUploadService());
    }

    [Fact]
    public async Task Stores_the_date_it_was_given()
    {
        var date = new DateOnly(2026, 9, 12);

        var created = await _service.CreateEventAsync("Katarzyna i Krzysztof", date);

        Assert.Equal(date, created.EventDate);
        Assert.Equal(date, _repository.Stored.Single().EventDate);
    }

    [Fact]
    public async Task Leaves_the_date_unset_when_none_is_given()
    {
        // The field is optional, and an event without one must not be handed an invented date.
        var created = await _service.CreateEventAsync("Katarzyna i Krzysztof");

        Assert.Null(created.EventDate);
    }

    [Fact]
    public async Task Keeps_the_date_as_a_calendar_day_with_no_time_or_zone()
    {
        // DateOnly rather than DateTime is the whole point: a wedding day has no clock and no
        // offset, and a timestamptz round trip could render 12 September as the 11th.
        var created = await _service.CreateEventAsync("Wesele", new DateOnly(2026, 1, 1));

        Assert.Equal(2026, created.EventDate!.Value.Year);
        Assert.Equal(1, created.EventDate!.Value.Month);
        Assert.Equal(1, created.EventDate!.Value.Day);
    }

    private sealed class FakeEventRepository : IEventRepository
    {
        public List<Event> Stored { get; } = new();

        public Task<Event?> GetByIdAsync(Guid id) => Task.FromResult(Stored.FirstOrDefault(e => e.Id == id));
        public Task<Event?> GetBySlugAsync(string slug) =>
            Task.FromResult(Stored.FirstOrDefault(e => string.Equals(e.Slug, slug, StringComparison.OrdinalIgnoreCase)));
        public Task<IEnumerable<Event>> GetAllAsync() => Task.FromResult<IEnumerable<Event>>(Stored);
        public Task<Event> AddAsync(Event entity) { Stored.Add(entity); return Task.FromResult(entity); }
        public Task UpdateAsync(Event entity) => Task.CompletedTask;
        public Task DeleteAsync(Event entity) { Stored.Remove(entity); return Task.CompletedTask; }
        public Task<IReadOnlyList<EventSummary>> GetSummariesAsync() =>
            Task.FromResult<IReadOnlyList<EventSummary>>(Array.Empty<EventSummary>());
        public Task<EventSummary?> GetSummaryByIdAsync(Guid id) => Task.FromResult<EventSummary?>(null);
    }
}
