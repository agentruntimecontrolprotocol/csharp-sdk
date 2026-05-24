// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Canonical wire type names for ARCP v1.1 messages.</summary>
public static class MessageTypeNames
{
    /// <summary>Gets the session hello.</summary>
    public const string SessionHello = "session.hello";
    /// <summary>Gets the session welcome.</summary>
    public const string SessionWelcome = "session.welcome";
    /// <summary>Gets the session bye.</summary>
    public const string SessionBye = "session.bye";
    /// <summary>Gets the session ping.</summary>
    public const string SessionPing = "session.ping";
    /// <summary>Gets the session pong.</summary>
    public const string SessionPong = "session.pong";
    /// <summary>Gets the session ack.</summary>
    public const string SessionAck = "session.ack";
    /// <summary>Gets the session list jobs.</summary>
    public const string SessionListJobs = "session.list_jobs";
    /// <summary>Gets the session jobs.</summary>
    public const string SessionJobs = "session.jobs";
    /// <summary>Gets the session error.</summary>
    public const string SessionError = "session.error";
    /// <summary>Gets the session resume.</summary>
    public const string SessionResume = "session.resume";

    /// <summary>Gets the job submit.</summary>
    public const string JobSubmit = "job.submit";
    /// <summary>Gets the job accepted.</summary>
    public const string JobAccepted = "job.accepted";
    /// <summary>Gets the job event.</summary>
    public const string JobEvent = "job.event";
    /// <summary>Gets the job result.</summary>
    public const string JobResult = "job.result";
    /// <summary>Gets the job error.</summary>
    public const string JobError = "job.error";
    /// <summary>Gets the job cancel.</summary>
    public const string JobCancel = "job.cancel";
    /// <summary>Gets the job subscribe.</summary>
    public const string JobSubscribe = "job.subscribe";
    /// <summary>Gets the job subscribed.</summary>
    public const string JobSubscribed = "job.subscribed";
    /// <summary>Gets the job unsubscribe.</summary>
    public const string JobUnsubscribe = "job.unsubscribe";

    /// <summary>Gets the all.</summary>
    public static readonly FrozenSet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        SessionHello, SessionWelcome, SessionBye, SessionPing, SessionPong, SessionAck,
        SessionListJobs, SessionJobs, SessionError, SessionResume,
        JobSubmit, JobAccepted, JobEvent, JobResult, JobError, JobCancel,
        JobSubscribe, JobSubscribed, JobUnsubscribe,
    }.ToFrozenSet();
}
