namespace SaveIT.Api.Migrations;

// Versioned validation semantics are immutable. Future formats require new functions/migrations;
// never replace v1 in a way that reinterprets a pinned historical snapshot.
internal static class TypedAssetSchemaSql
{
    public const string Validators = """
        CREATE FUNCTION public.valid_asset_text_v1(s text, lim integer, required boolean) RETURNS boolean
        LANGUAGE sql IMMUTABLE SET search_path=pg_catalog,pg_temp AS $$
          SELECT COALESCE(length(s)<=lim AND (NOT required OR length(btrim(s,E' \t\r\n'))>0)
            AND translate(s,E'\t\r\n','') !~ '[[:cntrl:]]',false)
        $$;
        CREATE FUNCTION public.valid_asset_value_v1(f jsonb, v jsonb) RETURNS boolean
        LANGUAGE plpgsql IMMUTABLE SET search_path=pg_catalog,pg_temp AS $$
        DECLARE s text; n numeric; d date;
        BEGIN
          IF v IS NULL OR v='null'::jsonb THEN RETURN false; END IF;
          CASE f->>'type'
          WHEN 'text' THEN RETURN jsonb_typeof(v)='string' AND public.valid_asset_text_v1(v#>>'{}',500,(f->>'required')::boolean);
          WHEN 'number' THEN
            IF jsonb_typeof(v)<>'number' THEN RETURN false; END IF;
            n := (v#>>'{}')::numeric; RETURN n BETWEEN -1000000000 AND 1000000000 AND round(n,3)=n;
          WHEN 'boolean' THEN RETURN jsonb_typeof(v)='boolean';
          WHEN 'date' THEN
            IF jsonb_typeof(v)<>'string' THEN RETURN false; END IF;
            s := v#>>'{}'; IF s !~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' THEN RETURN false; END IF;
            d := make_date(substring(s,1,4)::integer,substring(s,6,2)::integer,substring(s,9,2)::integer);
            RETURN true;
          WHEN 'choice' THEN RETURN jsonb_typeof(v)='string' AND COALESCE((f->'options') @> jsonb_build_array(v),false);
          ELSE RETURN false;
          END CASE;
        EXCEPTION WHEN data_exception THEN RETURN false;
        END $$;
        CREATE FUNCTION public.valid_asset_definition_v1(s jsonb) RETURNS boolean
        LANGUAGE plpgsql IMMUTABLE SET search_path=pg_catalog,pg_temp AS $$
        DECLARE f jsonb; k text; seen text[] := '{}'; opts jsonb;
        BEGIN
          IF jsonb_typeof(s) IS DISTINCT FROM 'array' OR octet_length(s::text)>16384 THEN RETURN false; END IF;
          IF jsonb_array_length(s) NOT BETWEEN 1 AND 20 THEN RETURN false; END IF;
          FOR f IN SELECT value FROM jsonb_array_elements(s) LOOP
            IF jsonb_typeof(f) IS DISTINCT FROM 'object' OR NOT f ?& ARRAY['key','label','type','required'] THEN RETURN false; END IF;
            IF EXISTS (SELECT 1 FROM jsonb_object_keys(f) AS keys(key) WHERE key NOT IN ('key','label','type','required','options','defaultValue')) THEN RETURN false; END IF;
            IF jsonb_typeof(f->'key') IS DISTINCT FROM 'string' OR jsonb_typeof(f->'label') IS DISTINCT FROM 'string'
              OR jsonb_typeof(f->'type') IS DISTINCT FROM 'string' OR jsonb_typeof(f->'required') IS DISTINCT FROM 'boolean' THEN RETURN false; END IF;
            k := f->>'key';
            IF k !~ '^[a-z][a-z0-9_]{0,31}$' OR k=ANY(seen) OR NOT public.valid_asset_text_v1(f->>'label',80,true)
              OR f->>'type' NOT IN ('text','number','boolean','date','choice') THEN RETURN false; END IF;
            seen := array_append(seen,k); opts := f->'options';
            IF f->>'type'='choice' THEN
              IF jsonb_typeof(opts) IS DISTINCT FROM 'array' THEN RETURN false; END IF;
              IF jsonb_array_length(opts) NOT BETWEEN 1 AND 20 THEN RETURN false; END IF;
              IF EXISTS (SELECT 1 FROM jsonb_array_elements(opts) AS o(v) WHERE jsonb_typeof(v)<>'string' OR NOT public.valid_asset_text_v1(v#>>'{}',80,true))
                OR (SELECT count(DISTINCT v) FROM jsonb_array_elements(opts) AS o(v)) <> jsonb_array_length(opts) THEN RETURN false; END IF;
            ELSIF opts IS NOT NULL AND opts<>'null'::jsonb THEN RETURN false;
            END IF;
            IF f ? 'defaultValue' AND f->'defaultValue'<>'null'::jsonb AND public.valid_asset_value_v1(f,f->'defaultValue') IS NOT TRUE THEN RETURN false; END IF;
          END LOOP;
          RETURN true;
        EXCEPTION WHEN data_exception THEN RETURN false;
        END $$;
        CREATE FUNCTION public.valid_asset_fields_v1(s jsonb, vals jsonb) RETURNS boolean
        LANGUAGE plpgsql IMMUTABLE SET search_path=pg_catalog,pg_temp AS $$
        DECLARE f jsonb; k text;
        BEGIN
          IF public.valid_asset_definition_v1(s) IS NOT TRUE OR jsonb_typeof(vals) IS DISTINCT FROM 'object' OR octet_length(vals::text)>16384 THEN RETURN false; END IF;
          IF EXISTS (SELECT 1 FROM jsonb_object_keys(vals) AS keys(key) WHERE NOT EXISTS (SELECT 1 FROM jsonb_array_elements(s) AS defs(def) WHERE defs.def->>'key'=keys.key)) THEN RETURN false; END IF;
          FOR f IN SELECT value FROM jsonb_array_elements(s) LOOP
            k := f->>'key';
            IF vals ? k THEN
              IF public.valid_asset_value_v1(f,vals->k) IS NOT TRUE THEN RETURN false; END IF;
            ELSIF (f->>'required')::boolean THEN RETURN false;
            END IF;
          END LOOP;
          RETURN true;
        END $$;
        REVOKE ALL ON FUNCTION public.valid_asset_text_v1(text,integer,boolean),public.valid_asset_value_v1(jsonb,jsonb),public.valid_asset_definition_v1(jsonb),public.valid_asset_fields_v1(jsonb,jsonb) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION public.valid_asset_text_v1(text,integer,boolean),public.valid_asset_value_v1(jsonb,jsonb),public.valid_asset_definition_v1(jsonb),public.valid_asset_fields_v1(jsonb,jsonb) TO saveit_app;
        """;

    public const string Enforcement = """
        GRANT SELECT,INSERT ON "AssetTypes","AssetTypeRevisions" TO saveit_app;
        GRANT UPDATE ("Name","Version","IsArchived","UpdatedAt") ON "AssetTypes" TO saveit_app;
        REVOKE SELECT ON "Assets" FROM saveit_search_worker;
        GRANT SELECT ("TenantId","OrganizationId","Id","Name","Kind","Version","UpdatedAt") ON "Assets" TO saveit_search_worker;
        CREATE FUNCTION public.enforce_asset_schema_v1() RETURNS trigger
        LANGUAGE plpgsql SET search_path=pg_catalog,pg_temp AS $$
        DECLARE s jsonb;
        BEGIN
          IF NEW."AssetTypeId" IS NULL AND NEW."AssetTypeVersion" IS NULL AND NEW."FieldsJson"='{}'::jsonb THEN RETURN NEW; END IF;
          IF NEW."AssetTypeId" IS NULL OR NEW."AssetTypeVersion" IS NULL THEN
            RAISE EXCEPTION 'Invalid asset template reference' USING ERRCODE='23514';
          END IF;
          SELECT "DefinitionJson" INTO s FROM public."AssetTypeRevisions" WHERE "TenantId"=NEW."TenantId"
            AND "OrganizationId"=NEW."OrganizationId" AND "AssetTypeId"=NEW."AssetTypeId" AND "Version"=NEW."AssetTypeVersion";
          IF s IS NULL THEN RAISE EXCEPTION 'Asset template unavailable' USING ERRCODE='23503'; END IF;
          IF public.valid_asset_fields_v1(s,NEW."FieldsJson") IS NOT TRUE THEN
            RAISE EXCEPTION 'Invalid asset fields' USING ERRCODE='23514';
          END IF;
          RETURN NEW;
        END $$;
        REVOKE ALL ON FUNCTION public.enforce_asset_schema_v1() FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION public.enforce_asset_schema_v1() TO saveit_app;
        CREATE TRIGGER asset_schema BEFORE INSERT OR UPDATE ON "Assets" FOR EACH ROW EXECUTE FUNCTION public.enforce_asset_schema_v1();
        CREATE TRIGGER asset_revision_schema BEFORE INSERT ON "AssetRevisions" FOR EACH ROW EXECUTE FUNCTION public.enforce_asset_schema_v1();
        """;
}
