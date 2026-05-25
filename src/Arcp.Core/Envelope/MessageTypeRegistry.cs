// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Arcp.Core.Messages;

namespace Arcp.Core.Wire;

/// <summary>Maps wire message <c>type</c> strings to .NET payload types. Used by the envelope JSON
/// converter to dispatch deserialization (spec §5.1, §6, §7).</summary>
public sealed class MessageTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _map = new(StringComparer.Ordinal);

    /// <summary>Gets the default.</summary>
    public static MessageTypeRegistry Default { get; } = CreateCoreCatalog();

    /// <summary>Gets the entries.</summary>
    public IReadOnlyDictionary<string, Type> Entries => _map;

    /// <summary>Register.</summary>
    public MessageTypeRegistry Register(string typeName, Type payloadType)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(payloadType);
        _map[typeName] = payloadType;
        return this;
    }

    /// <summary>Try get.</summary>
    public bool TryGet(string typeName, out Type? payloadType)
    {
        var ok = _map.TryGetValue(typeName, out var t);
        payloadType = t;
        return ok;
    }

    /// <summary>Create core catalog.</summary>
    public static MessageTypeRegistry CreateCoreCatalog()
    {
        var r = new MessageTypeRegistry();
        r.Register(MessageTypeNames.SessionHello, typeof(SessionHelloPayload));
        r.Register(MessageTypeNames.SessionWelcome, typeof(SessionWelcomePayload));
        r.Register(MessageTypeNames.SessionBye, typeof(SessionByePayload));
        r.Register(MessageTypeNames.SessionPing, typeof(SessionPingPayload));
        r.Register(MessageTypeNames.SessionPong, typeof(SessionPongPayload));
        r.Register(MessageTypeNames.SessionAck, typeof(SessionAckPayload));
        r.Register(MessageTypeNames.SessionListJobs, typeof(SessionListJobsPayload));
        r.Register(MessageTypeNames.SessionJobs, typeof(SessionJobsPayload));
        r.Register(MessageTypeNames.SessionError, typeof(SessionErrorPayload));
        r.Register(MessageTypeNames.SessionResume, typeof(SessionResumePayload));
        r.Register(MessageTypeNames.JobSubmit, typeof(JobSubmitPayload));
        r.Register(MessageTypeNames.JobAccepted, typeof(JobAcceptedPayload));
        r.Register(MessageTypeNames.JobEvent, typeof(JobEventPayload));
        r.Register(MessageTypeNames.JobResult, typeof(JobResultPayload));
        r.Register(MessageTypeNames.JobError, typeof(JobErrorPayload));
        r.Register(MessageTypeNames.JobCancel, typeof(JobCancelPayload));
        r.Register(MessageTypeNames.JobSubscribe, typeof(JobSubscribePayload));
        r.Register(MessageTypeNames.JobSubscribed, typeof(JobSubscribedPayload));
        r.Register(MessageTypeNames.JobUnsubscribe, typeof(JobUnsubscribePayload));
        return r;
    }
}
