using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using WeddingGallery.Api.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using WeddingGallery.Application.Interfaces;
using WeddingGallery.Application.Media;
using WeddingGallery.Application.Services;
using WeddingGallery.Domain.Interfaces;
using WeddingGallery.Infrastructure.Data;
using WeddingGallery.Infrastructure.Media;
using WeddingGallery.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Guests upload full-size phone photos and short videos; Kestrel's 30 MB default is too low.
// The per-file ceiling that actually matters is MediaFileValidator.MaxFileBytes, which is set
// by Cloudflare's 100 MB request limit on the free plan.
const long MaxUploadBytes = 200L * 1024 * 1024;

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = MaxUploadBytes;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxUploadBytes;
});

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<WeddingGalleryDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IEventRepository, EventRepository>();
builder.Services.AddScoped<IPhotoRepository, PhotoRepository>();
builder.Services.AddScoped<IEventService, EventService>();
builder.Services.AddScoped<IPhotoService, PhotoService>();
builder.Services.AddSingleton<IMediaFileValidator, MediaFileValidator>();
builder.Services.AddSingleton<IThumbnailGenerator, FfmpegThumbnailGenerator>();

// Must match the wwwroot/photos path that UseStaticFiles serves and that docker-compose
// mounts the data volume onto.
builder.Services.AddSingleton(new PhotoStorageOptions(
    Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "photos")));

builder.Services.AddScoped<IChunkedUploadService, ChunkedUploadService>();
builder.Services.AddHostedService<AbandonedUploadSweeper>();

// Deliberately outside wwwroot: UseStaticFiles serves everything under wwwroot/photos, which
// would make a half-received file publicly fetchable under a name the uploader chose, before
// validation has finished. docker-compose mounts this path onto the data volume so a 500 MB
// partial file never lands on the container's writable layer.
builder.Services.AddSingleton(new ChunkedUploadStorageOptions(
    Path.Combine(builder.Environment.ContentRootPath, "uploads-tmp")));

// In production the SPA is served from the same origin, so the list is empty and no CORS
// headers are emitted at all. Development keeps ng serve on localhost:4200.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                  ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins.Length > 0)
        {
            policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
        }
    });
});

var app = builder.Build();

// Admin credentials come from the environment. Empty values would let anyone authenticate
// with an empty password, so refuse to start instead of running wide open.
if (!app.Environment.IsDevelopment())
{
    foreach (var key in new[] { "AdminSettings:Password", "AdminSettings:Token" })
    {
        if (string.IsNullOrWhiteSpace(app.Configuration[key]))
        {
            throw new InvalidOperationException(
                $"{key} is not configured. Set it in .env before starting the container.");
        }
    }
}

// Apply pending EF Core migrations on startup so the schema exists in a fresh container.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WeddingGalleryDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    const int maxAttempts = 10;
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            db.Database.Migrate();
            logger.LogInformation("Database migrations applied.");
            break;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            logger.LogWarning(ex, "Database not ready (attempt {Attempt}/{MaxAttempts}), retrying in 3s...", attempt, maxAttempts);
            Thread.Sleep(TimeSpan.FromSeconds(3));
        }
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// TLS is terminated by Caddy in front of this container, so no redirect here.

// The built-in map covers .mp4 and .webm but not the formats iPhones actually produce, and an
// unmapped extension is served without a Content-Type, which stops playback in the browser.
var contentTypeProvider = new FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".mov"] = "video/quicktime";
contentTypeProvider.Mappings[".m4v"] = "video/x-m4v";
contentTypeProvider.Mappings[".heic"] = "image/heic";
contentTypeProvider.Mappings[".heif"] = "image/heif";

app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypeProvider }); // Serve photos and videos
app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.Run();
