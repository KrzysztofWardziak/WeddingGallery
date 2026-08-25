using Microsoft.AspNetCore.Mvc;
using WeddingGallery.Application.Interfaces;

namespace WeddingGallery.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EventsController : ControllerBase
    {
        private readonly IEventService _eventService;

        public EventsController(IEventService eventService)
        {
            _eventService = eventService;
        }

        [HttpGet("{slug}")]
        public async Task<IActionResult> GetBySlug(string slug)
        {
            var weddingEvent = await _eventService.GetEventBySlugAsync(slug);
            if (weddingEvent == null) return NotFound();

            return Ok(new { id = weddingEvent.Id, name = weddingEvent.Name, slug = weddingEvent.Slug });
        }
    }
}
