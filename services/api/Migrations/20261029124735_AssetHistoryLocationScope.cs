using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveIT.Api.Migrations
{
    /// <inheritdoc />
    public partial class AssetHistoryLocationScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AssetRevisions_TenantId_OrganizationId_LocationId",
                table: "AssetRevisions",
                columns: new[] { "TenantId", "OrganizationId", "LocationId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AssetRevisions_Locations_TenantId_OrganizationId_LocationId",
                table: "AssetRevisions",
                columns: new[] { "TenantId", "OrganizationId", "LocationId" },
                principalTable: "Locations",
                principalColumns: new[] { "TenantId", "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssetRevisions_Locations_TenantId_OrganizationId_LocationId",
                table: "AssetRevisions");

            migrationBuilder.DropIndex(
                name: "IX_AssetRevisions_TenantId_OrganizationId_LocationId",
                table: "AssetRevisions");
        }
    }
}
