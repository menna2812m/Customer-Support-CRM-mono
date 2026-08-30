using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentityAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_User_Email",
                table: "User");

            migrationBuilder.DropIndex(
                name: "IX_User_ProviderSubject",
                table: "User");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderSubject",
                table: "User",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "User",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.InsertData(
                table: "RolePermission",
                columns: new[] { "Permission", "RoleId" },
                values: new object[,]
                {
                    { "identity.manage", new Guid("0195f1a0-0000-7000-8000-000000000001") },
                    { "identity.view", new Guid("0195f1a0-0000-7000-8000-000000000001") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_User_Email",
                table: "User",
                column: "Email",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_User_Provider_ProviderSubject",
                table: "User",
                columns: new[] { "Provider", "ProviderSubject" },
                unique: true,
                filter: "[ProviderSubject] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_User_Email",
                table: "User");

            migrationBuilder.DropIndex(
                name: "IX_User_Provider_ProviderSubject",
                table: "User");

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "identity.manage", new Guid("0195f1a0-0000-7000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "identity.view", new Guid("0195f1a0-0000-7000-8000-000000000001") });

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "User");

            migrationBuilder.AlterColumn<string>(
                name: "ProviderSubject",
                table: "User",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_Email",
                table: "User",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_ProviderSubject",
                table: "User",
                column: "ProviderSubject",
                unique: true);
        }
    }
}
