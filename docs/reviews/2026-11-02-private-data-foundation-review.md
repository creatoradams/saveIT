# Private network/database definition review

The user-requested efficiency/security agents reviewed the new cell-foundation source separately, read-only. They made no edits, executed no tests and made no AWS calls. No independent assurance is implied.

Remedies:

- Sort AZ input before assigning CIDR slots. Reordering the same AZ list previously could replace subnets; a reversed-input regression now preserves both application/database mappings.
- Use the PostgreSQL 18 major prefix with automatic minor servicing, expose the actual minor separately and reject exact-minor input. A fixed 18.4 request could otherwise become a downgrade after service maintenance. A mocked observed 18.5 regression checks the declared configuration, not actual RDS refresh/availability.
- Check each complete KMS deny's actions/NotAction, principal/resource and combined conditions. Narrow truth tables read those policy predicates for missing/direct/wrong-region/non-RDS/approved-RDS use and missing/false/true grant context, including exact administrator/root exceptions. This is not an IAM evaluator.

Local Terraform 1.13.5 formatting/init/validation and sixteen mocked plan cases pass with the pinned AWS 6.71.0 lock. Distinct plan-time network identities avoid vacuous wiring assertions. No apply or service authentication occurred. Expanded ignore checks keep state/plans/backend/local values untracked while the provider lock and sanitized example remain trackable. All 146 capability/connector labels remain unchanged. Linux verification is pending.

The root remains isolated partial infrastructure without compute, internet/VPC endpoints, ingress, runtime keys/secrets or operator connectivity. Terraform manages declared SG rules; actual out-of-band drift/auditing/reachability still need account controls. RDS-managed bootstrap credentials never become runtime authority. AWS warns blanket KMS denies can affect internal resource-owner/service-linked access; actual grant/backup/maintenance/failover/recovery behavior is a pre-deployment gate. Managed role/functions/forced-RLS/SSL/CA compatibility and restoration are unverified. No production/SLO/RPO/RTO or compliance claim follows.

Both agents rechecked the AZ/version remedies and expanded exact policy tests with no remaining actionable source findings. No AWS enforcement or independent assurance is implied.
