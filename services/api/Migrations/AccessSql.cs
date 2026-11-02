namespace SaveIT.Api.Migrations;

internal static class AccessSql
{
    public const string Up = """
        DO $body$ DECLARE t text; BEGIN
          FOREACH t IN ARRAY ARRAY['AccessResponses','AccessAudits','AccessControlEvents'] LOOP
            EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY',t);
            EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY',t);
            EXECUTE format('CREATE POLICY tenant_isolation ON public.%I USING ("TenantId"=NULLIF(current_setting(''app.tenant_id'',true),'''')::uuid) WITH CHECK ("TenantId"=NULLIF(current_setting(''app.tenant_id'',true),'''')::uuid)',t);
            -- A fixed migration owner is not BYPASSRLS. Definers still qualify every source by scope.
            EXECUTE format('CREATE POLICY saveit_access_owner ON public.%I TO saveit_migration_owner USING (true) WITH CHECK (true)',t);
            EXECUTE format('REVOKE ALL ON public.%I FROM PUBLIC,saveit_app,saveit_search_worker,saveit_access_operator',t);
          END LOOP;
        END $body$;

        CREATE FUNCTION public.access_key_v1(t uuid,m uuid) RETURNS bigint
          LANGUAGE sql IMMUTABLE STRICT SECURITY INVOKER SET search_path=pg_catalog,pg_temp
          AS $$ SELECT hashtextextended('saveit.access.v1:'||t::text||':'||m::text,0) $$;
        REVOKE ALL ON FUNCTION public.access_key_v1(uuid,uuid) FROM PUBLIC;

        -- VOLATILE SPI statements obtain fresh READ COMMITTED snapshots AFTER advisory waits.
        -- Do not combine the wait and authority/drain query into one ordinary statement/CTE.
        CREATE FUNCTION public.register_response_v1(t uuid,m uuid,s text,e integer,r uuid,i uuid) RETURNS boolean
          LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
        DECLARE deadline timestamptz := clock_timestamp()+interval '3 seconds';
        BEGIN
          IF current_setting('transaction_isolation')<>'read committed' THEN RAISE EXCEPTION 'Unsupported access isolation' USING ERRCODE='25000'; END IF;
          IF t IS NULL OR m IS NULL OR r IS NULL OR i IS NULL OR s IS NULL OR e IS NULL OR e<1
            OR t IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid
            OR m IS DISTINCT FROM NULLIF(current_setting('app.membership_id',true),'')::uuid THEN
            RAISE EXCEPTION 'Invalid response authority' USING ERRCODE='42501';
          END IF;
          LOOP
            EXIT WHEN pg_try_advisory_xact_lock_shared(public.access_key_v1(t,m));
            IF clock_timestamp()>=deadline THEN RAISE EXCEPTION 'Access registration busy' USING ERRCODE='55P03'; END IF;
            PERFORM pg_sleep(0.025);
          END LOOP;
          PERFORM 1 FROM public."Memberships" WHERE "TenantId"=t AND "Id"=m AND "Subject"=s AND "Active" AND "SessionEpoch"=e;
          IF NOT FOUND THEN RETURN false; END IF;
          INSERT INTO public."AccessResponses" ("TenantId","MembershipId","RequestId","InstanceId","StartedAt") VALUES (t,m,r,i,clock_timestamp());
          RETURN true;
        END $$;

        CREATE FUNCTION public.complete_response_v1(t uuid,m uuid,r uuid,i uuid,aborted boolean) RETURNS void
          LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
        BEGIN
          IF t IS NULL OR m IS NULL OR r IS NULL OR i IS NULL OR aborted IS NULL
            OR t IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid
            OR m IS DISTINCT FROM NULLIF(current_setting('app.membership_id',true),'')::uuid THEN
            RAISE EXCEPTION 'Invalid response completion' USING ERRCODE='42501';
          END IF;
          -- No registration gate or membership row lock: operator drain must not block closure.
          UPDATE public."AccessResponses" SET "CompletedAt"=GREATEST(clock_timestamp(),"StartedAt"),"Outcome"=CASE WHEN aborted THEN 'Aborted' ELSE 'Completed' END
            WHERE "TenantId"=t AND "MembershipId"=m AND "RequestId"=r AND "InstanceId"=i AND "CompletedAt" IS NULL;
          -- Bounded per-member retention; NEVER prune uncertain pending responses.
          DELETE FROM public."AccessResponses" WHERE ("TenantId","MembershipId","RequestId") IN (
            SELECT "TenantId","MembershipId","RequestId" FROM public."AccessResponses"
            WHERE "TenantId"=t AND "MembershipId"=m AND "CompletedAt"<clock_timestamp()-interval '7 days'
            ORDER BY "CompletedAt","RequestId" LIMIT 32 FOR UPDATE SKIP LOCKED);
        END $$;
        REVOKE ALL ON FUNCTION public.register_response_v1(uuid,uuid,text,integer,uuid,uuid),public.complete_response_v1(uuid,uuid,uuid,uuid,boolean) FROM PUBLIC;
        GRANT EXECUTE ON FUNCTION public.register_response_v1(uuid,uuid,text,integer,uuid,uuid),public.complete_response_v1(uuid,uuid,uuid,uuid,boolean) TO saveit_app;

        CREATE FUNCTION public.access_change_v1(t uuid,m uuid,expected integer,operation text,org uuid,new_role text,enabled boolean,display_name text,issuer text,external_subject text,attribution text) RETURNS integer
          LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
        DECLARE deadline timestamptz:=clock_timestamp()+interval '5 seconds'; member public."Memberships"%ROWTYPE;
          audit_id uuid:=gen_random_uuid(); action text; prior_role text;
        BEGIN
          IF current_setting('transaction_isolation')<>'read committed' THEN RAISE EXCEPTION 'Unsupported access isolation' USING ERRCODE='25000'; END IF;
          IF t IS NULL OR m IS NULL OR t='00000000-0000-0000-0000-000000000000'::uuid OR m='00000000-0000-0000-0000-000000000000'::uuid
            OR expected IS NULL OR operation IS NULL OR attribution IS NULL OR NOT public.valid_asset_text_v1(attribution,160,true)
            OR attribution<>btrim(attribution) OR attribution~'[[:cntrl:]]'
            OR operation NOT IN ('create','active','grant','capability') THEN
            RAISE EXCEPTION 'Invalid access operation' USING ERRCODE='22023';
          END IF;
          IF operation='create' THEN
            IF expected<>0 OR org IS NULL OR new_role IS NULL OR new_role NOT IN ('Viewer','Editor') OR enabled IS NOT NULL
              OR display_name IS NULL OR NOT public.valid_asset_text_v1(display_name,160,true) OR display_name<>btrim(display_name) OR display_name~'[[:cntrl:]]'
              OR issuer IS NULL OR length(issuer)>500 OR issuer!~'^https://[^/?#@[:space:]]+(/[^?#[:space:]]*)?$' OR right(issuer,1)='/'
              OR external_subject IS NULL OR NOT public.valid_asset_text_v1(external_subject,256,true) OR external_subject<>btrim(external_subject) OR external_subject~'[[:cntrl:]]' THEN
              RAISE EXCEPTION 'Invalid mapped membership' USING ERRCODE='22023';
            END IF;
          ELSIF expected<1 OR display_name IS NOT NULL OR issuer IS NOT NULL OR external_subject IS NOT NULL
            OR (operation IN ('active','capability') AND (enabled IS NULL OR org IS NOT NULL OR new_role IS NOT NULL))
            OR (operation='grant' AND (org IS NULL OR enabled IS NOT NULL OR (new_role IS NOT NULL AND new_role NOT IN ('Viewer','Editor')))) THEN
            RAISE EXCEPTION 'Invalid access change' USING ERRCODE='22023';
          END IF;
          -- Lock BEFORE any core row lock. Existing responses may still need membership/grant rows.
          LOOP
            EXIT WHEN pg_try_advisory_xact_lock(public.access_key_v1(t,m));
            IF clock_timestamp()>=deadline THEN RAISE EXCEPTION 'Access barrier busy' USING ERRCODE='55P03'; END IF;
            PERFORM pg_sleep(0.025);
          END LOOP;
          LOOP
            EXIT WHEN NOT EXISTS (SELECT 1 FROM public."AccessResponses" WHERE "TenantId"=t AND "MembershipId"=m AND "CompletedAt" IS NULL);
            IF clock_timestamp()>=deadline THEN RAISE EXCEPTION 'Responses have not drained; access unchanged' USING ERRCODE='55P03'; END IF;
            PERFORM pg_sleep(0.025);
          END LOOP;
          -- All sources are explicitly tenant-qualified, including under the owner-only RLS policy.
          PERFORM 1 FROM public."Tenants" WHERE "Id"=t;
          IF NOT FOUND THEN RAISE EXCEPTION 'Missing access source' USING ERRCODE='23503'; END IF;
          IF org IS NOT NULL THEN
            PERFORM 1 FROM public."Organizations" WHERE "TenantId"=t AND "Id"=org FOR KEY SHARE;
            IF NOT FOUND THEN RAISE EXCEPTION 'Missing access organization' USING ERRCODE='23503'; END IF;
          END IF;
          IF operation='create' THEN
            INSERT INTO public."Memberships" ("Id","TenantId","Subject","DisplayName","Active","CanManageOrganizations","ExternalIssuer","ExternalSubject","AccessVersion","SessionEpoch")
              VALUES (m,t,m::text,display_name,true,false,issuer,external_subject,1,1);
            INSERT INTO public."OrganizationGrants" ("TenantId","MembershipId","OrganizationId","Role") VALUES (t,m,org,new_role);
            action:='Created';
          ELSE
            SELECT * INTO member FROM public."Memberships" WHERE "TenantId"=t AND "Id"=m FOR UPDATE;
            IF NOT FOUND THEN RAISE EXCEPTION 'Missing access member' USING ERRCODE='23503'; END IF;
            IF member."AccessVersion"<>expected OR member."AccessVersion"=2147483647 THEN RAISE EXCEPTION 'Access version changed' USING ERRCODE='40001'; END IF;
            IF operation='active' THEN
              IF member."Active"=enabled OR member."SessionEpoch"=2147483647 THEN RAISE EXCEPTION 'Invalid active transition' USING ERRCODE='22023'; END IF;
              UPDATE public."Memberships" SET "Active"=enabled,"AccessVersion"="AccessVersion"+1,"SessionEpoch"="SessionEpoch"+1 WHERE "TenantId"=t AND "Id"=m;
              action:='ActiveChanged';
            ELSIF operation='capability' THEN
              IF member."CanManageOrganizations"=enabled THEN RAISE EXCEPTION 'Unchanged capability' USING ERRCODE='22023'; END IF;
              UPDATE public."Memberships" SET "CanManageOrganizations"=enabled,"AccessVersion"="AccessVersion"+1 WHERE "TenantId"=t AND "Id"=m;
              action:='CapabilityChanged';
            ELSE
              SELECT "Role" INTO prior_role FROM public."OrganizationGrants" WHERE "TenantId"=t AND "MembershipId"=m AND "OrganizationId"=org FOR UPDATE;
              IF prior_role IS NOT DISTINCT FROM new_role THEN RAISE EXCEPTION 'Unchanged grant' USING ERRCODE='22023'; END IF;
              IF new_role IS NULL THEN DELETE FROM public."OrganizationGrants" WHERE "TenantId"=t AND "MembershipId"=m AND "OrganizationId"=org;
              ELSE INSERT INTO public."OrganizationGrants" ("TenantId","MembershipId","OrganizationId","Role") VALUES (t,m,org,new_role)
                ON CONFLICT ("TenantId","MembershipId","OrganizationId") DO UPDATE SET "Role"=EXCLUDED."Role";
              END IF;
              UPDATE public."Memberships" SET "AccessVersion"="AccessVersion"+1 WHERE "TenantId"=t AND "Id"=m;
              action:='GrantChanged';
            END IF;
          END IF;
          SELECT * INTO STRICT member FROM public."Memberships" WHERE "TenantId"=t AND "Id"=m;
          INSERT INTO public."AccessAudits" ("TenantId","MembershipId","Id","OrganizationId","Action","DatabaseLogin","Operator","AccessVersion","SessionEpoch","Active","CanManageOrganizations","Role","CreatedAt")
            VALUES (t,m,audit_id,org,action,session_user,attribution,member."AccessVersion",member."SessionEpoch",member."Active",member."CanManageOrganizations",new_role,clock_timestamp());
          INSERT INTO public."AccessControlEvents" ("TenantId","MembershipId","Id","AuditId","SchemaVersion","CreatedAt") VALUES (t,m,gen_random_uuid(),audit_id,1,clock_timestamp());
          RETURN member."AccessVersion";
        END $$;

        CREATE FUNCTION public.access_inspect_v1(t uuid,m uuid,after_org uuid,attribution text) RETURNS jsonb
          LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $$
        DECLARE member public."Memberships"%ROWTYPE; grants jsonb; next_org uuid; pending integer; oldest timestamptz; instances jsonb;
          deadline timestamptz:=clock_timestamp()+interval '3 seconds';
        BEGIN
          IF current_setting('transaction_isolation')<>'read committed' THEN RAISE EXCEPTION 'Unsupported access isolation' USING ERRCODE='25000'; END IF;
          IF t IS NULL OR m IS NULL OR attribution IS NULL OR NOT public.valid_asset_text_v1(attribution,160,true) OR attribution<>btrim(attribution) OR attribution~'[[:cntrl:]]' THEN
            RAISE EXCEPTION 'Invalid access inspection' USING ERRCODE='22023';
          END IF;
          LOOP
            EXIT WHEN pg_try_advisory_xact_lock_shared(public.access_key_v1(t,m));
            IF clock_timestamp()>=deadline THEN RAISE EXCEPTION 'Access inspection busy' USING ERRCODE='55P03'; END IF;
            PERFORM pg_sleep(0.025);
          END LOOP;
          SELECT * INTO member FROM public."Memberships" WHERE "TenantId"=t AND "Id"=m;
          IF NOT FOUND THEN RAISE EXCEPTION 'Missing access member' USING ERRCODE='23503'; END IF;
          SELECT COALESCE(jsonb_agg(jsonb_build_object('organizationId',g."OrganizationId",'role',g."Role") ORDER BY g."OrganizationId"),'[]'::jsonb) INTO grants
            FROM (SELECT "OrganizationId","Role" FROM public."OrganizationGrants" WHERE "TenantId"=t AND "MembershipId"=m
              AND (after_org IS NULL OR "OrganizationId">after_org) ORDER BY "OrganizationId" LIMIT 51) g;
          IF jsonb_array_length(grants)>50 THEN next_org:=(grants->49->>'organizationId')::uuid; grants:=grants-50; END IF;
          SELECT count(*),min(p."StartedAt"),COALESCE(jsonb_agg(DISTINCT p."InstanceId"),'[]'::jsonb) INTO pending,oldest,instances
            FROM (SELECT "StartedAt","InstanceId" FROM public."AccessResponses" WHERE "TenantId"=t AND "MembershipId"=m AND "CompletedAt" IS NULL LIMIT 1001) p;
          INSERT INTO public."AccessAudits" ("TenantId","MembershipId","Id","Action","DatabaseLogin","Operator","AccessVersion","SessionEpoch","Active","CanManageOrganizations","CreatedAt")
            VALUES (t,m,gen_random_uuid(),'Inspected',session_user,attribution,member."AccessVersion",member."SessionEpoch",member."Active",member."CanManageOrganizations",clock_timestamp());
          RETURN jsonb_build_object('tenantId',t,'membershipId',m,'accessVersion',member."AccessVersion",'sessionEpoch',member."SessionEpoch",'active',member."Active",
            'canManageOrganizations',member."CanManageOrganizations",'mappedIdentity',member."ExternalIssuer" IS NOT NULL,'grants',grants,'nextOrganizationId',next_org,
            'pendingResponseCount',LEAST(pending,1000),'pendingResponseCountCapped',pending>1000,'sampledOldestPendingAt',oldest,'sampledPendingInstances',instances);
        END $$;
        REVOKE ALL ON FUNCTION public.access_change_v1(uuid,uuid,integer,text,uuid,text,boolean,text,text,text,text),public.access_inspect_v1(uuid,uuid,uuid,text) FROM PUBLIC,saveit_app,saveit_search_worker;
        GRANT EXECUTE ON FUNCTION public.access_change_v1(uuid,uuid,integer,text,uuid,text,boolean,text,text,text,text),public.access_inspect_v1(uuid,uuid,uuid,text) TO saveit_access_operator;
        """;

    public const string Down = """
        DROP FUNCTION public.access_change_v1(uuid,uuid,integer,text,uuid,text,boolean,text,text,text,text),public.access_inspect_v1(uuid,uuid,uuid,text);
        DROP FUNCTION public.register_response_v1(uuid,uuid,text,integer,uuid,uuid),public.complete_response_v1(uuid,uuid,uuid,uuid,boolean);
        DROP FUNCTION public.access_key_v1(uuid,uuid);
        """;
}
