using Microsoft.AspNetCore.Mvc;
using WeddingGallery.Application.Interfaces;
using WeddingGallery.Application.Media;

namespace WeddingGallery.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PhotosController : AdminAuthorizedControllerBase
    {
        private readonly IPhotoService _photoService;

        public PhotosController(IPhotoService photoService, IConfiguration configuration)
            : base(configuration)
        {
            _photoService = photoService;
        }

        [HttpGet("event/{eventId}")]
        public async Task<IActionResult> GetPhotos(Guid eventId)
        {
            var photos = await _photoService.GetPhotosByEventAsync(eventId);
            return Ok(photos.Select(p => new {
                id = p.Id,
                url = p.OriginalPath,
                thumbUrl = p.ThumbPath,
                mediaType = p.MediaType,
                uploaderName = p.UploaderName,
                uploadedAt = p.CreatedAt
            }));
        }

        // Full-gallery export. The event GUID is not a secret (it is returned by the
        // public GET /api/events/{slug} endpoint), so this must stay behind the admin
        // token or any guest who scanned the QR code could download the whole gallery.
        [HttpGet("event/{eventId}/download")]
        public async Task<IActionResult> DownloadAllPhotos(Guid eventId)
        {
            if (!ValidateToken()) return Unauthorized();

            var (zipBytes, fileName) = await _photoService.GetZipArchiveOfEventPhotosAsync(eventId);
            return File(zipBytes, "application/zip", fileName);
        }

        [HttpPost("upload")]
        // uploaderName is optional: guests may contribute without naming themselves, and
        // PhotoService substitutes a stand-in when it is blank.
        public async Task<IActionResult> UploadPhotos([FromForm] Guid eventId, [FromForm] string? uploaderName, List<IFormFile> files)
        {
            if (files == null || files.Count == 0)
                return BadRequest("No files uploaded");

            var fileData = files.Select(f => new UploadedFile(f.FileName, f.Length, f.OpenReadStream()));

            try
            {
                var uploadedPhotos = await _photoService.UploadPhotosAsync(eventId, uploaderName, fileData);
                return Ok(uploadedPhotos.Select(p => new { id = p.Id, url = p.OriginalPath, mediaType = p.MediaType }));
            }
            catch (InvalidMediaFileException ex)
            {
                // The message is written for the guest and is shown verbatim in the picker.
                return BadRequest(ex.Message);
            }
        }
    }
}
