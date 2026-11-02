using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveIT.Api.Migrations
{
    /// <inheritdoc />
    public partial class OperatorAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccessVersion",
                table: "Memberships",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "SessionEpoch",
                table: "Memberships",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "AccessAudits",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DatabaseLogin = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    Operator = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    AccessVersion = table.Column<int>(type: "integer", nullable: false),
                    SessionEpoch = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false),
                    CanManageOrganizations = table.Column<bool>(type: "boolean", nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessAudits", x => new { x.TenantId, x.MembershipId, x.Id });
                    table.CheckConstraint("CK_AccessAudit_Action", "\"Action\" IN ('Created','ActiveChanged','GrantChanged','CapabilityChanged','Inspected')");
                    table.CheckConstraint("CK_AccessAudit_Role", "\"Role\" IS NULL OR (\"OrganizationId\" IS NOT NULL AND \"Role\" IN ('Viewer','Editor'))");
                    table.CheckConstraint("CK_AccessAudit_Version", "\"AccessVersion\">0 AND \"SessionEpoch\">0");
                    table.ForeignKey(
                        name: "FK_AccessAudits_Memberships_TenantId_MembershipId",
                        columns: x => new { x.TenantId, x.MembershipId },
                        principalTable: "Memberships",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessAudits_Organizations_TenantId_OrganizationId",
                        columns: x => new { x.TenantId, x.OrganizationId },
                        principalTable: "Organizations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessAudits_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccessResponses",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessResponses", x => new { x.TenantId, x.MembershipId, x.RequestId });
                    table.CheckConstraint("CK_AccessResponse_Completion", "(\"CompletedAt\" IS NULL AND \"Outcome\" IS NULL) OR (\"CompletedAt\" IS NOT NULL AND \"Outcome\" IS NOT NULL AND \"CompletedAt\">=\"StartedAt\" AND \"Outcome\" IN ('Completed','Aborted'))");
                    table.ForeignKey(
                        name: "FK_AccessResponses_Memberships_TenantId_MembershipId",
                        columns: x => new { x.TenantId, x.MembershipId },
                        principalTable: "Memberships",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessResponses_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccessControlEvents",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuditId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessControlEvents", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_AccessControlEvent_Schema", "\"SchemaVersion\"=1");
                    table.ForeignKey(
                        name: "FK_AccessControlEvents_AccessAudits_TenantId_MembershipId_Audi~",
                        columns: x => new { x.TenantId, x.MembershipId, x.AuditId },
                        principalTable: "AccessAudits",
                        principalColumns: new[] { "TenantId", "MembershipId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessControlEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Membership_AccessVersion",
                table: "Memberships",
                sql: "\"AccessVersion\">0 AND \"SessionEpoch\">0");

            migrationBuilder.CreateIndex(
                name: "IX_AccessAudits_TenantId_MembershipId_CreatedAt_Id",
                table: "AccessAudits",
                columns: new[] { "TenantId", "MembershipId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessAudits_TenantId_OrganizationId",
                table: "AccessAudits",
                columns: new[] { "TenantId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessControlEvents_TenantId_MembershipId_AuditId",
                table: "AccessControlEvents",
                columns: new[] { "TenantId", "MembershipId", "AuditId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessResponses_Pending",
                table: "AccessResponses",
                columns: new[] { "TenantId", "MembershipId" },
                filter: "\"CompletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccessResponses_TenantId_MembershipId_CompletedAt",
                table: "AccessResponses",
                columns: new[] { "TenantId", "MembershipId", "CompletedAt" });
            migrationBuilder.Sql(AccessSql.Up);
            migrationBuilder.Sql(OrganizationHistorySql.Functions(history: true, accessVersions: true));
            migrationBuilder.Sql("GRANT USAGE ON SCHEMA public TO saveit_access_operator; GRANT SELECT (\"MigrationId\") ON public.\"__EFMigrationsHistory\" TO saveit_access_operator");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AccessSql.Down);
            migrationBuilder.Sql(OrganizationHistorySql.Functions(history: true));
            migrationBuilder.DropTable(
                name: "AccessControlEvents");

            migrationBuilder.DropTable(
                name: "AccessResponses");

            migrationBuilder.DropTable(
                name: "AccessAudits");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Membership_AccessVersion",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "AccessVersion",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "SessionEpoch",
                table: "Memberships");
        }
    }
}
