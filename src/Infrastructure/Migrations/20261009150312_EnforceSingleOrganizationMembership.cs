using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Archiva.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleOrganizationMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM OrganizationUsers GROUP BY UserId HAVING COUNT(*) > 1)
                    THROW 51000, 'OrganizationUsers has duplicate UserId values. Resolve memberships before applying this migration.', 1;
                IF EXISTS (SELECT 1 FROM Tags AS t LEFT JOIN Organizations AS o ON o.Id = t.OrganizationId WHERE o.Id IS NULL)
                    THROW 51001, 'Tags has orphaned OrganizationId values. Resolve tags before applying this migration.', 1;
                """
            );

            migrationBuilder.CreateIndex(
                name: "IX_Tags_OrganizationId",
                table: "Tags",
                column: "OrganizationId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationUsers_UserId",
                table: "OrganizationUsers",
                column: "UserId",
                unique: true
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Tags_Organizations_OrganizationId",
                table: "Tags",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tags_Organizations_OrganizationId",
                table: "Tags"
            );

            migrationBuilder.DropIndex(name: "IX_Tags_OrganizationId", table: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationUsers_UserId",
                table: "OrganizationUsers"
            );
        }
    }
}
