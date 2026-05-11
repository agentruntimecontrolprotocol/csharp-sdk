// Three sink stubs. Real versions ship to stdout, SQLite (arcp.store.eventlog
// schema), and an OTLP endpoint.
using Env = ARCP.Envelope.Envelope;

namespace ARCP.Samples.Subscriptions.Sinks;

public sealed class StdoutSink
{
    public Task HandleAsync(Env envelope) => throw new NotImplementedException();
}

public sealed class OtlpSink
{
    public OtlpSink(string endpoint) => Endpoint = endpoint;

    public string Endpoint { get; }

    public Task HandleAsync(Env envelope) => throw new NotImplementedException();
}

public sealed class SqliteSink : IAsyncDisposable
{
    public SqliteSink(string path) => Path = path;

    public string Path { get; }

    public Task HandleAsync(Env envelope) => throw new NotImplementedException();

    public ValueTask DisposeAsync() => throw new NotImplementedException();
}
