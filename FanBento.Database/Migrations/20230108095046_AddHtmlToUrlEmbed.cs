using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FanBento.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddHtmlToUrlEmbed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Html",
                table: "UrlEmbed",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Html",
                table: "UrlEmbed");
        }
    }
}
