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
    public const string Log = "log";
    public const string Thought = "thought";
    public const string ToolCall = "tool_call";
    public const string ToolResult = "tool_result";
    public const string Status = "status";
    public const string Metric = "metric";
    public const string ArtifactRef = "artifact_ref";
    public const string Delegate = "delegate";
    public const string Progress = "progress";
    public const string ResultChunk = "result_chunk";

    public static readonly FrozenSet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Log, Thought, ToolCall, ToolResult, Status, Metric, ArtifactRef, Delegate, Progress, ResultChunk,
    }.ToFrozenSet();
}
