using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FanBento.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPostTranslations : Migration
    {
        /// <summary>Adds translation storage, including databases already upgraded by a worker.</summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "PostTranslation" (
                    "PostId" TEXT NOT NULL,
                    "Language" TEXT NOT NULL,
                    "ContentJson" TEXT NULL,
                    "Status" TEXT NOT NULL,
                    "LastAttemptUtc" TEXT NULL,
                    "SentToTelegramChannel" INTEGER NOT NULL,
                    CONSTRAINT "PK_PostTranslation" PRIMARY KEY ("PostId", "Language"),
                    CONSTRAINT "FK_PostTranslation_Post_PostId" FOREIGN KEY ("PostId") REFERENCES "Post" ("Id") ON DELETE CASCADE
                );
                """);
        }

        /// <summary>Removes saved translations while leaving original posts intact.</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PostTranslation");
        }
    }
}
