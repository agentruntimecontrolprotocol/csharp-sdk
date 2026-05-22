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
    public async Task<JobHandle> SubmitAsync(string agent, object? input = null, Lease? leaseRequest = null,
        LeaseConstraints? leaseConstraints = null, string? idempotencyKey = null,
        int? maxRuntimeSec = null, string? parentJobId = null, CancellationToken cancellationToken = default)
    {
        var handle = new JobHandle(this);
        _pendingSubmits.Enqueue(handle);
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

    public async Task<SessionJobsPayload> ListJobsAsync(JobListFilter? filter = null, int? limit = null,
        string? cursor = null, CancellationToken cancellationToken = default)
    {
        var id = "msg_" + Ulid.NewUlid();
        var tcs = new TaskCompletionSource<SessionJobsPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        _listJobsRequests[id] = tcs;
        await _transport.SendAsync(new Envelope
        {
            Id = id,
            Type = MessageTypeNames.SessionListJobs,
            SessionId = SessionId.Value,
            Payload = new SessionListJobsPayload { Filter = filter, Limit = limit, Cursor = cursor },
        }, cancellationToken).ConfigureAwait(false);
        return await tcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
