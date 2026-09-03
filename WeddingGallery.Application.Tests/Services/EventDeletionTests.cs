using WeddingGallery.Application.Events;
using WeddingGallery.Application.Services;
using WeddingGallery.Application.Tests.Doubles;
using WeddingGallery.Domain;
using WeddingGallery.Domain.Entities;
using WeddingGallery.Domain.Interfaces;

namespace WeddingGallery.Application.Tests.Services;

public class EventDeletionTests
{
    private readonly FakeEventRepository _events = new();
    private readonly RecordingPhotoService _photos = new();
    private readonly RecordingChunkedUploadService _uploads = new();
    private readonly EventService _service;

    public EventDeletionTests()
    {
        _service = new EventService(_events, _photos, _uploads);
    }

    private Event GivenEvent(string name = "Katarzyna i Krzysztof")
    {
        var weddingEvent = new Event { Name = name, Slug = "katarzyna-i-krzysztof" };
        _events.Stored.Add(weddingEvent);
        return weddingEvent;
    }

    [Fact]
    public async Task Deletes_the_event_its_media_and_its_upload_sessions()
    {
        var weddingEvent = GivenEvent();

        var result = await _service.DeleteEventAsync(weddingEvent.Id, weddingEvent.Name);

        Assert.Equal(EventDeletionResult.Deleted, result);
        Assert.Empty(_events.Stored);
        // Files are deleted explicitly: the database cascade removes Photo rows but would
        // leave every byte on the volume with nothing pointing at it.
        Assert.Equal(new[] { weddingEvent.Id }, _photos.DeletedForEvents);
        Assert.Equal(new[] { weddingEvent.Id }, _uploads.AbandonedForEvents);
    }

    [Fact]
    public async Task Refuses_when_the_typed_name_does_not_match_and_touches_nothing()
    {
        var weddingEvent = GivenEvent();

        var result = await _service.DeleteEventAsync(weddingEvent.Id, "Kasia i Krzysiek");

        Assert.Equal(EventDeletionResult.NameMismatch, result);
        Assert.Single(_events.Stored);
        Assert.Empty(_photos.DeletedForEvents);
        Assert.Empty(_uploads.AbandonedForEvents);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Refuses_a_blank_confirmation(string confirmName)
    {
        var weddingEvent = GivenEvent();

        var result = await _service.DeleteEventAsync(weddingEvent.Id, confirmName);

        Assert.Equal(EventDeletionResult.NameMismatch, result);
        Assert.Single(_events.Stored);
    }

    [Theory]
    [InlineData("  Katarzyna i Krzysztof  ")]
    [InlineData("katarzyna i krzysztof")]
    public async Task Accepts_a_confirmation_that_differs_only_in_case_or_padding(string confirmName)
    {
        // The admin is retyping a name off the screen, often on a phone keyboard that
        // capitalises for them. Demanding byte equality would fail honest attempts.
        var weddingEvent = GivenEvent();

        var result = await _service.DeleteEventAsync(weddingEvent.Id, confirmName);

        Assert.Equal(EventDeletionResult.Deleted, result);
        Assert.Empty(_events.Stored);
    }

    [Fact]
    public async Task Reports_an_unknown_event_rather_than_pretending_to_delete_it()
    {
        var result = await _service.DeleteEventAsync(Guid.NewGuid(), "cokolwiek");

        Assert.Equal(EventDeletionResult.NotFound, result);
        Assert.Empty(_photos.DeletedForEvents);
    }

    [Fact]
    public async Task Removes_media_before_the_event_row()
    {
        // Order matters: dropping the row first lets the cascade take the Photo rows away,
        // and the file paths with them, leaving the bytes unreachable.
        var weddingEvent = GivenEvent();
        var order = new List<string>();
        _photos.OnDelete = () => order.Add("media");
        _events.OnDelete = () => order.Add("event");

        await _service.DeleteEventAsync(weddingEvent.Id, weddingEvent.Name);

        Assert.Equal(new[] { "media", "event" }, order);
    }

    private sealed class FakeEventRepository : IEventRepository
    {
        public List<Event> Stored { get; } = new();
        public Action? OnDelete { get; set; }

        public Task<Event?> GetByIdAsync(Guid id) => Task.FromResult(Stored.FirstOrDefault(e => e.Id == id));
        public Task<Event?> GetBySlugAsync(string slug) =>
            Task.FromResult(Stored.FirstOrDefault(e => string.Equals(e.Slug, slug, StringComparison.OrdinalIgnoreCase)));
        public Task<IEnumerable<Event>> GetAllAsync() => Task.FromResult<IEnumerable<Event>>(Stored);
        public Task<Event> AddAsync(Event entity) { Stored.Add(entity); return Task.FromResult(entity); }
        public Task UpdateAsync(Event entity) => Task.CompletedTask;

        public Task DeleteAsync(Event entity)
        {
            OnDelete?.Invoke();
            Stored.Remove(entity);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EventSummary>> GetSummariesAsync() =>
            Task.FromResult<IReadOnlyList<EventSummary>>(Array.Empty<EventSummary>());
        public Task<EventSummary?> GetSummaryByIdAsync(Guid id) => Task.FromResult<EventSummary?>(null);
    }

}
