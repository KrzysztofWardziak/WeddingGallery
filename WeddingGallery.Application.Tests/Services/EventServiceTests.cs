using WeddingGallery.Application.Events;
using WeddingGallery.Application.Services;
using WeddingGallery.Application.Tests.Doubles;
using WeddingGallery.Domain;
using WeddingGallery.Domain.Entities;
using WeddingGallery.Domain.Interfaces;

namespace WeddingGallery.Application.Tests.Services;

public class EventServiceTests
{
    private readonly FakeEventRepository _repository = new();
    private readonly EventService _service;

    public EventServiceTests()
    {
        // Slug behaviour does not touch media; the doubles refuse any call it should not make.
        _service = new EventService(_repository, new RecordingPhotoService(), new RecordingChunkedUploadService());
    }

    [Fact]
    public async Task Derives_the_slug_from_the_event_name()
    {
        var created = await _service.CreateEventAsync("Katarzyna i Krzysztof");

        Assert.Equal("Katarzyna-i-Krzysztof", created.Slug);
        Assert.Equal("Katarzyna i Krzysztof", created.Name);
        Assert.NotEmpty(created.AccessToken);
    }

    [Fact]
    public async Task Numbers_the_slug_when_an_event_of_the_same_name_already_exists()
    {
        await _service.CreateEventAsync("Katarzyna i Krzysztof");
        var second = await _service.CreateEventAsync("Katarzyna i Krzysztof");
        var third = await _service.CreateEventAsync("Katarzyna i Krzysztof");

        Assert.Equal("Katarzyna-i-Krzysztof-2", second.Slug);
        Assert.Equal("Katarzyna-i-Krzysztof-3", third.Slug);
    }

    [Fact]
    public async Task Treats_a_slug_differing_only_in_case_as_taken()
    {
        // Guest lookup is case-insensitive, so two slugs differing only in capitalisation
        // would make the printed URL ambiguous.
        await _service.CreateEventAsync("Katarzyna i Krzysztof");
        var second = await _service.CreateEventAsync("KATARZYNA I KRZYSZTOF");

        Assert.Equal("KATARZYNA-I-KRZYSZTOF-2", second.Slug);
    }

    [Fact]
    public async Task Falls_back_to_a_random_slug_when_the_name_yields_none()
    {
        var created = await _service.CreateEventAsync("🎉🎉🎉");

        Assert.NotEmpty(created.Slug);
        Assert.Matches("^[0-9a-f]+$", created.Slug);
    }

    [Fact]
    public async Task Keeps_the_slug_within_the_database_column()
    {
        var created = await _service.CreateEventAsync(new string('a', 200));
        var duplicate = await _service.CreateEventAsync(new string('a', 200));

        Assert.True(created.Slug.Length <= SlugGenerator.MaxLength);
        Assert.True(duplicate.Slug.Length <= EventService.SlugColumnLength);
    }

    private sealed class FakeEventRepository : IEventRepository
    {
        public List<Event> Saved { get; } = new();

        public Task<Event> AddAsync(Event entity)
        {
            Saved.Add(entity);
            return Task.FromResult(entity);
        }

        // Mirrors the real repository, which lowers both sides of the comparison.
        public Task<Event?> GetBySlugAsync(string slug) =>
            Task.FromResult(Saved.FirstOrDefault(
                e => string.Equals(e.Slug, slug, StringComparison.OrdinalIgnoreCase)));

        public Task<Event?> GetByIdAsync(Guid id) =>
            Task.FromResult(Saved.FirstOrDefault(e => e.Id == id));

        public Task<IEnumerable<Event>> GetAllAsync() =>
            Task.FromResult<IEnumerable<Event>>(Saved);

        public Task<IReadOnlyList<EventSummary>> GetSummariesAsync() =>
            Task.FromResult<IReadOnlyList<EventSummary>>(Saved.Select(ToSummary).ToList());

        public Task<EventSummary?> GetSummaryByIdAsync(Guid id) =>
            Task.FromResult(Saved.Where(e => e.Id == id).Select(ToSummary).FirstOrDefault());

        public Task UpdateAsync(Event entity) => Task.CompletedTask;

        public Task DeleteAsync(Event entity)
        {
            Saved.Remove(entity);
            return Task.CompletedTask;
        }

        private static EventSummary ToSummary(Event e) => new(
            e.Id,
            e.Name,
            e.Slug,
            e.Photos.Count(p => p.MediaType == MediaTypes.Image),
            e.Photos.Count(p => p.MediaType == MediaTypes.Video));
    }
}
