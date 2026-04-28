using Microsoft.AspNetCore.Mvc;
using WeddingGallery.Application.Interfaces;

namespace WeddingGallery.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PhotosController : ControllerBase
    {
        private readonly IPhotoService _photoService;

        public PhotosController(IPhotoService photoService)
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
                uploaderName = p.UploaderName,
                uploadedAt = p.CreatedAt
            }));
        }

        [HttpGet("event/{eventId}/download")]
        public async Task<IActionResult> DownloadAllPhotos(Guid eventId)
        {
            var (zipBytes, fileName) = await _photoService.GetZipArchiveOfEventPhotosAsync(eventId);
            return File(zipBytes, "application/zip", fileName);
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadPhotos([FromForm] Guid eventId, [FromForm] string uploaderName, List<IFormFile> files)
        {
            if (files == null || files.Count == 0)
                return BadRequest("No files uploaded");

            var fileData = files.Select(f => (f.FileName, f.OpenReadStream()));
            var uploadedPhotos = await _photoService.UploadPhotosAsync(eventId, uploaderName, fileData);

            return Ok(uploadedPhotos.Select(p => new { id = p.Id, url = p.OriginalPath }));
        }
    }
}
