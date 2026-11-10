namespace SaveIT.Api.Migrations;

internal static class ComputeReleaseSql
{
    public const string Up="""
        ALTER TABLE public."ComputeOperations" ADD COLUMN "ReleaseJws" text, ADD COLUMN "ReleaseKeySha256" text;
        ALTER TABLE public."ComputeOperations" ADD CONSTRAINT "CK_ComputeOperations_Release" CHECK (
          ("ReleaseJws" IS NULL AND "ReleaseKeySha256" IS NULL) OR
          ("ReleaseJws" IS NOT NULL AND "ReleaseKeySha256" IS NOT NULL AND "PlanSha256" IS NOT NULL AND "Action"='prepare' AND
            length("ReleaseJws") BETWEEN 1 AND 49152 AND "ReleaseJws"~'^[A-Za-z0-9_-]+[.][A-Za-z0-9_-]+[.][A-Za-z0-9_-]+$' AND
            "ReleaseKeySha256"~'^[a-f0-9]{64}$' AND "PlanSha256"=encode(sha256(convert_to("ReleaseJws",'UTF8')),'hex')));

        CREATE FUNCTION public.compute_admit_v1(expected bigint,operation uuid,g uuid,plan_sha text,key_sha text,release_jws text,attribution text) RETURNS bigint
          LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
        DECLARE v bigint; h jsonb; p jsonb; part text; now_seconds bigint; issued bigint; expires bigint;
        BEGIN
          IF release_jws IS NULL OR length(release_jws) NOT BETWEEN 1 AND 49152 OR release_jws!~'^[A-Za-z0-9_-]+[.][A-Za-z0-9_-]+[.][A-Za-z0-9_-]+$'
            OR key_sha IS NULL OR key_sha!~'^[a-f0-9]{64}$' OR plan_sha IS NULL OR plan_sha<>encode(sha256(convert_to(release_jws,'UTF8')),'hex') THEN RAISE EXCEPTION 'Invalid compute release binding' USING ERRCODE='22023'; END IF;
          -- The trusted runner verifies the signature. These checks catch binding/freshness errors at the durable gate.
          part:=split_part(release_jws,'.',1); h:=convert_from(decode(translate(part,'-_','+/')||repeat('=',(4-length(part)%4)%4),'base64'),'UTF8')::jsonb;
          part:=split_part(release_jws,'.',2); p:=convert_from(decode(translate(part,'-_','+/')||repeat('=',(4-length(part)%4)%4),'base64'),'UTF8')::jsonb;
          IF jsonb_typeof(h)<>'object' OR jsonb_typeof(p)<>'object' OR
            h->>'alg' IS DISTINCT FROM 'PS256' OR h->>'typ' IS DISTINCT FROM 'saveit-compute-release+jwt' OR h->>'kid' IS DISTINCT FROM 'sha256-'||key_sha OR
            p->>'iss' IS DISTINCT FROM 'saveit-release' OR p->>'jti' IS DISTINCT FROM operation::text OR
            p#>>'{contract,generation}' IS DISTINCT FROM g::text OR (p#>>'{contract,expectedVersion}')::bigint IS DISTINCT FROM expected OR
            jsonb_typeof(p->'iat') IS DISTINCT FROM 'number' OR jsonb_typeof(p->'exp') IS DISTINCT FROM 'number' THEN RAISE EXCEPTION 'Compute release does not match operation' USING ERRCODE='22023'; END IF;
          issued:=(p->>'iat')::bigint; expires:=(p->>'exp')::bigint;
          v:=public.compute_change_v1(expected,operation,'prepare',g,plan_sha,attribution);
          -- compute_change holds the exclusive global gate; a wait cannot preserve a stale approval snapshot.
          now_seconds:=floor(extract(epoch FROM clock_timestamp()))::bigint;
          IF issued>now_seconds OR issued<now_seconds-900 OR expires<=now_seconds OR expires<=issued OR expires>issued+900 THEN RAISE EXCEPTION 'Compute release expired' USING ERRCODE='22023'; END IF;
          UPDATE public."ComputeOperations" SET "ReleaseJws"=release_jws,"ReleaseKeySha256"=key_sha WHERE "Version"=v AND "OperationId"=operation AND "ReleaseJws" IS NULL;
          IF NOT FOUND THEN RAISE EXCEPTION 'Compute release receipt unavailable' USING ERRCODE='22023'; END IF;
          RETURN v;
        END $$;

        CREATE FUNCTION public.compute_release_status_v1(operation uuid) RETURNS jsonb
          LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
        DECLARE receipt public."ComputeOperations"%ROWTYPE; state public."ComputeControl"%ROWTYPE;
        BEGIN
          IF current_setting('transaction_isolation')<>'read committed' OR operation IS NULL OR operation='00000000-0000-0000-0000-000000000000'::uuid THEN RAISE EXCEPTION 'Invalid compute inspection' USING ERRCODE='22023'; END IF;
          SELECT * INTO receipt FROM public."ComputeOperations" WHERE "OperationId"=operation AND "ReleaseJws" IS NOT NULL;
          IF NOT FOUND THEN RETURN jsonb_build_object('operationId',operation,'admitted',false); END IF;
          SELECT * INTO state FROM public."ComputeControl" WHERE "Id"=1;
          RETURN jsonb_build_object('operationId',operation,'admitted',true,'admittedVersion',receipt."Version",'generation',receipt."Generation",'planSha256',receipt."PlanSha256",'keySha256',receipt."ReleaseKeySha256",'currentVersion',state."Version",'currentPhase',state."Phase",'currentGenerationMatches',state."Generation"=receipt."Generation");
        END $$;

        CREATE FUNCTION public.compute_release_v1(operation uuid) RETURNS text
          LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
        DECLARE result text;
        BEGIN
          IF current_setting('transaction_isolation')<>'read committed' OR operation IS NULL OR operation='00000000-0000-0000-0000-000000000000'::uuid THEN RAISE EXCEPTION 'Invalid compute inspection' USING ERRCODE='22023'; END IF;
          SELECT "ReleaseJws" INTO result FROM public."ComputeOperations" WHERE "OperationId"=operation AND "ReleaseJws" IS NOT NULL;
          IF NOT FOUND THEN RAISE EXCEPTION 'Compute release not admitted' USING ERRCODE='22023'; END IF;
          RETURN result;
        END $$;
        REVOKE ALL ON FUNCTION public.compute_admit_v1(bigint,uuid,uuid,text,text,text,text),public.compute_release_status_v1(uuid),public.compute_release_v1(uuid) FROM PUBLIC,saveit_app,saveit_search_worker,saveit_access_operator;
        GRANT EXECUTE ON FUNCTION public.compute_admit_v1(bigint,uuid,uuid,text,text,text,text),public.compute_release_status_v1(uuid),public.compute_release_v1(uuid) TO saveit_compute_operator;
        """;
    public const string Down="""
        DROP FUNCTION public.compute_admit_v1(bigint,uuid,uuid,text,text,text,text),public.compute_release_status_v1(uuid),public.compute_release_v1(uuid);
        ALTER TABLE public."ComputeOperations" DROP CONSTRAINT "CK_ComputeOperations_Release", DROP COLUMN "ReleaseJws", DROP COLUMN "ReleaseKeySha256";
        """;
}
