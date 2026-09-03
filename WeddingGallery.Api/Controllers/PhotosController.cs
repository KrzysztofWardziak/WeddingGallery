using Microsoft.AspNetCore.Mvc;
using WeddingGallery.Application.Interfaces;
using WeddingGallery.Application.Media;
using WeddingGallery.Application.Services;

namespace WeddingGallery.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PhotosController : AdminAuthorizedControllerBase
    {
        private readonly IPhotoService _photoService;
        private readonly IChunkedUploadService _chunkedUploadService;

        public PhotosController(
            IPhotoService photoService,
            IChunkedUploadService chunkedUploadService,
            IConfiguration configuration)
            : base(configuration)
        {
            _photoService = photoService;
            _chunkedUploadService = chunkedUploadService;
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

        public class StartUploadRequest
        {
            public Guid EventId { get; set; }
            public string? UploaderName { get; set; }
            public string FileName { get; set; } = string.Empty;
            public long TotalSize { get; set; }
        }

        // Chunked upload. Cloudflare caps a request body at 100 MB and the tunnel is the only
        // route in, so a large video has to arrive across several requests instead of one.
        [HttpPost("uploads")]
        public async Task<IActionResult> StartUpload([FromBody] StartUploadRequest request)
        {
            try
            {
                var session = await _chunkedUploadService.StartAsync(
                    request.EventId, request.UploaderName, request.FileName, request.TotalSize);

                return Ok(new
                {
                    uploadId = session.Id,
                    offset = session.Offset,
                    chunkSize = ChunkedUploadService.ChunkSizeBytes
                });
            }
            catch (InvalidMediaFileException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // The resume primitive: the client asks where it got to and continues from there.
        [HttpGet("uploads/{uploadId}")]
        public async Task<IActionResult> GetUpload(Guid uploadId)
        {
            var session = await _chunkedUploadService.GetAsync(uploadId);
            if (session is null) return NotFound();

            return Ok(new { uploadId = session.Id, offset = session.Offset, totalSize = session.TotalSize });
        }

        [HttpPost("uploads/{uploadId}/chunk")]
        public async Task<IActionResult> AppendChunk(Guid uploadId, [FromQuery] long offset)
        {
            try
            {
                // Streamed straight from the request body to the partial file; a 25 MB chunk
                // never needs to sit in memory.
                var newOffset = await _chunkedUploadService.AppendAsync(uploadId, offset, Request.Body);
                return Ok(new { offset = newOffset });
            }
            catch (UploadSessionNotFoundException)
            {
                return NotFound();
            }
            catch (UploadOffsetMismatchException ex)
            {
                // 409 carries the true offset so the client corrects itself rather than
                // restarting the file or corrupting it.
                return Conflict(new { offset = ex.ActualOffset });
            }
            catch (InvalidMediaFileException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("uploads/{uploadId}/complete")]
        public async Task<IActionResult> CompleteUpload(Guid uploadId)
        {
            try
            {
                var photo = await _chunkedUploadService.CompleteAsync(uploadId);
                return Ok(new { id = photo.Id, url = photo.OriginalPath, mediaType = photo.MediaType });
            }
            catch (UploadSessionNotFoundException)
            {
                return NotFound();
            }
            catch (IncompleteUploadException ex)
            {
                // Session preserved: the guest resumes the missing bytes.
                return BadRequest(new { received = ex.Received, expected = ex.Expected });
            }
        }

        [HttpDelete("uploads/{uploadId}")]
        public async Task<IActionResult> AbandonUpload(Guid uploadId)
        {
            await _chunkedUploadService.AbandonAsync(uploadId);
            return NoContent();
        }
    }
}
