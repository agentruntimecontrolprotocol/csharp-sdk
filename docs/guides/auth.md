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

`StaticBearerVerifier` rejects any token not in its table and throws
`UnauthenticatedException`.

## Custom verifier

Implement `IBearerVerifier` for database lookups, JWT validation, or any
other auth mechanism:

```csharp
public sealed class JwtBearerVerifier : IBearerVerifier
{
    private readonly TokenValidationParameters _params;

    public JwtBearerVerifier(TokenValidationParameters p) => _params = p;

    public ValueTask<AuthPrincipal> VerifyAsync(
        string token,
        CancellationToken ct = default)
    {
        var handler = new JwtSecurityTokenHandler();
        var claims  = handler.ValidateToken(token, _params, out _);
        var email   = claims.FindFirstValue(ClaimTypes.Email)
                      ?? throw new UnauthenticatedException("missing email claim");
        return ValueTask.FromResult(new AuthPrincipal(email));
    }
}
```

Register it:

```csharp
var server = new ArcpServer(new ArcpServerOptions
{
    Auth = new JwtBearerVerifier(jwtParams),
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

For internal same-process testing, pass `NullBearerVerifier.Instance`:

```csharp
new ArcpServerOptions
{
    Auth = NullBearerVerifier.Instance,   // accepts any token (or no token)
};
```

Never use `NullBearerVerifier` in network-accessible deployments.

## Principal in agent context

The verified principal is available in every agent call:

```csharp
server.RegisterAgent("echo", (ctx, ct) =>
{
    Console.WriteLine($"called by {ctx.Principal.Id}");
    return Task.FromResult<object?>(ctx.Input);
});
```

`ctx.Principal.Id` is the string passed to `AuthPrincipal(id)` by the
verifier.
