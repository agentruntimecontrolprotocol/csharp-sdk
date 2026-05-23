# Authentication

ARCP uses Bearer authentication (spec §6.1). The client sends a token in
`session.hello`; the runtime verifies it before sending `session.welcome`.

## Static bearer (built-in)

`StaticBearerVerifier` maps token strings to `AuthPrincipal` values. It is
the simplest option for single-token deployments and testing:

```csharp
var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "my-runtime", Version = "1.1.0" },
    Auth    = new StaticBearerVerifier(
        ("tok-alice", new AuthPrincipal("alice@example.com")),
        ("tok-bob",   new AuthPrincipal("bob@example.com"))
    ),
});
```

`StaticBearerVerifier.VerifyAsync` returns `null` for any token not in
its table. The runtime turns a `null` principal into a `session.error`
with code `UNAUTHENTICATED` and closes the transport.

## Custom verifier

Implement `IBearerVerifier` for database lookups, JWT validation, or any
other auth mechanism. Return `null` to reject; return an `AuthPrincipal`
to accept:

```csharp
public sealed class JwtBearerVerifier : IBearerVerifier
{
    private readonly TokenValidationParameters _params;

    public JwtBearerVerifier(TokenValidationParameters p) => _params = p;

    public ValueTask<AuthPrincipal?> VerifyAsync(
        string? token,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(token))
            return ValueTask.FromResult<AuthPrincipal?>(null);

        var handler = new JwtSecurityTokenHandler();
        var claims  = handler.ValidateToken(token, _params, out _);
        var email   = claims.FindFirstValue(ClaimTypes.Email);
        if (email is null)
            return ValueTask.FromResult<AuthPrincipal?>(null);

        return ValueTask.FromResult<AuthPrincipal?>(new AuthPrincipal(email));
    }
}
```

Register it:

```csharp
var server = new ArcpServer(new ArcpServerOptions
{
    Runtime = new RuntimeInfo { Name = "my-runtime", Version = "1.1.0" },
    Auth    = new JwtBearerVerifier(jwtParams),
});
```

## Client-side token

Pass the bearer token in `ArcpClientOptions.Token`:

```csharp
await using var client = await ArcpClient.ConnectAsync(transport, new ArcpClientOptions
{
    Client = new ClientInfo { Name = "my-app", Version = "1.0.0" },
    Token  = Environment.GetEnvironmentVariable("ARCP_TOKEN"),
});
```

If the token is absent or invalid, the runtime sends `session.error`
(`UNAUTHENTICATED`) and closes the transport. `ConnectAsync` surfaces this as
`UnauthenticatedException`.

## No-auth deployments

For internal same-process testing, pass `AllowAnyBearerVerifier`, which
accepts any non-empty token and reflects it as the principal subject:

```csharp
new ArcpServerOptions
{
    Auth = new AllowAnyBearerVerifier(),
};
```

Never use `AllowAnyBearerVerifier` in network-accessible deployments.

## Principal and authorization

The verified `AuthPrincipal` is held on the session, not on
`JobContext` — agents cannot read it directly. The runtime uses it to
enforce `IJobAuthorizationPolicy` (default: `SamePrincipalPolicy`,
which only lets a session observe its own jobs). To customize who can
observe or cancel jobs, set `ArcpServerOptions.AuthorizationPolicy` to
a custom implementation.
