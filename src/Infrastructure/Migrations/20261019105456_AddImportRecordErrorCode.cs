using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataMart.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportRecordErrorCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "ImportRecords",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "ImportRecords");
        }
    }
}
