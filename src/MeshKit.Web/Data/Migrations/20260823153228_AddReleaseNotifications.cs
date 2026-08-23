using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeshKit.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NewReleaseOptIn",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NewReleaseOptInAt",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PackAnnouncements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PackSlug = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Recipients = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackAnnouncements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackAnnouncements_PackSlug",
                table: "PackAnnouncements",
                column: "PackSlug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackAnnouncements");

            migrationBuilder.DropColumn(
                name: "NewReleaseOptIn",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NewReleaseOptInAt",
                table: "AspNetUsers");
        }
    }
}
