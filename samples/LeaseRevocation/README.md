# LeaseRevocation

Warehouse DB admin agent. Reads against pre-granted tables run free.
INSERT / UPDATE / DELETE / DDL trigger a synchronous
`permission.request` scoped to the specific table and operation. A
mid-flight `lease.revoked` evicts the cache so the next call re-prompts.

## Before ARCP

Two failure modes: (1) the agent has a write-capable DB role and
operators audit Slack, hoping; (2) writes go through a separate
"approval" service that the agent doesn't actually understand.

## With ARCP

```csharp
StatementClass klass = Sql.Classify(sql);   // read / write / ddl
foreach (string table in klass.Tables)
{
    await RequestLeaseAsync(client,
        permission: $"db.{klass.Op}",
        table: table,
        operation: klass.Op,
        seconds: klass.Op == "read" ? ReadLeaseSeconds : WriteLeaseSeconds,
        reason: ...);
}
```

Granted leases are cached. Mid-statement `lease.revoked` drops the
cache entry so the next call re-prompts.

## ARCP primitives

- Permission challenge — RFC §15.4.
- Full lease lifecycle (request, grant, use, refresh, revoke) — §15.5.
- `PERMISSION_DENIED` / `LEASE_EXPIRED` / `LEASE_REVOKED` — §18.2.

## File tour

- `Program.cs` — opens session, bootstraps reads, runs two queries.
- `Sql.cs` — sqlglot-equivalent classifier (stubbed).
- `Stubs.cs` — elided client helpers.

## Variations

- Replace operator approval with a policy engine (Cedar, OPA).
- Promote read leases to row-level by encoding row-filter SQL into
  `resource` (`table:public.orders/region=us`).
- Stream every DDL into [Subscriptions](../Subscriptions) for change history.
