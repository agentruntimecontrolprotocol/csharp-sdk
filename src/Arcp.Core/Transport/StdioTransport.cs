// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Wire;

namespace Arcp.Core.Transport;

/// <summary>Stdio transport (spec §4.2). Newline-delimited UTF-8 JSON envelopes on a paired
/// <see cref="Stream"/> for input and output.</summary>
public sealed class StdioTransport : ITransport
{
    private readonly StreamReader _input;
    private readonly StreamWriter _output;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _closed;

    /// <summary>Initializes a new instance of the <see cref="StdioTransport"/> class.</summary>
    public StdioTransport(Stream input, Stream output)
    {
        _input = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        _output = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 4096, leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = false,
        };
    }

    /// <summary>From console.</summary>
    public static StdioTransport FromConsole() =>
        new(Console.OpenStandardInput(), Console.OpenStandardOutput());

    /// <summary>Send (asynchronous).</summary>
    public async ValueTask SendAsync(Envelope envelope, CancellationToken cancellationToken = default)
    {
        if (_closed) throw new InvalidOperationException("Transport is closed.");
        var json = ArcpJson.Serialize(envelope);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _output.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Receive (asynchronous).</summary>
    public async IAsyncEnumerable<Envelope> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!_closed)
        {
            string? line;
            try
            {
                line = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { yield break; }
            if (line is null) yield break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            Envelope env;
            try
            {
                env = ArcpJson.Deserialize(line);
            }
            catch (Exception ex) when (ex is Errors.ArcpException or System.Text.Json.JsonException)
            {
                continue;
            }
            yield return env;
        }
    }

    /// <summary>Close (asynchronous).</summary>
    public ValueTask CloseAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        _closed = true;
        return ValueTask.CompletedTask;
    }

    /// <summary>Dispose (asynchronous).</summary>
    public ValueTask DisposeAsync()
    {
        _closed = true;
        _input.Dispose();
        _output.Dispose();
        _sendLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
