using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveIT.Api.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Organizations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Organizations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "CanManageOrganizations",
                table: "Memberships",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_TenantId_Name_Id",
                table: "Organizations",
                columns: new[] { "TenantId", "Name", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Organization_Version",
                table: "Organizations",
                sql: "\"Version\" > 0");
            migrationBuilder.Sql("""
                CREATE FUNCTION public.create_organization(new_id uuid,new_name text,member_id uuid) RETURNS void
                LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
                DECLARE tenant_id uuid := NULLIF(current_setting('app.tenant_id',true),'')::uuid;
                BEGIN
                    IF tenant_id IS NULL OR member_id IS NULL OR new_id IS NULL OR new_id='00000000-0000-0000-0000-000000000000'::uuid
                      OR member_id IS DISTINCT FROM NULLIF(current_setting('app.membership_id',true),'')::uuid THEN
                        RAISE EXCEPTION 'Organization administration is unavailable' USING ERRCODE='42501';
                    END IF;
                    PERFORM 1 FROM public."Memberships" m WHERE m."TenantId"=tenant_id AND m."Id"=member_id
                      AND m."Active" AND m."CanManageOrganizations" FOR SHARE;
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'Organization administration is unavailable' USING ERRCODE='42501';
                    END IF;
                    IF new_name IS NULL OR length(btrim(new_name)) NOT BETWEEN 1 AND 160 THEN
                        RAISE EXCEPTION 'Invalid organization name' USING ERRCODE='22023';
                    END IF;
                    -- Plain INSERT guarantees this grant applies only to a brand-new empty scope.
                    INSERT INTO public."Organizations" ("Id","TenantId","Name","Version","UpdatedAt")
                      VALUES (new_id,tenant_id,btrim(new_name),1,CURRENT_TIMESTAMP);
                    INSERT INTO public."OrganizationGrants" ("TenantId","MembershipId","OrganizationId","Role")
                      VALUES (tenant_id,member_id,new_id,'Editor');
                END $$;
                CREATE FUNCTION public.rename_organization(org_id uuid,new_name text,member_id uuid,expected_version integer) RETURNS void
                LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
                DECLARE tenant_id uuid := NULLIF(current_setting('app.tenant_id',true),'')::uuid;
                BEGIN
                    IF tenant_id IS NULL OR member_id IS NULL
                      OR member_id IS DISTINCT FROM NULLIF(current_setting('app.membership_id',true),'')::uuid THEN
                        RAISE EXCEPTION 'Organization administration is unavailable' USING ERRCODE='42501';
                    END IF;
                    PERFORM 1 FROM public."Memberships" m WHERE m."TenantId"=tenant_id AND m."Id"=member_id
                      AND m."Active" AND m."CanManageOrganizations" FOR SHARE;
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'Organization administration is unavailable' USING ERRCODE='42501';
                    END IF;
                    PERFORM 1 FROM public."OrganizationGrants" g WHERE g."TenantId"=tenant_id AND g."MembershipId"=member_id
                      AND g."OrganizationId"=org_id AND g."Role"='Editor' FOR SHARE;
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'Organization administration is unavailable' USING ERRCODE='42501';
                    END IF;
                    IF new_name IS NULL OR length(btrim(new_name)) NOT BETWEEN 1 AND 160 OR expected_version IS NULL OR expected_version < 1 THEN
                        RAISE EXCEPTION 'Invalid organization revision' USING ERRCODE='22023';
                    END IF;
                    UPDATE public."Organizations" SET "Name"=btrim(new_name),"Version"="Version"+1,"UpdatedAt"=CURRENT_TIMESTAMP
                      WHERE "TenantId"=tenant_id AND "Id"=org_id AND "Version"=expected_version AND "Version"<2147483647;
                    IF NOT FOUND THEN
                        RAISE EXCEPTION 'Organization changed' USING ERRCODE='40001';
                    END IF;
                END $$;
                REVOKE ALL ON FUNCTION public.create_organization(uuid,text,uuid),public.rename_organization(uuid,text,uuid,integer) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION public.create_organization(uuid,text,uuid),public.rename_organization(uuid,text,uuid,integer) TO saveit_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION public.create_organization(uuid,text,uuid);
                DROP FUNCTION public.rename_organization(uuid,text,uuid,integer);
                """);
            migrationBuilder.DropIndex(
                name: "IX_Organizations_TenantId_Name_Id",
                table: "Organizations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Organization_Version",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CanManageOrganizations",
                table: "Memberships");
        }
    }
}
