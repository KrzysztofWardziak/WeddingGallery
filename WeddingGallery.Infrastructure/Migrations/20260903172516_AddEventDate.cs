using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeddingGallery.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEventDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EventDate",
                table: "Events",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EventDate",
                table: "Events");
        }
    }
}
