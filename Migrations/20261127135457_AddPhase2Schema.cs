using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcxiomCRM.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase2Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConvertedCustomerId",
                table: "Lead",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Lead",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AssignedTo",
                table: "Customer",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Customer",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ManagerId",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lead_ConvertedCustomerId",
                table: "Lead",
                column: "ConvertedCustomerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Lead_Customer_ConvertedCustomerId",
                table: "Lead",
                column: "ConvertedCustomerId",
                principalTable: "Customer",
                principalColumn: "CustomerId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Lead_Customer_ConvertedCustomerId",
                table: "Lead");

            migrationBuilder.DropIndex(
                name: "IX_Lead_ConvertedCustomerId",
                table: "Lead");

            migrationBuilder.DropColumn(
                name: "ConvertedCustomerId",
                table: "Lead");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Lead");

            migrationBuilder.DropColumn(
                name: "AssignedTo",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Customer");

            migrationBuilder.DropColumn(
                name: "ManagerId",
                table: "AspNetUsers");
        }
    }
}
