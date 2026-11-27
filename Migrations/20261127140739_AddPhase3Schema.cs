using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AcxiomCRM.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase3Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Opportunity",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "FollowUp",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OpportunityId",
                table: "FollowUp",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Activity",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OpportunityId",
                table: "Activity",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FollowUp_OpportunityId",
                table: "FollowUp",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_Activity_OpportunityId",
                table: "Activity",
                column: "OpportunityId");

            migrationBuilder.AddForeignKey(
                name: "FK_Activity_Opportunity_OpportunityId",
                table: "Activity",
                column: "OpportunityId",
                principalTable: "Opportunity",
                principalColumn: "OpportunityId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FollowUp_Opportunity_OpportunityId",
                table: "FollowUp",
                column: "OpportunityId",
                principalTable: "Opportunity",
                principalColumn: "OpportunityId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Activity_Opportunity_OpportunityId",
                table: "Activity");

            migrationBuilder.DropForeignKey(
                name: "FK_FollowUp_Opportunity_OpportunityId",
                table: "FollowUp");

            migrationBuilder.DropIndex(
                name: "IX_FollowUp_OpportunityId",
                table: "FollowUp");

            migrationBuilder.DropIndex(
                name: "IX_Activity_OpportunityId",
                table: "Activity");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Opportunity");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "FollowUp");

            migrationBuilder.DropColumn(
                name: "OpportunityId",
                table: "FollowUp");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Activity");

            migrationBuilder.DropColumn(
                name: "OpportunityId",
                table: "Activity");
        }
    }
}
