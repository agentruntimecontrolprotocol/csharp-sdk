---
title: Errors
sdk: csharp
spec_sections: ["§12"]
order: 12
kind: reference
---

ARCP defines 15 canonical error codes (spec §12). The C# SDK exposes them as `string` constants on `ErrorCode` and as sealed `ArcpException` subclasses.

| Code | C# subclass | Retryable |
| ---- | ----------- | --------- |
| `PERMISSION_DENIED`             | `PermissionDeniedException` | no |
| `LEASE_SUBSET_VIOLATION`        | `LeaseSubsetViolationException` | no |
| `JOB_NOT_FOUND`                 | `JobNotFoundException` | no |
| `DUPLICATE_KEY`                 | `DuplicateKeyException` | no |
| `AGENT_NOT_AVAILABLE`           | `AgentNotAvailableException` | **yes** |
| `AGENT_VERSION_NOT_AVAILABLE`   | `AgentVersionNotAvailableException` | no |
| `CANCELLED`                     | `CancelledException` | no |
| `TIMEOUT`                       | `TimeoutException` (`Arcp.Core.Errors`) | **yes** |
| `RESUME_WINDOW_EXPIRED`         | `ResumeWindowExpiredException` | no |
| `HEARTBEAT_LOST`                | `HeartbeatLostException` | **yes** |
| `LEASE_EXPIRED`                 | `LeaseExpiredException` | no |
| `BUDGET_EXHAUSTED`              | `BudgetExhaustedException` | no |
| `INVALID_REQUEST`               | `InvalidRequestException` | no |
| `UNAUTHENTICATED`               | `UnauthenticatedException` | no |
| `INTERNAL_ERROR`                | `InternalErrorException` | **yes** |

```csharp
try
{
    var handle = await client.SubmitAsync("code-refactor@9.9.9");
}
catch (AgentVersionNotAvailableException ex)
{
    Console.WriteLine($"unknown version: {ex.Message} retryable={ex.Retryable}");
}
```

Note: `Arcp.Core.Errors.TimeoutException` deliberately shadows `System.TimeoutException`. The fully qualified name disambiguates.

Vendor-namespaced codes (`arcpx.acme.QUOTA_EXCEEDED`) round-trip as strings — they are never lossy-converted through an enum.
