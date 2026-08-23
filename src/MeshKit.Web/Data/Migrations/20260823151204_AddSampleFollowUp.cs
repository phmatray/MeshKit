using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeshKit.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSampleFollowUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FollowUpOptIn",
                table: "SampleDownloads",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FollowUpSentAt",
                table: "SampleDownloads",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FollowUpOptIn",
                table: "SampleDownloads");

            migrationBuilder.DropColumn(
                name: "FollowUpSentAt",
                table: "SampleDownloads");
        }
    }
}
