# Errors

ARCP defines 15 canonical error codes (spec §12). The C# SDK exposes them as
`string` constants on `ErrorCode` and as sealed `ArcpException` subclasses.

## Error taxonomy

| Code                           | C# subclass                       | Retryable |
| ------------------------------ | --------------------------------- | --------- |
| `PERMISSION_DENIED`            | `PermissionDeniedException`       | no        |
| `LEASE_SUBSET_VIOLATION`       | `LeaseSubsetViolationException`   | no        |
| `JOB_NOT_FOUND`                | `JobNotFoundException`            | no        |
| `DUPLICATE_KEY`                | `DuplicateKeyException`           | no        |
| `AGENT_NOT_AVAILABLE`          | `AgentNotAvailableException`      | **yes**   |
| `AGENT_VERSION_NOT_AVAILABLE`  | `AgentVersionNotAvailableException` | no      |
| `CANCELLED`                    | `CancelledException`              | no        |
| `TIMEOUT`                      | `TimeoutException` ¹              | **yes**   |
| `RESUME_WINDOW_EXPIRED`        | `ResumeWindowExpiredException`    | no        |
| `HEARTBEAT_LOST`               | `HeartbeatLostException`          | **yes**   |
| `LEASE_EXPIRED`                | `LeaseExpiredException`           | no        |
| `BUDGET_EXHAUSTED`             | `BudgetExhaustedException`        | no        |
| `INVALID_REQUEST`              | `InvalidRequestException`         | no        |
| `UNAUTHENTICATED`              | `UnauthenticatedException`        | no        |
| `INTERNAL_ERROR`               | `InternalErrorException`          | **yes**   |

¹ `Arcp.Core.Errors.TimeoutException` — use the fully qualified name to
  disambiguate from `System.TimeoutException`.

## Catching errors

```csharp
try
{
    var handle = await client.SubmitAsync("code-refactor@9.9.9");
    var result = await handle.Result;
}
catch (AgentVersionNotAvailableException ex)
{
    Console.WriteLine($"version not found: {ex.Message}, retryable={ex.Retryable}");
}
catch (ArcpException ex) when (ex.Retryable)
{
    // safe to back-off and retry
    await Task.Delay(TimeSpan.FromSeconds(5));
}
catch (ArcpException ex)
{
    Console.Error.WriteLine($"fatal ARCP error: {ex.Code} — {ex.Message}");
}
```

All `ArcpException` subclasses expose:

```csharp
string Code        // the canonical error code string
bool   Retryable   // whether the caller should retry
string Message     // human-readable description
```

## Retryable errors

The retryable flag maps directly to the spec §12 table. Errors marked
retryable are transient — a simple back-off retry will usually succeed:

- `AGENT_NOT_AVAILABLE` — runtime briefly has no spare capacity.
- `TIMEOUT` — hard wall-clock limit hit; re-submit with a fresh job.
- `HEARTBEAT_LOST` — transport drop; reconnect and resume.
- `INTERNAL_ERROR` — unexpected runtime fault; retry is safe.

Non-retryable errors indicate a structural problem (bad lease, wrong version,
exhausted budget). Retrying without changing the request would fail again.

## Vendor error codes

Vendor-namespaced codes (e.g. `arcpx.acme.QUOTA_EXCEEDED`) round-trip as
`string` — they are never lossy-converted through an enum. Catch them by
checking the `Code` property of `ArcpException`:

```csharp
catch (ArcpException ex) when (ex.Code == "arcpx.acme.QUOTA_EXCEEDED")
{
    // handle vendor-specific error
}
```

## Error flow from agents

Agents can produce errors by throwing:

```csharp
server.RegisterAgent("strict", async (ctx, ct) =>
{
    if (ctx.Lease.Get(LeaseNamespaces.FsRead).Count == 0)
        throw new PermissionDeniedException("fs.read required");

    // ...
    return result;
});
```

An unhandled exception that is not an `ArcpException` becomes
`INTERNAL_ERROR`. An `ArcpException` subclass preserves its `Code`.

## Related guides

- [Leases](./leases.md) — `PERMISSION_DENIED`, `LEASE_EXPIRED`.
- [Jobs](./jobs.md) — `BUDGET_EXHAUSTED`, `CANCELLED`, `TIMEOUT`.
- [Resume](./resume.md) — `RESUME_WINDOW_EXPIRED`, `HEARTBEAT_LOST`.
- [Auth](./auth.md) — `UNAUTHENTICATED`.
