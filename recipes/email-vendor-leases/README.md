# Recipe: email-vendor-leases

A triage agent iterates over three tool calls (inbox read, attachment scan,
send reply).  The caller's lease grants only the first two; the third is
blocked by `LeaseManager.AuthorizeOperation`, which raises
`PermissionDeniedException`.  The blocked call receives a structured
`ToolError` result instead of a thrown exception.  The permitted `inbox_read`
call also emits a vendor extension event that the client can observe alongside
standard protocol events.

## What this demonstrates

| Feature | Spec ref |
| ------- | -------- |
| `LeaseManager.AuthorizeOperation` — per-tool enforcement | §9.4 |
| `PermissionDeniedException` → `ctx.ToolResultAsync` error path | §9.4, §10 |
| `ctx.EmitEventAsync("x-vendor.*")` — vendor extension events | §11 |
| `ev.Kind.StartsWith("x-vendor.")` — client-side vendor event filtering | §11 |

## Lease shape

```json
{ "tool.call": ["inbox_read", "attachment_scan"] }
```

`send_reply` is absent from the lease, so `AuthorizeOperation` throws and the
agent returns a `ToolError` with `Code = "PERMISSION_DENIED"`.

## Vendor event

When `inbox_read` succeeds the agent emits:

```json
{ "type": "x-vendor.acme.email.parsed", "payload": { "folder": "INBOX", "count": 42, "unread": 7 } }
```

The client filters for `ev.Kind.StartsWith("x-vendor.")` to display these
separately from protocol events.

## Run

```sh
dotnet run --project recipes/email-vendor-leases
```

## Related

- [Leases guide](../../docs/guides/leases.md)
- [Vendor extensions guide](../../docs/guides/vendor-extensions.md)
- [`samples/LeaseViolation`](../../samples/LeaseViolation/) — lease enforcement basics
- [`samples/VendorExtensions`](../../samples/VendorExtensions/) — vendor event round-trip
