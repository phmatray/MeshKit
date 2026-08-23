using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeshKit.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSampleDownloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SampleDownloads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    PackSlug = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ModelSlug = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DownloadedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SampleDownloads", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SampleDownloads_UserId_PackSlug",
                table: "SampleDownloads",
                columns: new[] { "UserId", "PackSlug" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SampleDownloads");
        }
    }
}
