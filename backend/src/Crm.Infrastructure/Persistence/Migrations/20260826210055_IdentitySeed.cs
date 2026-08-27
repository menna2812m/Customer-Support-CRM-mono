using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Crm.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentitySeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Role",
                columns: new[] { "Id", "CreatedAt", "CreatedBy", "Description", "IsSystem", "Name", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("0195f1a0-0000-7000-8000-000000000001"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Every permission in the catalog.", true, "Administrator", null, null },
                    { new Guid("0195f1a0-0000-7000-8000-000000000002"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Day-to-day customer and ticket work.", true, "Agent", null, null },
                    { new Guid("0195f1a0-0000-7000-8000-000000000003"), new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Read access for oversight and reporting.", true, "ReadOnly", null, null }
                });

            migrationBuilder.InsertData(
                table: "RolePermission",
                columns: new[] { "Permission", "RoleId" },
                values: new object[,]
                {
                    { "customers.create", new Guid("0195f1a0-0000-7000-8000-000000000001") },
                    { "customers.update", new Guid("0195f1a0-0000-7000-8000-000000000001") },
                    { "customers.view", new Guid("0195f1a0-0000-7000-8000-000000000001") },
                    { "diagnostics.read", new Guid("0195f1a0-0000-7000-8000-000000000001") },
                    { "reports.view", new Guid("0195f1a0-0000-7000-8000-000000000001") },
                    { "tickets.assign", new Guid("0195f1a0-0000-7000-8000-000000000001") },
                    { "tickets.create", new Guid("0195f1a0-0000-7000-8000-000000000001") },
                    { "tickets.escalate", new Guid("0195f1a0-0000-7000-8000-000000000001") },
                    { "tickets.view", new Guid("0195f1a0-0000-7000-8000-000000000001") },
                    { "users.manage", new Guid("0195f1a0-0000-7000-8000-000000000001") },
                    { "customers.create", new Guid("0195f1a0-0000-7000-8000-000000000002") },
                    { "customers.update", new Guid("0195f1a0-0000-7000-8000-000000000002") },
                    { "customers.view", new Guid("0195f1a0-0000-7000-8000-000000000002") },
                    { "tickets.create", new Guid("0195f1a0-0000-7000-8000-000000000002") },
                    { "tickets.view", new Guid("0195f1a0-0000-7000-8000-000000000002") },
                    { "customers.view", new Guid("0195f1a0-0000-7000-8000-000000000003") },
                    { "reports.view", new Guid("0195f1a0-0000-7000-8000-000000000003") },
                    { "tickets.view", new Guid("0195f1a0-0000-7000-8000-000000000003") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "customers.create", new Guid("0195f1a0-0000-7000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "customers.update", new Guid("0195f1a0-0000-7000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "customers.view", new Guid("0195f1a0-0000-7000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "diagnostics.read", new Guid("0195f1a0-0000-7000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "reports.view", new Guid("0195f1a0-0000-7000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "tickets.assign", new Guid("0195f1a0-0000-7000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "tickets.create", new Guid("0195f1a0-0000-7000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "tickets.escalate", new Guid("0195f1a0-0000-7000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "tickets.view", new Guid("0195f1a0-0000-7000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "users.manage", new Guid("0195f1a0-0000-7000-8000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "customers.create", new Guid("0195f1a0-0000-7000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "customers.update", new Guid("0195f1a0-0000-7000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "customers.view", new Guid("0195f1a0-0000-7000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "tickets.create", new Guid("0195f1a0-0000-7000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "tickets.view", new Guid("0195f1a0-0000-7000-8000-000000000002") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "customers.view", new Guid("0195f1a0-0000-7000-8000-000000000003") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "reports.view", new Guid("0195f1a0-0000-7000-8000-000000000003") });

            migrationBuilder.DeleteData(
                table: "RolePermission",
                keyColumns: new[] { "Permission", "RoleId" },
                keyValues: new object[] { "tickets.view", new Guid("0195f1a0-0000-7000-8000-000000000003") });

            migrationBuilder.DeleteData(
                table: "Role",
                keyColumn: "Id",
                keyValue: new Guid("0195f1a0-0000-7000-8000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Role",
                keyColumn: "Id",
                keyValue: new Guid("0195f1a0-0000-7000-8000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Role",
                keyColumn: "Id",
                keyValue: new Guid("0195f1a0-0000-7000-8000-000000000003"));
        }
    }
}
