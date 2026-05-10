using System.Collections.Concurrent;

namespace ARCP.Envelope;

/// <summary>
/// Bidirectional registry mapping wire <c>type</c> discriminators (e.g.
/// <c>"session.open"</c>) to <see cref="MessageType" /> CLR types.
/// </summary>
/// <remarks>
/// Per-instance, not static. <see cref="CoreCatalog" /> registers every core
/// (§6.2) message type. Extension types are added via
/// <see cref="ARCP.Extensions.ExtensionRegistry" />; the
/// <c>EnvelopeJsonConverter</c> consults both.
/// </remarks>
public sealed class MessageTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _wireToClr = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, string> _clrToWire = new();

    /// <summary>
    /// Register a concrete <see cref="MessageType" /> subtype with its wire
    /// discriminator.
    /// </summary>
    /// <typeparam name="T">The concrete payload type.</typeparam>
    /// <param name="wireType">The canonical type string (e.g. <c>"session.open"</c>).</param>
    public void Register<T>(string wireType)
        where T : MessageType
    {
        ArgumentException.ThrowIfNullOrEmpty(wireType);
        if (_wireToClr.TryGetValue(wireType, out Type? existing) && existing != typeof(T))
        {
            throw new InvalidOperationException(
                $"MessageTypeRegistry: \"{wireType}\" already bound to {existing.Name}, refusing to rebind to {typeof(T).Name}.");
        }
        _wireToClr[wireType] = typeof(T);
        _clrToWire[typeof(T)] = wireType;
    }

    /// <summary>Resolve a CLR type for a wire discriminator.</summary>
    /// <param name="wireType">The wire string.</param>
    /// <returns>The CLR type, or <see langword="null" /> if not registered.</returns>
    public Type? Resolve(string wireType) =>
        string.IsNullOrEmpty(wireType) ? null : _wireToClr.GetValueOrDefault(wireType);

    /// <summary>Whether <paramref name="wireType" /> is registered.</summary>
    /// <param name="wireType">The wire string.</param>
    /// <returns><see langword="true" /> if registered.</returns>
    public bool Contains(string wireType) =>
        !string.IsNullOrEmpty(wireType) && _wireToClr.ContainsKey(wireType);

    /// <summary>The number of registered message types.</summary>
    public int Count => _wireToClr.Count;

    /// <summary>
    /// The canonical core catalog populated with every §6.2 core message type.
    /// </summary>
    /// <returns>A new registry pre-populated with core types.</returns>
    public static MessageTypeRegistry CoreCatalog()
    {
        MessageTypeRegistry r = new();

        // Session
        r.Register<Messages.Session.SessionOpen>("session.open");
        r.Register<Messages.Session.SessionChallenge>("session.challenge");
        r.Register<Messages.Session.SessionAuthenticate>("session.authenticate");
        r.Register<Messages.Session.SessionAccepted>("session.accepted");
        r.Register<Messages.Session.SessionUnauthenticated>("session.unauthenticated");
        r.Register<Messages.Session.SessionRejected>("session.rejected");
        r.Register<Messages.Session.SessionRefresh>("session.refresh");
        r.Register<Messages.Session.SessionEvicted>("session.evicted");
        r.Register<Messages.Session.SessionClose>("session.close");

        // Control
        r.Register<Messages.Control.Ping>("ping");
        r.Register<Messages.Control.Pong>("pong");
        r.Register<Messages.Control.Ack>("ack");
        r.Register<Messages.Control.Nack>("nack");
        r.Register<Messages.Control.Cancel>("cancel");
        r.Register<Messages.Control.CancelAccepted>("cancel.accepted");
        r.Register<Messages.Control.CancelRefused>("cancel.refused");
        r.Register<Messages.Control.Interrupt>("interrupt");
        r.Register<Messages.Control.Resume>("resume");
        r.Register<Messages.Control.Backpressure>("backpressure");
        r.Register<Messages.Control.CheckpointCreate>("checkpoint.create");
        r.Register<Messages.Control.CheckpointRestore>("checkpoint.restore");

        // Execution
        r.Register<Messages.Execution.ToolInvoke>("tool.invoke");
        r.Register<Messages.Execution.ToolResult>("tool.result");
        r.Register<Messages.Execution.ToolError>("tool.error");
        r.Register<Messages.Execution.JobAccepted>("job.accepted");
        r.Register<Messages.Execution.JobStarted>("job.started");
        r.Register<Messages.Execution.JobProgress>("job.progress");
        r.Register<Messages.Execution.JobHeartbeat>("job.heartbeat");
        r.Register<Messages.Execution.JobCheckpoint>("job.checkpoint");
        r.Register<Messages.Execution.JobCompleted>("job.completed");
        r.Register<Messages.Execution.JobFailed>("job.failed");
        r.Register<Messages.Execution.JobCancelled>("job.cancelled");
        r.Register<Messages.Execution.JobSchedule>("job.schedule");
        r.Register<Messages.Execution.WorkflowStart>("workflow.start");
        r.Register<Messages.Execution.WorkflowComplete>("workflow.complete");
        r.Register<Messages.Execution.AgentDelegate>("agent.delegate");
        r.Register<Messages.Execution.AgentHandoff>("agent.handoff");

        // Streaming
        r.Register<Messages.Streaming.StreamOpen>("stream.open");
        r.Register<Messages.Streaming.StreamChunk>("stream.chunk");
        r.Register<Messages.Streaming.StreamClose>("stream.close");
        r.Register<Messages.Streaming.StreamError>("stream.error");

        // Human
        r.Register<Messages.Human.HumanInputRequest>("human.input.request");
        r.Register<Messages.Human.HumanInputResponse>("human.input.response");
        r.Register<Messages.Human.HumanInputCancelled>("human.input.cancelled");
        r.Register<Messages.Human.HumanChoiceRequest>("human.choice.request");
        r.Register<Messages.Human.HumanChoiceResponse>("human.choice.response");

        // Permissions
        r.Register<Messages.Permissions.PermissionRequest>("permission.request");
        r.Register<Messages.Permissions.PermissionGrant>("permission.grant");
        r.Register<Messages.Permissions.PermissionDeny>("permission.deny");
        r.Register<Messages.Permissions.LeaseGranted>("lease.granted");
        r.Register<Messages.Permissions.LeaseRefresh>("lease.refresh");
        r.Register<Messages.Permissions.LeaseExtended>("lease.extended");
        r.Register<Messages.Permissions.LeaseRevoked>("lease.revoked");

        // Subscriptions
        r.Register<Messages.Subscriptions.Subscribe>("subscribe");
        r.Register<Messages.Subscriptions.SubscribeAccepted>("subscribe.accepted");
        r.Register<Messages.Subscriptions.SubscribeEvent>("subscribe.event");
        r.Register<Messages.Subscriptions.Unsubscribe>("unsubscribe");
        r.Register<Messages.Subscriptions.SubscribeClosed>("subscribe.closed");

        // Artifacts
        r.Register<Messages.Artifacts.ArtifactPut>("artifact.put");
        r.Register<Messages.Artifacts.ArtifactFetch>("artifact.fetch");
        r.Register<Messages.Artifacts.ArtifactRef>("artifact.ref");
        r.Register<Messages.Artifacts.ArtifactRelease>("artifact.release");

        // Telemetry
        r.Register<Messages.Telemetry.EventEmit>("event.emit");
        r.Register<Messages.Telemetry.LogMessage>("log");
        r.Register<Messages.Telemetry.Metric>("metric");
        r.Register<Messages.Telemetry.TraceSpan>("trace.span");

        return r;
    }
}
