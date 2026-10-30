using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveIT.Api.Migrations
{
    /// <inheritdoc />
    public partial class AssetRevisionLocationNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LocationName",
                table: "AssetRevisions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AssetRevision_LocationName",
                table: "AssetRevisions",
                sql: "\"LocationName\" IS NULL OR (\"LocationId\" IS NOT NULL AND length(btrim(\"LocationName\")) BETWEEN 1 AND 160)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AssetRevision_LocationName",
                table: "AssetRevisions");

            migrationBuilder.DropColumn(
                name: "LocationName",
                table: "AssetRevisions");
        }
    }
}
