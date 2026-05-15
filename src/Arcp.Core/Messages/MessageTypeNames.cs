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
    public const string SessionHello = "session.hello";
    public const string SessionWelcome = "session.welcome";
    public const string SessionBye = "session.bye";
    public const string SessionPing = "session.ping";
    public const string SessionPong = "session.pong";
    public const string SessionAck = "session.ack";
    public const string SessionListJobs = "session.list_jobs";
    public const string SessionJobs = "session.jobs";
    public const string SessionError = "session.error";
    public const string SessionResume = "session.resume";

    public const string JobSubmit = "job.submit";
    public const string JobAccepted = "job.accepted";
    public const string JobEvent = "job.event";
    public const string JobResult = "job.result";
    public const string JobError = "job.error";
    public const string JobCancel = "job.cancel";
    public const string JobSubscribe = "job.subscribe";
    public const string JobSubscribed = "job.subscribed";
    public const string JobUnsubscribe = "job.unsubscribe";

    public static readonly FrozenSet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        SessionHello, SessionWelcome, SessionBye, SessionPing, SessionPong, SessionAck,
        SessionListJobs, SessionJobs, SessionError, SessionResume,
        JobSubmit, JobAccepted, JobEvent, JobResult, JobError, JobCancel,
        JobSubscribe, JobSubscribed, JobUnsubscribe,
    }.ToFrozenSet();
}
