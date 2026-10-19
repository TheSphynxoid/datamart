using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataMart.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalFramework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImportRecords_ImportBatchId",
                table: "ImportRecords");

            migrationBuilder.AddColumn<int>(
                name: "RowNumber",
                table: "ImportRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "ImportBatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JournalExtrasJson",
                table: "ImportBatches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MappingSnapshotJson",
                table: "ImportBatches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxImpactedId",
                table: "ImportBatches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinImpactedId",
                table: "ImportBatches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RulesSnapshotJson",
                table: "ImportBatches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                table: "ImportBatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportRecords_ImportBatchId_RowNumber",
                table: "ImportRecords",
                columns: new[] { "ImportBatchId", "RowNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImportRecords_ImportBatchId_RowNumber",
                table: "ImportRecords");

            migrationBuilder.DropColumn(
                name: "RowNumber",
                table: "ImportRecords");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "JournalExtrasJson",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "MappingSnapshotJson",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "MaxImpactedId",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "MinImpactedId",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "RulesSnapshotJson",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "ImportBatches");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRecords_ImportBatchId",
                table: "ImportRecords",
                column: "ImportBatchId");
        }
    }
}
