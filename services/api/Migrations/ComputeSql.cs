namespace SaveIT.Api.Migrations;

internal static class ComputeSql
{
    public const string Up = """
        CREATE TABLE public."ComputeControl" (
          "Id" integer PRIMARY KEY CHECK ("Id"=1), "Version" bigint NOT NULL CHECK ("Version">0),
          "Phase" text NOT NULL CHECK ("Phase" IN ('Parked','Starting','Running','Quiescing','CoreQuiescent','Fencing')),
          "Generation" uuid NOT NULL CHECK ("Generation"<>'00000000-0000-0000-0000-000000000000'),
          "PlanSha256" text CHECK ("PlanSha256"~'^[0-9a-f]{64}$'), "OperationId" uuid NOT NULL,
          "UpdatedAt" timestamptz NOT NULL);
        CREATE TABLE public."ComputeOperations" (
          "Version" bigint PRIMARY KEY CHECK ("Version">0), "OperationId" uuid NOT NULL UNIQUE,
          "Action" text NOT NULL CHECK ("Action" IN ('initialize','prepare','activate','quiesce','drained','fencing','park')),
          "Phase" text NOT NULL, "Generation" uuid NOT NULL, "PlanSha256" text,
          "DatabaseLogin" name NOT NULL, "Operator" varchar(160) NOT NULL, "CreatedAt" timestamptz NOT NULL);
        CREATE INDEX "IX_ComputeOperations_Generation" ON public."ComputeOperations" ("Generation");
        DO $body$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['ComputeControl','ComputeOperations'] LOOP
            EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY',t);
            EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY',t);
            EXECUTE format('CREATE POLICY saveit_compute_owner ON public.%I TO saveit_migration_owner USING (true) WITH CHECK (true)',t);
            EXECUTE format('REVOKE ALL ON public.%I FROM PUBLIC,saveit_app,saveit_search_worker,saveit_access_operator,saveit_compute_operator',t);
          END LOOP;
        END $body$;

        CREATE FUNCTION public.compute_enter_v1(g uuid) RETURNS boolean
          LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
        DECLARE deadline timestamptz:=clock_timestamp()+interval '3 seconds'; state public."ComputeControl"%ROWTYPE;
        BEGIN
          IF current_setting('transaction_isolation')<>'read committed' THEN RAISE EXCEPTION 'Unsupported compute isolation' USING ERRCODE='25000'; END IF;
          LOOP
            EXIT WHEN pg_try_advisory_xact_lock_shared(91555333526868::bigint);
            IF clock_timestamp()>=deadline THEN RAISE EXCEPTION 'Compute admission busy' USING ERRCODE='55P03'; END IF;
            PERFORM pg_sleep(0.025);
          END LOOP;
          SELECT * INTO state FROM public."ComputeControl" WHERE "Id"=1;
          IF NOT FOUND THEN RETURN g IS NULL; END IF;
          RETURN state."Phase"='Running' AND g IS NOT NULL AND state."Generation"=g;
        END $$;
        CREATE FUNCTION public.compute_startup_v1(g uuid) RETURNS boolean
          LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
        BEGIN
          IF current_setting('transaction_isolation')<>'read committed' THEN RAISE EXCEPTION 'Unsupported compute isolation' USING ERRCODE='25000'; END IF;
          IF g IS NULL THEN RETURN NOT EXISTS (SELECT 1 FROM public."ComputeControl" WHERE "Id"=1); END IF;
          RETURN g IS NOT NULL AND EXISTS (SELECT 1 FROM public."ComputeControl" WHERE "Id"=1 AND "Generation"=g AND "Phase" IN ('Starting','Running'));
        END $$;
        REVOKE ALL ON FUNCTION public.compute_enter_v1(uuid),public.compute_startup_v1(uuid) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION public.compute_enter_v1(uuid),public.compute_startup_v1(uuid) TO saveit_app,saveit_search_worker;

        -- Preserve the original checked registration implementation and its actor/epoch rules.
        ALTER FUNCTION public.register_response_v1(uuid,uuid,text,integer,uuid,uuid) RENAME TO register_response_core_v1;
        REVOKE ALL ON FUNCTION public.register_response_core_v1(uuid,uuid,text,integer,uuid,uuid) FROM PUBLIC,saveit_app,saveit_search_worker;
        CREATE FUNCTION public.register_response_v1(t uuid,m uuid,s text,e integer,r uuid,i uuid) RETURNS boolean
          LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
        BEGIN
          -- Global gate precedes the original member gate. Completion stays unchanged and ungated.
          IF NOT public.compute_enter_v1(NULLIF(current_setting('app.compute_generation',true),'')::uuid) THEN RETURN false; END IF;
          RETURN public.register_response_core_v1(t,m,s,e,r,i);
        END $$;
        REVOKE ALL ON FUNCTION public.register_response_v1(uuid,uuid,text,integer,uuid,uuid) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION public.register_response_v1(uuid,uuid,text,integer,uuid,uuid) TO saveit_app;

        CREATE FUNCTION public.compute_change_v1(expected bigint,operation uuid,action text,g uuid,plan_sha text,attribution text) RETURNS bigint
          LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
        DECLARE deadline timestamptz:=clock_timestamp()+interval '3 seconds'; state public."ComputeControl"%ROWTYPE; next_phase text; next_generation uuid; next_plan text; next_version bigint;
        BEGIN
          IF current_setting('transaction_isolation')<>'read committed' THEN RAISE EXCEPTION 'Unsupported compute isolation' USING ERRCODE='25000'; END IF;
          IF expected IS NULL OR expected<0 OR operation IS NULL OR operation='00000000-0000-0000-0000-000000000000'::uuid
            OR action IS NULL OR attribution IS NULL OR NOT public.valid_asset_text_v1(attribution,160,true) OR attribution<>btrim(attribution) OR attribution~'[[:cntrl:]]'
            OR action NOT IN ('initialize','prepare','activate','quiesce','drained','fencing','park') THEN RAISE EXCEPTION 'Invalid compute operation' USING ERRCODE='22023'; END IF;
          IF action='prepare' THEN
            IF g IS NULL OR g='00000000-0000-0000-0000-000000000000'::uuid OR plan_sha IS NULL OR plan_sha!~'^[0-9a-f]{64}$' THEN RAISE EXCEPTION 'Invalid compute binding' USING ERRCODE='22023'; END IF;
          ELSIF g IS NOT NULL OR plan_sha IS NOT NULL THEN RAISE EXCEPTION 'Unexpected compute binding' USING ERRCODE='22023'; END IF;
          LOOP
            EXIT WHEN pg_try_advisory_xact_lock(91555333526868::bigint);
            IF clock_timestamp()>=deadline THEN RAISE EXCEPTION 'Compute change busy' USING ERRCODE='55P03'; END IF;
            PERFORM pg_sleep(0.025);
          END LOOP;
          SELECT * INTO state FROM public."ComputeControl" WHERE "Id"=1 FOR UPDATE;
          IF NOT FOUND THEN
            IF expected<>0 OR action<>'initialize' THEN RAISE EXCEPTION 'Compute version changed' USING ERRCODE='40001'; END IF;
            IF EXISTS (SELECT 1 FROM public."AccessResponses" WHERE "CompletedAt" IS NULL) THEN RAISE EXCEPTION 'Compute responses pending' USING ERRCODE='55P03'; END IF;
            next_phase:='Parked'; next_generation:=gen_random_uuid(); next_version:=1;
          ELSE
            IF expected<>state."Version" THEN RAISE EXCEPTION 'Compute version changed' USING ERRCODE='40001'; END IF;
            next_phase:=CASE
              WHEN action='prepare' AND state."Phase"='Parked' THEN 'Starting'
              WHEN action='activate' AND state."Phase"='Starting' THEN 'Running'
              WHEN action='quiesce' AND state."Phase" IN ('Starting','Running') THEN 'Quiescing'
              WHEN action='drained' AND state."Phase"='Quiescing' THEN 'CoreQuiescent'
              WHEN action='fencing' AND state."Phase"='CoreQuiescent' THEN 'Fencing'
              WHEN action='park' AND state."Phase"='Fencing' THEN 'Parked' END;
            IF next_phase IS NULL THEN RAISE EXCEPTION 'Invalid compute transition' USING ERRCODE='22023'; END IF;
            IF action IN ('prepare','drained','fencing','park') AND EXISTS (SELECT 1 FROM public."AccessResponses" WHERE "CompletedAt" IS NULL) THEN RAISE EXCEPTION 'Compute responses pending' USING ERRCODE='55P03'; END IF;
            IF action='prepare' AND EXISTS (SELECT 1 FROM public."ComputeOperations" WHERE "Generation"=g) THEN RAISE EXCEPTION 'Compute generation already used' USING ERRCODE='22023'; END IF;
            next_generation:=CASE WHEN action='prepare' THEN g ELSE state."Generation" END;
            next_plan:=CASE WHEN action='prepare' THEN plan_sha ELSE state."PlanSha256" END;
            next_version:=state."Version"+1;
          END IF;
          INSERT INTO public."ComputeControl" ("Id","Version","Phase","Generation","PlanSha256","OperationId","UpdatedAt")
            VALUES (1,next_version,next_phase,next_generation,next_plan,operation,clock_timestamp())
            ON CONFLICT ("Id") DO UPDATE SET "Version"=EXCLUDED."Version","Phase"=EXCLUDED."Phase","Generation"=EXCLUDED."Generation","PlanSha256"=EXCLUDED."PlanSha256","OperationId"=EXCLUDED."OperationId","UpdatedAt"=EXCLUDED."UpdatedAt";
          INSERT INTO public."ComputeOperations" ("Version","OperationId","Action","Phase","Generation","PlanSha256","DatabaseLogin","Operator","CreatedAt")
            VALUES (next_version,operation,action,next_phase,next_generation,next_plan,session_user,attribution,clock_timestamp());
          RETURN next_version;
        END $$;
        CREATE FUNCTION public.compute_inspect_v1() RETURNS jsonb
          LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
        DECLARE state public."ComputeControl"%ROWTYPE; pending integer;
        BEGIN
          IF current_setting('transaction_isolation')<>'read committed' THEN RAISE EXCEPTION 'Unsupported compute isolation' USING ERRCODE='25000'; END IF;
          SELECT * INTO state FROM public."ComputeControl" WHERE "Id"=1;
          SELECT count(*) INTO pending FROM (SELECT 1 FROM public."AccessResponses" WHERE "CompletedAt" IS NULL LIMIT 1001) p;
          RETURN jsonb_build_object('version',COALESCE(state."Version",0),'phase',state."Phase",'generation',state."Generation",'planSha256',state."PlanSha256",'operationId',state."OperationId",'pendingResponses',LEAST(pending,1000),'pendingResponsesCapped',pending>1000);
        END $$;
        REVOKE ALL ON FUNCTION public.compute_change_v1(bigint,uuid,text,uuid,text,text),public.compute_inspect_v1() FROM PUBLIC,saveit_app,saveit_search_worker,saveit_access_operator;
        GRANT EXECUTE ON FUNCTION public.compute_change_v1(bigint,uuid,text,uuid,text,text),public.compute_inspect_v1() TO saveit_compute_operator;
        GRANT USAGE ON SCHEMA public TO saveit_compute_operator;
        GRANT SELECT ("MigrationId") ON public."__EFMigrationsHistory" TO saveit_compute_operator;
        """;

    public const string Down = """
        DROP FUNCTION public.register_response_v1(uuid,uuid,text,integer,uuid,uuid);
        ALTER FUNCTION public.register_response_core_v1(uuid,uuid,text,integer,uuid,uuid) RENAME TO register_response_v1;
        GRANT EXECUTE ON FUNCTION public.register_response_v1(uuid,uuid,text,integer,uuid,uuid) TO saveit_app;
        DROP FUNCTION public.compute_enter_v1(uuid),public.compute_startup_v1(uuid),public.compute_change_v1(bigint,uuid,text,uuid,text,text),public.compute_inspect_v1();
        DROP TABLE public."ComputeOperations",public."ComputeControl";
        """;
}
