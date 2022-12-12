using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FanBento.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddUrlEmbed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UrlEmbedMap",
                table: "ContentBody",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UrlEmbedId",
                table: "Block",
                type: "TEXT",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "UrlEmbed",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Host = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", nullable: true),
                    Url = table.Column<string>(type: "TEXT", nullable: true),
                    Order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UrlEmbed", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UrlEmbed");

            migrationBuilder.DropColumn(
                name: "UrlEmbedId",
                table: "Block");

            migrationBuilder.DropColumn(
                name: "UrlEmbedMap",
                table: "ContentBody");
        }
    }
}
