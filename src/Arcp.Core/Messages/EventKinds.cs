// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcp.Core.Caps;
using Arcp.Core.Leases;

namespace Arcp.Core.Messages;

/// <summary>Reserved event kind values carried on <c>job.event.payload.kind</c> (spec §8.2).</summary>
public static class EventKinds
{
    /// <summary>Gets the log.</summary>
    public const string Log = "log";
    /// <summary>Gets the thought.</summary>
    public const string Thought = "thought";
    /// <summary>Gets the tool call.</summary>
    public const string ToolCall = "tool_call";
    /// <summary>Gets the tool result.</summary>
    public const string ToolResult = "tool_result";
    /// <summary>Gets the status.</summary>
    public const string Status = "status";
    /// <summary>Gets the metric.</summary>
    public const string Metric = "metric";
    /// <summary>Gets the artifact ref.</summary>
    public const string ArtifactRef = "artifact_ref";
    /// <summary>Gets the delegate.</summary>
    public const string Delegate = "delegate";
    /// <summary>Gets the progress.</summary>
    public const string Progress = "progress";
    /// <summary>Gets the result chunk.</summary>
    public const string ResultChunk = "result_chunk";

    /// <summary>Gets the all.</summary>
    public static readonly FrozenSet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Log, Thought, ToolCall, ToolResult, Status, Metric, ArtifactRef, Delegate, Progress, ResultChunk,
    }.ToFrozenSet();
}
