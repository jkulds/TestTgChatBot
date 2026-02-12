using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TestTgChatBot.Dao.Migrations
{
    /// <inheritdoc />
    public partial class AddTestAccessTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "UserProfiles",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchoolName",
                table: "UserProfiles",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TestAccessTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TestId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsUsed = table.Column<bool>(type: "INTEGER", nullable: false),
                    UsedByUserTestId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestAccessTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestAccessTokens_Tests_TestId",
                        column: x => x.TestId,
                        principalTable: "Tests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestAccessTokens_UserTests_UsedByUserTestId",
                        column: x => x.UsedByUserTestId,
                        principalTable: "UserTests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestAccessTokens_TestId",
                table: "TestAccessTokens",
                column: "TestId");

            migrationBuilder.CreateIndex(
                name: "IX_TestAccessTokens_UsedByUserTestId",
                table: "TestAccessTokens",
                column: "UsedByUserTestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestAccessTokens");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "SchoolName",
                table: "UserProfiles");
        }
    }
}
