using Microsoft.EntityFrameworkCore;
using WeddingGallery.Domain.Entities;

namespace WeddingGallery.Infrastructure.Data
{
    public class WeddingGalleryDbContext : DbContext
    {
        public WeddingGalleryDbContext(DbContextOptions<WeddingGalleryDbContext> options) : base(options)
        {
        }

        public DbSet<Photo> Photos { get; set; }
        public DbSet<Event> Events { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Event>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Slug).IsRequired().HasMaxLength(50);
                entity.HasIndex(e => e.Slug).IsUnique();
            });

            modelBuilder.Entity<Photo>(entity =>
            {
                entity.HasKey(p => p.Id);
                entity.HasOne(p => p.Event)
                      .WithMany(e => e.Photos)
                      .HasForeignKey(p => p.EventId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
