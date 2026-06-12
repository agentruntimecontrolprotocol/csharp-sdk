# Arcp.Core

`Arcp.Core` is the lowest-level project in the SDK. It defines the wire
primitives that all other packages build on. You rarely reference it directly —
the `Arcp` meta-package re-exports everything — but it is useful in shared
libraries that only need types, not a full client or runtime.

```sh
dotnet add package Arcp.Core
```

## Key types

### Envelope

The JSON container that wraps every ARCP message on the wire (spec §5):

```csharp
public sealed record Envelope
{
    public string  Arcp       { get; init; }  // wire version, e.g. "1.1"
    public string  Id         { get; init; }  // random message id
    public string  Type       { get; init; }  // e.g. "job.submit"
    public string? SessionId  { get; init; }
    public string? JobId      { get; init; }
    public string? TraceId    { get; init; }
    public long?   EventSeq   { get; init; }
    public object? Payload    { get; init; }

    // Unknown top-level fields round-trip here (§15):
    public IDictionary<string, JsonElement>? Extensions { get; init; }
}
```

### Lease

Capability grant attached to a job submission (spec §9):

```csharp
var lease = new Lease(new Dictionary<string, IReadOnlyList<string>>
{
    [LeaseNamespaces.FsRead]    = new[] { "/workspace/**" },
    [LeaseNamespaces.FsWrite]   = new[] { "/workspace/src/**" },
    [LeaseNamespaces.NetFetch]  = new[] { "https://api.example.com/**" },
    [LeaseNamespaces.ModelUse]  = new[] { "tier-fast/*" },
    [LeaseNamespaces.CostBudget]= new[] { "USD:10.00" },
});
```

### LeaseNamespaces

String constants for the six reserved namespaces (spec §9.2):

| Constant               | Value             |
| ---------------------- | ----------------- |
| `LeaseNamespaces.FsRead`       | `"fs.read"`       |
| `LeaseNamespaces.FsWrite`      | `"fs.write"`      |
| `LeaseNamespaces.NetFetch`     | `"net.fetch"`     |
| `LeaseNamespaces.ToolCall`     | `"tool.call"`     |
| `LeaseNamespaces.AgentDelegate`| `"agent.delegate"`|
| `LeaseNamespaces.ModelUse`     | `"model.use"`     |
| `LeaseNamespaces.CostBudget`   | `"cost.budget"`   |

### LeaseConstraints

Optional time and cost caps attached at submission (spec §9.5):

```csharp
var constraints = new LeaseConstraints
{
    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
};
```

### Error codes

`ErrorCode` exposes the 15 canonical code strings (spec §12) as `const string`
fields. `ArcpException` and its subclasses mirror the table — see
[Errors](../guides/errors.md).

### ITransport

The one-method interface all transport implementations satisfy:

```csharp
public interface ITransport : IAsyncDisposable
{
    IAsyncEnumerable<Envelope> ReceiveAsync(CancellationToken ct);
    ValueTask SendAsync(Envelope envelope, CancellationToken ct);
}
```

## Related

- [Arcp](./Arcp.md) — umbrella package.
- [Transports](../transports.md) — `ITransport` implementations.
- [Leases guide](../guides/leases.md) — full lease API.
- [Errors guide](../guides/errors.md) — exception hierarchy.
