using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeddingGallery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MediaType",
                table: "Photos",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                // Every row that predates video support is a photo, so backfill rather than
                // leaving existing uploads with an empty type the gallery cannot interpret.
                defaultValue: "image");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediaType",
                table: "Photos");
        }
    }
}
