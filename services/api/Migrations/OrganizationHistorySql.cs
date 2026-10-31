namespace SaveIT.Api.Migrations;

internal static class OrganizationHistorySql
{
    // Only migration-owned constants are interpolated. No dynamic SQL runs in either function.
    internal static string Functions(bool history)
    {
        var actorDeclaration = history ? "actor text;" : "";
        var membershipRead = history ? "SELECT m.\"Subject\" INTO actor" : "PERFORM 1";
        var created = history ? """
            INSERT INTO public."OrganizationRevisions" ("TenantId","OrganizationId","Version","Name","Actor","CreatedAt","Origin")
              SELECT o."TenantId",o."Id",o."Version",o."Name",actor,o."UpdatedAt",'Created'
              FROM public."Organizations" o WHERE o."TenantId"=tenant_id AND o."Id"=new_id;
            """ : "";
        var renamed = history ? """
            INSERT INTO public."OrganizationRevisions" ("TenantId","OrganizationId","Version","Name","Actor","CreatedAt","Origin")
              SELECT o."TenantId",o."Id",o."Version",o."Name",actor,o."UpdatedAt",'Renamed'
              FROM public."Organizations" o WHERE o."TenantId"=tenant_id AND o."Id"=org_id;
            """ : "";
        return $$"""
            CREATE OR REPLACE FUNCTION public.create_organization(new_id uuid,new_name text,member_id uuid) RETURNS void
            LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $function$
            DECLARE tenant_id uuid := NULLIF(current_setting('app.tenant_id',true),'')::uuid; {{actorDeclaration}}
            BEGIN
                IF tenant_id IS NULL OR member_id IS NULL OR new_id IS NULL OR new_id='00000000-0000-0000-0000-000000000000'::uuid
                  OR member_id IS DISTINCT FROM NULLIF(current_setting('app.membership_id',true),'')::uuid THEN
                    RAISE EXCEPTION 'Organization administration is unavailable' USING ERRCODE='42501';
                END IF;
                {{membershipRead}} FROM public."Memberships" m WHERE m."TenantId"=tenant_id AND m."Id"=member_id
                  AND m."Active" AND m."CanManageOrganizations" FOR SHARE;
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'Organization administration is unavailable' USING ERRCODE='42501';
                END IF;
                IF new_name IS NULL OR length(btrim(new_name)) NOT BETWEEN 1 AND 160 THEN
                    RAISE EXCEPTION 'Invalid organization name' USING ERRCODE='22023';
                END IF;
                INSERT INTO public."Organizations" ("Id","TenantId","Name","Version","UpdatedAt")
                  VALUES (new_id,tenant_id,btrim(new_name),1,CURRENT_TIMESTAMP);
                INSERT INTO public."OrganizationGrants" ("TenantId","MembershipId","OrganizationId","Role")
                  VALUES (tenant_id,member_id,new_id,'Editor');
                {{created}}
            END $function$;
            CREATE OR REPLACE FUNCTION public.rename_organization(org_id uuid,new_name text,member_id uuid,expected_version integer) RETURNS void
            LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $function$
            DECLARE tenant_id uuid := NULLIF(current_setting('app.tenant_id',true),'')::uuid; {{actorDeclaration}}
            BEGIN
                IF tenant_id IS NULL OR member_id IS NULL
                  OR member_id IS DISTINCT FROM NULLIF(current_setting('app.membership_id',true),'')::uuid THEN
                    RAISE EXCEPTION 'Organization administration is unavailable' USING ERRCODE='42501';
                END IF;
                {{membershipRead}} FROM public."Memberships" m WHERE m."TenantId"=tenant_id AND m."Id"=member_id
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
                {{renamed}}
            END $function$;
            REVOKE ALL ON FUNCTION public.create_organization(uuid,text,uuid),public.rename_organization(uuid,text,uuid,integer) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION public.create_organization(uuid,text,uuid),public.rename_organization(uuid,text,uuid,integer) TO saveit_app;
            """;
    }
}
