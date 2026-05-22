---
title: Provisioned credentials
sdk: csharp
spec_sections: ["§9.7", "§9.8", "§14"]
order: 19
kind: guide
---

Provisioned credentials are short-lived bearer tokens issued by the runtime after a job's lease is finalized. They let an upstream model gateway, search service, or other cost-bearing backend enforce `cost.budget`, `model.use`, and `lease_constraints.expires_at` outside the agent process.

## Configure a provisioner

Core defines only the vendor-neutral contract. Vendor adapters belong in host code or plug-in packages.

```csharp
var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "runtime", Version = "1.0.0" },
    CredentialProvisioner = new MyCredentialProvisioner(),
    CredentialStore = new MyDurableCredentialStore(),
});
```

`ICredentialProvisioner.IssueAsync` receives the finalized `Lease`, `LeaseConstraints`, and `CredentialIssueContext`. It returns `ProvisionedCredential` values whose wire shape is `{ id, scheme, value, endpoint, profile?, constraints? }`.

## Lifecycle

1. `JobManager.SubmitAsync` authorizes the lease and creates the job.
2. `CredentialManager.IssueForJobAsync` calls the provisioner and persists outstanding credential ids in `ICredentialStore`.
3. `job.accepted.payload.credentials` carries the issued credentials to the submitter.
4. `JobContext.Credentials` exposes a safe view to agent code with `value` stripped.
5. Terminal job states trigger best-effort revocation with retry; successful revocation removes ids from the store.

`InMemoryCredentialStore` is useful for tests and samples. Production hosts should provide a durable store so restart recovery can revoke orphaned credentials.

## Confidentiality

Credential `value` is treated as a secret. The SDK does not place values in logs, `session.list_jobs`, or non-submitter `job.subscribed` acknowledgements. Rotation events sent to non-submitter subscribers have `credential_value` removed before fan-out.

## Rotation

Agents can rotate a credential during a job:

```csharp
await ctx.RotateCredentialAsync(current.Id, new ProvisionedCredential
{
    Id = current.Id,
    Value = "new-secret",
    Endpoint = current.Endpoint,
    Constraints = current.Constraints,
}, ct);
```

The runtime revokes the previous id, replaces the stored credential, and emits a `status` event with `phase = "credential_rotated"` to the submitter.

See [`samples/ProvisionedCredential/`](../samples/ProvisionedCredential/).
