// SPDX-License-Identifier: Apache-2.0
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Ids;
using Arcp.Core.Leases;
using Arcp.Core.Messages;
using Arcp.Core.Wire;

namespace Arcp.Client;

public sealed partial class ArcpClient
{
    /// <summary>Submit (asynchronous).</summary>
    public async Task<JobHandle> SubmitAsync(string agent, object? input = null, Lease? leaseRequest = null,
        LeaseConstraints? leaseConstraints = null, string? idempotencyKey = null,
        int? maxRuntimeSec = null, string? parentJobId = null, CancellationToken cancellationToken = default)
    {
        var handle = new JobHandle(this);
        _pendingSubmits.Enqueue(handle);
        try
        {
            await _transport.SendAsync(new Envelope
            {
                Type = MessageTypeNames.JobSubmit,
                SessionId = SessionId.Value,
                Payload = new JobSubmitPayload
                {
                    Agent = agent,
                    Input = input is null ? null : ArcpJson.ToJsonElement(input),
                    LeaseRequest = leaseRequest,
                    LeaseConstraints = leaseConstraints,
                    IdempotencyKey = idempotencyKey,
                    MaxRuntimeSec = maxRuntimeSec,
                    ParentJobId = parentJobId,
                },
            }, cancellationToken).ConfigureAwait(false);
            await handle.Accepted.WaitAsync(cancellationToken).ConfigureAwait(false);
            return handle;
        }
        catch
        {
            // The pending queue is FIFO-correlated with job.accepted responses. If we never
            // got an acceptance, evict our handle so the NEXT successful submit isn't bound
            // to this stale slot. If the acceptance already dequeued us, the walk is a no-op.
            RemovePendingSubmit(handle);
            throw;
        }
    }

    /// <summary>Evict a handle from the pending-submits queue. Walks the queue and re-enqueues
    /// every other handle in order so FIFO correlation with <c>job.accepted</c> is preserved.</summary>
    private void RemovePendingSubmit(JobHandle handle)
    {
        var keep = new System.Collections.Generic.List<JobHandle>();
        while (_pendingSubmits.TryDequeue(out var h))
        {
            if (!ReferenceEquals(h, handle)) keep.Add(h);
        }
        foreach (var h in keep) _pendingSubmits.Enqueue(h);
    }

    /// <summary>Cancel job (asynchronous).</summary>
    public async Task CancelJobAsync(JobId jobId, string? reason = null, CancellationToken cancellationToken = default)
    {
        await _transport.SendAsync(new Envelope
        {
            Type = MessageTypeNames.JobCancel,
            SessionId = SessionId.Value,
            JobId = jobId.Value,
            Payload = new JobCancelPayload { JobId = jobId.Value, Reason = reason },
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>List jobs (asynchronous).</summary>
    public async Task<SessionJobsPayload> ListJobsAsync(JobListFilter? filter = null, int? limit = null,
        string? cursor = null, CancellationToken cancellationToken = default)
    {
        var id = "msg_" + Ulid.NewUlid();
        var tcs = new TaskCompletionSource<SessionJobsPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        _listJobsRequests[id] = tcs;
        try
        {
            await _transport.SendAsync(new Envelope
            {
                Id = id,
                Type = MessageTypeNames.SessionListJobs,
                SessionId = SessionId.Value,
                Payload = new SessionListJobsPayload { Filter = filter, Limit = limit, Cursor = cursor },
            }, cancellationToken).ConfigureAwait(false);
            return await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _listJobsRequests.TryRemove(id, out _);
            throw;
        }
    }
}
