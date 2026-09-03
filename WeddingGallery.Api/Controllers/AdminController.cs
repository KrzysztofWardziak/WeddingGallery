using Microsoft.AspNetCore.Mvc;
using WeddingGallery.Application.Events;
using WeddingGallery.Application.Interfaces;
using WeddingGallery.Domain;

namespace WeddingGallery.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : AdminAuthorizedControllerBase
    {
        private readonly IEventService _eventService;
        private readonly IPhotoService _photoService;

        public AdminController(IEventService eventService, IPhotoService photoService, IConfiguration configuration)
            : base(configuration)
        {
            _eventService = eventService;
            _photoService = photoService;
        }

        public class LoginRequest { public string Password { get; set; } = string.Empty; }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            var expectedPassword = Configuration["AdminSettings:Password"];
            if (request.Password == expectedPassword)
            {
                return Ok(new { token = Configuration["AdminSettings:Token"] });
            }
            return Unauthorized("Invalid password");
        }

        public class CreateEventRequest { public string Name { get; set; } = string.Empty; }

        [HttpPost("events")]
        public async Task<IActionResult> CreateEvent([FromBody] CreateEventRequest request)
        {
            if (!ValidateToken()) return Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Name is required");

            var newEvent = await _eventService.CreateEventAsync(request.Name);
            return Ok(new {
                id = newEvent.Id,
                name = newEvent.Name,
                slug = newEvent.Slug,
                accessToken = newEvent.AccessToken
            });
        }

        // Backs the admin event list. Admin-only: the response pairs every event name with
        // its slug, which is the guest URL for that gallery.
        [HttpGet("events")]
        public async Task<IActionResult> GetEvents()
        {
            if (!ValidateToken()) return Unauthorized();

            var summaries = await _eventService.GetEventSummariesAsync();
            return Ok(summaries.Select(ToResponse));
        }

        [HttpGet("events/{id}")]
        public async Task<IActionResult> GetEvent(Guid id)
        {
            if (!ValidateToken()) return Unauthorized();

            var summary = await _eventService.GetEventSummaryAsync(id);
            if (summary is null) return NotFound();

            return Ok(ToResponse(summary));
        }

        private static object ToResponse(EventSummary summary) => new
        {
            id = summary.Id,
            name = summary.Name,
            slug = summary.Slug,
            photoCount = summary.PhotoCount,
            videoCount = summary.VideoCount
        };

        // Irreversible: takes the event, its photos and videos, their files on disk, and any
        // chunked upload still in flight. confirmName must match the event name, checked in the
        // service rather than only in the browser so a mistyped request cannot wipe a gallery.
        [HttpDelete("events/{id}")]
        public async Task<IActionResult> DeleteEvent(Guid id, [FromQuery] string? confirmName)
        {
            if (!ValidateToken()) return Unauthorized();

            var result = await _eventService.DeleteEventAsync(id, confirmName);

            return result switch
            {
                EventDeletionResult.Deleted => NoContent(),
                EventDeletionResult.NotFound => NotFound(),
                EventDeletionResult.NameMismatch =>
                    BadRequest("Wpisana nazwa nie zgadza się z nazwą wydarzenia."),
                _ => StatusCode(StatusCodes.Status500InternalServerError)
            };
        }

        [HttpDelete("photos/{id}")]
        public async Task<IActionResult> DeletePhoto(Guid id)
        {
            if (!ValidateToken()) return Unauthorized();

            await _photoService.DeletePhotoAsync(id);
            return NoContent();
        }
    }
}
