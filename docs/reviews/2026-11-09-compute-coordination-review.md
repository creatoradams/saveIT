# Core compute coordination review

Scope: ADR051 SQL migration/control journal, restricted operator runner, fixed process generation, API/worker admission, response completion, health modes, task metadata and real PostgreSQL regressions. Controller/provider fencing and whole-cell operation ownership are outside this completed primitive.

The independent security agent found null generation skipped API startup/readiness/early admission and worker startup checks after control initialization. All now consult the checked database function; null startup is compatible only with an absent control row. Actual unbound API/worker transition tests cover the remedy.

The agent also found compute credentials with an accidental registration/completion EXECUTE grant could pass the operator guard. Reachable direct/PUBLIC/NOINHERIT protocol function authority now fails both operator guards; cross-operator membership/functions fail symmetrically. Regressions exercise completion grants and dual-role credentials.

The agent closed these source findings and reviewed global-before-member/tenant lock order, fresh READ COMMITTED post-wait snapshots, immutable never-reused generations, atomic state/journal, seed-free initialization and ungated OnCompleted completion without another actionable issue. Tests include true non-superuser migration ownership with PUBLIC schema USAGE revoked, registration wait/commit races, lost COMMIT acknowledgement, held Kestrel completion, orphan aging, journal statement/deferred failure and selected-event rollback acknowledgement held while quiescence commits before retry. The agent reviewed but did not execute tests; exact executed results are in progress.

CoreQuiescent is cooperative core/worker admission coordination. It neither revokes compromised database credentials nor excludes migration/access operators, key-ring/background activity or downstream buffers. Whole-cell ownership and exact observed process/task termination remain required; privileged operator decisions or stop acknowledgements do not prove a fence. No pending row can be closed/pruned/expired by the compute operator. Managed recovery with lost protocol state still requires independent old-instance fencing.

The requested independent efficiency agent remains unavailable after its account usage limit. That review remains outstanding; source self-review and passing tests do not replace it. No AWS API/apply or credentials were used.
