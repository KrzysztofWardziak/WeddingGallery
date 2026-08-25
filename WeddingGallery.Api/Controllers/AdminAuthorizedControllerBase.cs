using Microsoft.AspNetCore.Mvc;

namespace WeddingGallery.Api.Controllers
{
    /// <summary>
    /// Base for controllers/actions that must be gated behind the admin bearer token
    /// (compared against AdminSettings:Token). Shared so the comparison logic lives
    /// in exactly one place.
    /// </summary>
    public abstract class AdminAuthorizedControllerBase : ControllerBase
    {
        protected readonly IConfiguration Configuration;

        protected AdminAuthorizedControllerBase(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        protected bool ValidateToken()
        {
            var expectedToken = Configuration["AdminSettings:Token"];
            var providedToken = Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "");
            return expectedToken == providedToken;
        }
    }
}