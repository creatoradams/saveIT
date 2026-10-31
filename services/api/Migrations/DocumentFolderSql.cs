namespace SaveIT.Api.Migrations;

internal static class DocumentFolderSql
{
    internal const string Up = """
        REVOKE ALL ON "DocumentFolders","DocumentPlacements" FROM PUBLIC,saveit_app,saveit_search_worker;
        GRANT SELECT,INSERT ON "DocumentFolders","DocumentPlacements" TO saveit_app;
        GRANT UPDATE ("Name","ParentId","Version","UpdatedAt") ON "DocumentFolders" TO saveit_app;
        GRANT UPDATE ("FolderId","Version","UpdatedAt") ON "DocumentPlacements" TO saveit_app;
        ALTER TABLE "DocumentFolders" ENABLE ROW LEVEL SECURITY;
        ALTER TABLE "DocumentFolders" FORCE ROW LEVEL SECURITY;
        ALTER TABLE "DocumentPlacements" ENABLE ROW LEVEL SECURITY;
        ALTER TABLE "DocumentPlacements" FORCE ROW LEVEL SECURITY;
        CREATE POLICY tenant_boundary ON "DocumentFolders" FOR ALL TO saveit_app
          USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
          WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
        CREATE POLICY tenant_boundary ON "DocumentPlacements" FOR ALL TO saveit_app
          USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
          WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);

        CREATE FUNCTION public.validate_document_folder_v1() RETURNS trigger
        LANGUAGE plpgsql VOLATILE SECURITY INVOKER SET search_path=pg_catalog,pg_temp AS $function$
        DECLARE folder_count integer; parent_depth integer := 0; subtree_height integer; cycle boolean; rooted boolean;
        BEGIN
          IF current_setting('transaction_isolation')<>'read committed' THEN
            RAISE EXCEPTION 'Folder writes require read committed' USING ERRCODE='25000';
          END IF;
          IF TG_OP='INSERT' THEN
            IF NEW."Version"<>1 THEN RAISE EXCEPTION 'Invalid folder version' USING ERRCODE='23514'; END IF;
          ELSE
            IF NEW."TenantId" IS DISTINCT FROM OLD."TenantId" OR NEW."OrganizationId" IS DISTINCT FROM OLD."OrganizationId"
              OR NEW."Id" IS DISTINCT FROM OLD."Id" OR OLD."Version"=2147483647 OR NEW."Version"<>OLD."Version"+1 THEN
              RAISE EXCEPTION 'Invalid folder revision' USING ERRCODE='23514';
            END IF;
          END IF;
          -- Keep this expression identical to the API's separate pre-read lock statement.
          PERFORM pg_advisory_xact_lock(hashtextextended(NEW."TenantId"::text || ':' || NEW."OrganizationId"::text,190019));
          -- Separate VOLATILE queries see committed writes after a READ COMMITTED lock wait.
          SELECT count(*) INTO folder_count FROM (
            SELECT 1 FROM public."DocumentFolders" f WHERE f."TenantId"=NEW."TenantId" AND f."OrganizationId"=NEW."OrganizationId" LIMIT 1001
          ) bounded;
          IF folder_count>1000 OR (TG_OP='INSERT' AND folder_count>=1000) THEN
            RAISE EXCEPTION 'Folder capacity exceeded' USING ERRCODE='54000';
          END IF;
          IF NEW."ParentId" IS NOT NULL THEN
            IF NOT EXISTS (SELECT 1 FROM public."DocumentFolders" f WHERE f."TenantId"=NEW."TenantId" AND f."OrganizationId"=NEW."OrganizationId" AND f."Id"=NEW."ParentId") THEN
              RAISE EXCEPTION 'Invalid folder parent' USING ERRCODE='23503';
            END IF;
            WITH RECURSIVE ancestors AS (
              SELECT f."Id",f."ParentId",1 AS depth FROM public."DocumentFolders" f
                WHERE f."TenantId"=NEW."TenantId" AND f."OrganizationId"=NEW."OrganizationId" AND f."Id"=NEW."ParentId"
              UNION ALL SELECT f."Id",f."ParentId",a.depth+1 FROM ancestors a JOIN public."DocumentFolders" f
                ON f."TenantId"=NEW."TenantId" AND f."OrganizationId"=NEW."OrganizationId" AND f."Id"=a."ParentId" WHERE a.depth<9
            ) SELECT max(depth),bool_or("Id"=NEW."Id"),bool_or("ParentId" IS NULL) INTO parent_depth,cycle,rooted FROM ancestors;
            IF cycle OR parent_depth>8 OR NOT rooted THEN RAISE EXCEPTION 'Invalid folder cycle or depth' USING ERRCODE='23514'; END IF;
          END IF;
          WITH RECURSIVE descendants AS (
            SELECT NEW."Id" AS id,1 AS depth
            UNION ALL SELECT f."Id",d.depth+1 FROM descendants d JOIN public."DocumentFolders" f
              ON f."TenantId"=NEW."TenantId" AND f."OrganizationId"=NEW."OrganizationId" AND f."ParentId"=d.id WHERE d.depth<9
          ) SELECT max(depth) INTO subtree_height FROM descendants;
          IF parent_depth+subtree_height>8 THEN RAISE EXCEPTION 'Folder depth exceeded' USING ERRCODE='23514'; END IF;
          RETURN NEW;
        END $function$;
        REVOKE ALL ON FUNCTION public.validate_document_folder_v1() FROM PUBLIC,saveit_search_worker;
        GRANT EXECUTE ON FUNCTION public.validate_document_folder_v1() TO saveit_app;
        CREATE TRIGGER document_folder_boundary BEFORE INSERT OR UPDATE ON "DocumentFolders"
          FOR EACH ROW EXECUTE FUNCTION public.validate_document_folder_v1();

        CREATE FUNCTION public.validate_document_placement_v1() RETURNS trigger
        LANGUAGE plpgsql VOLATILE SECURITY INVOKER SET search_path=pg_catalog,pg_temp AS $function$
        BEGIN
          IF current_setting('transaction_isolation')<>'read committed' THEN
            RAISE EXCEPTION 'Placement writes require read committed' USING ERRCODE='25000';
          END IF;
          IF TG_OP='INSERT' THEN
            IF NEW."Version"<>1 THEN RAISE EXCEPTION 'Invalid placement version' USING ERRCODE='23514'; END IF;
          ELSE
            IF NEW."TenantId" IS DISTINCT FROM OLD."TenantId" OR NEW."OrganizationId" IS DISTINCT FROM OLD."OrganizationId"
              OR NEW."DocumentId" IS DISTINCT FROM OLD."DocumentId" OR OLD."Version"=2147483647 OR NEW."Version"<>OLD."Version"+1 THEN
              RAISE EXCEPTION 'Invalid placement revision' USING ERRCODE='23514';
            END IF;
          END IF;
          RETURN NEW;
        END $function$;
        REVOKE ALL ON FUNCTION public.validate_document_placement_v1() FROM PUBLIC,saveit_search_worker;
        GRANT EXECUTE ON FUNCTION public.validate_document_placement_v1() TO saveit_app;
        CREATE TRIGGER document_placement_boundary BEFORE INSERT OR UPDATE ON "DocumentPlacements"
          FOR EACH ROW EXECUTE FUNCTION public.validate_document_placement_v1();
        """;

    internal const string Down = """
        DROP TRIGGER document_placement_boundary ON "DocumentPlacements";
        DROP TRIGGER document_folder_boundary ON "DocumentFolders";
        DROP FUNCTION public.validate_document_placement_v1();
        DROP FUNCTION public.validate_document_folder_v1();
        """;
}
