-- Synthetic fixture only: compare metadata counts without exporting row content to logs.
SELECT format('SELECT %L || '':'' || count(*) FROM public.%I', c.relname, c.relname)
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE n.nspname='public' AND c.relkind='r'
  AND (c.relrowsecurity OR c.relname='__EFMigrationsHistory')
ORDER BY c.relname
\gexec
