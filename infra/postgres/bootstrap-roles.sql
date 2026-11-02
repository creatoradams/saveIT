-- Execute as a separately authorized cluster administrator. No passwords or tenant data belong here.
-- Create an EMPTY database owned by saveit_migration_owner, and grant that role only to a deployment login.
-- Workload login/password or IAM provisioning is separate protected infrastructure configuration.
DO $body$
DECLARE role_name text;
BEGIN
  FOREACH role_name IN ARRAY ARRAY['saveit_migration_owner','saveit_app','saveit_search_worker','saveit_access_operator'] LOOP
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname=role_name) THEN
      EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOBYPASSRLS NOREPLICATION',role_name);
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname=role_name AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolbypassrls OR rolreplication)) THEN
      RAISE EXCEPTION 'Unsafe preexisting deployment/runtime role';
    END IF;
  END LOOP;
  IF pg_has_role('saveit_app','saveit_migration_owner','MEMBER') OR pg_has_role('saveit_search_worker','saveit_migration_owner','MEMBER') THEN
    RAISE EXCEPTION 'Runtime roles must not reach deployment ownership';
  END IF;
  IF (SELECT rolcanlogin FROM pg_roles WHERE rolname='saveit_access_operator')
    OR pg_has_role('saveit_app','saveit_access_operator','MEMBER') OR pg_has_role('saveit_search_worker','saveit_access_operator','MEMBER') THEN
    RAISE EXCEPTION 'Access operator must be a separate non-login role unreachable by workloads';
  END IF;
END $body$;
