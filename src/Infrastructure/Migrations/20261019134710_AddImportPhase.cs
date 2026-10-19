using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataMart.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportPhase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Phase",
                table: "ImportBatches",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Phase",
                table: "ImportBatches");
        }
    }
}
