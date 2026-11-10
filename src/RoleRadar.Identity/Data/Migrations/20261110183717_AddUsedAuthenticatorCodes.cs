using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoleRadar.Identity.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsedAuthenticatorCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsedAuthenticatorCodes",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CodeHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcceptedAtUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsedAuthenticatorCodes", x => new { x.UserId, x.CodeHash });
                    table.ForeignKey(
                        name: "FK_UsedAuthenticatorCodes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsedAuthenticatorCodes_ExpiresAtUtc",
                table: "UsedAuthenticatorCodes",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsedAuthenticatorCodes");
        }
    }
}
