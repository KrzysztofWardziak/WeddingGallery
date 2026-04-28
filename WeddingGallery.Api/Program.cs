using Microsoft.EntityFrameworkCore;
using WeddingGallery.Application.Interfaces;
using WeddingGallery.Application.Services;
using WeddingGallery.Domain.Interfaces;
using WeddingGallery.Infrastructure.Data;
using WeddingGallery.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Enable developer exception page or swagger here if needed later
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();
app.UseStaticFiles(); // Serve photos
app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.Run();
