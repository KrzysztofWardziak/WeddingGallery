using Microsoft.AspNetCore.Mvc;
using WeddingGallery.Application.Interfaces;

namespace WeddingGallery.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly IEventService _eventService;
        private readonly IPhotoService _photoService;
        private readonly IConfiguration _configuration;

        public AdminController(IEventService eventService, IPhotoService photoService, IConfiguration configuration)
        {
            _eventService = eventService;
            _photoService = photoService;
            _configuration = configuration;
        }

        private bool ValidateToken()
        {
            var expectedToken = _configuration["AdminSettings:Token"];
            var providedToken = Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "");
            return expectedToken == providedToken;
        }

        public class LoginRequest { public string Password { get; set; } = string.Empty; }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            var expectedPassword = _configuration["AdminSettings:Password"];
            if (request.Password == expectedPassword)
            {
                return Ok(new { token = _configuration["AdminSettings:Token"] });
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

        [HttpDelete("photos/{id}")]
        public async Task<IActionResult> DeletePhoto(Guid id)
        {
            if (!ValidateToken()) return Unauthorized();

            await _photoService.DeletePhotoAsync(id);
            return NoContent();
        }
    }
}
