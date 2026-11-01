internal static class DocumentTemplateSql
{
    public const string Up = """
        REVOKE ALL ON "DocumentTemplates","DocumentTemplateRevisions","DocumentTemplateOrigins" FROM PUBLIC,saveit_app,saveit_search_worker;
        GRANT SELECT,INSERT ON "DocumentTemplates","DocumentTemplateRevisions","DocumentTemplateOrigins" TO saveit_app;
        GRANT UPDATE ("Name","Version","IsArchived","UpdatedAt") ON "DocumentTemplates" TO saveit_app;
        ALTER TABLE "DocumentTemplates" ENABLE ROW LEVEL SECURITY;
        ALTER TABLE "DocumentTemplates" FORCE ROW LEVEL SECURITY;
        ALTER TABLE "DocumentTemplateRevisions" ENABLE ROW LEVEL SECURITY;
        ALTER TABLE "DocumentTemplateRevisions" FORCE ROW LEVEL SECURITY;
        ALTER TABLE "DocumentTemplateOrigins" ENABLE ROW LEVEL SECURITY;
        ALTER TABLE "DocumentTemplateOrigins" FORCE ROW LEVEL SECURITY;
        CREATE POLICY tenant_boundary ON "DocumentTemplates" TO saveit_app USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid) WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
        CREATE POLICY tenant_boundary ON "DocumentTemplateRevisions" TO saveit_app USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid) WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
        CREATE POLICY tenant_boundary ON "DocumentTemplateOrigins" TO saveit_app USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid) WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
        CREATE FUNCTION public.validate_document_template_v1() RETURNS trigger
        LANGUAGE plpgsql SECURITY INVOKER VOLATILE SET search_path=pg_catalog,pg_temp AS $$
        BEGIN
          IF TG_OP='INSERT' THEN
            IF NEW."Version"<>1 OR NEW."IsArchived" THEN RAISE EXCEPTION 'Invalid initial template' USING ERRCODE='23514'; END IF;
          ELSE
            IF (NEW."TenantId",NEW."OrganizationId",NEW."Id") IS DISTINCT FROM (OLD."TenantId",OLD."OrganizationId",OLD."Id")
              OR OLD."Version"=2147483647 OR NEW."Version"::bigint<>OLD."Version"::bigint+1 THEN
              RAISE EXCEPTION 'Invalid template revision' USING ERRCODE='23514';
            END IF;
          END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION public.validate_document_template_revision_v1() RETURNS trigger
        LANGUAGE plpgsql SECURITY INVOKER VOLATILE SET search_path=pg_catalog,pg_temp AS $$
        BEGIN
          IF NOT EXISTS (SELECT 1 FROM public."DocumentTemplates" h WHERE h."TenantId"=NEW."TenantId" AND h."OrganizationId"=NEW."OrganizationId" AND h."Id"=NEW."TemplateId"
            AND (h."Version",h."Name",h."IsArchived",h."UpdatedAt")=(NEW."Version",NEW."Name",NEW."IsArchived",NEW."CreatedAt")) THEN
            RAISE EXCEPTION 'Template snapshot must match its current head' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE FUNCTION public.check_document_template_tip_v1() RETURNS trigger
        LANGUAGE plpgsql SECURITY INVOKER VOLATILE SET search_path=pg_catalog,pg_temp AS $$
        BEGIN
          IF NOT EXISTS (SELECT 1 FROM public."DocumentTemplateRevisions" r
            WHERE (r."TenantId",r."OrganizationId",r."TemplateId",r."Version")=(NEW."TenantId",NEW."OrganizationId",NEW."Id",NEW."Version")
            AND (r."Name",r."IsArchived",r."CreatedAt")=(NEW."Name",NEW."IsArchived",NEW."UpdatedAt")) THEN
            RAISE EXCEPTION 'Template requires its immutable current snapshot' USING ERRCODE='23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE FUNCTION public.validate_document_template_origin_v1() RETURNS trigger
        LANGUAGE plpgsql SECURITY INVOKER VOLATILE SET search_path=pg_catalog,pg_temp AS $$
        DECLARE h record;
        BEGIN
          SELECT "Version","IsArchived" INTO h FROM public."DocumentTemplates" WHERE "TenantId"=NEW."TenantId"
            AND "OrganizationId"=NEW."OrganizationId" AND "Id"=NEW."TemplateId" FOR SHARE;
          IF NOT FOUND THEN RAISE EXCEPTION 'Template unavailable' USING ERRCODE='23503'; END IF;
          IF h."IsArchived" OR h."Version"<>NEW."TemplateVersion" OR NOT EXISTS (SELECT 1 FROM public."Documents" d
            WHERE (d."TenantId",d."OrganizationId",d."Id",d."Version")=(NEW."TenantId",NEW."OrganizationId",NEW."DocumentId",1)) THEN
            RAISE EXCEPTION 'Origin requires a new document and active current template' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        REVOKE ALL ON FUNCTION public.validate_document_template_v1(),public.validate_document_template_revision_v1(),public.check_document_template_tip_v1(),public.validate_document_template_origin_v1() FROM PUBLIC,saveit_search_worker;
        GRANT EXECUTE ON FUNCTION public.validate_document_template_v1(),public.validate_document_template_revision_v1(),public.check_document_template_tip_v1(),public.validate_document_template_origin_v1() TO saveit_app;
        CREATE TRIGGER document_template_version BEFORE INSERT OR UPDATE ON "DocumentTemplates" FOR EACH ROW EXECUTE FUNCTION public.validate_document_template_v1();
        CREATE TRIGGER document_template_snapshot BEFORE INSERT ON "DocumentTemplateRevisions" FOR EACH ROW EXECUTE FUNCTION public.validate_document_template_revision_v1();
        CREATE CONSTRAINT TRIGGER document_template_tip AFTER INSERT OR UPDATE ON "DocumentTemplates" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.check_document_template_tip_v1();
        CREATE TRIGGER document_template_origin BEFORE INSERT ON "DocumentTemplateOrigins" FOR EACH ROW EXECUTE FUNCTION public.validate_document_template_origin_v1();
        """;
    public const string Down = """
        DROP TRIGGER IF EXISTS document_template_origin ON "DocumentTemplateOrigins";
        DROP TRIGGER IF EXISTS document_template_tip ON "DocumentTemplates";
        DROP TRIGGER IF EXISTS document_template_snapshot ON "DocumentTemplateRevisions";
        DROP TRIGGER IF EXISTS document_template_version ON "DocumentTemplates";
        DROP FUNCTION IF EXISTS public.validate_document_template_origin_v1(),public.check_document_template_tip_v1(),public.validate_document_template_revision_v1(),public.validate_document_template_v1();
        """;
}
