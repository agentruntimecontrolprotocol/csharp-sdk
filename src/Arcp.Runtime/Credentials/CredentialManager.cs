// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Arcp.Core.Errors;
using Arcp.Core.Ids;
using Arcp.Core.Messages;
using Microsoft.Extensions.Logging;

namespace Arcp.Runtime.Credentials;

/// <summary>Coordinates credential issuance, rotation, redaction, and terminal revocation.</summary>
public sealed class CredentialManager
{
    private const int MaxRevokeAttempts = 3;

    private readonly ICredentialProvisioner _provisioner;
    private readonly ICredentialStore _store;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="CredentialManager"/> class.</summary>
    public CredentialManager(
        ICredentialProvisioner provisioner,
        ICredentialStore store,
        ILogger logger,
        TimeProvider time)
    {
        _provisioner = provisioner;
        _store = store;
        _logger = logger;
        _time = time;
    }

    /// <summary>Issue for job (asynchronous).</summary>
    public async ValueTask<IReadOnlyList<ProvisionedCredential>> IssueForJobAsync(Job job, CancellationToken cancellationToken)
    {
        try
        {
            var context = new CredentialIssueContext(
                job.JobId,
                job.SessionId,
                job.SubmitterPrincipal,
                job.ParentJobId,
                job.BudgetLedger.IsActive ? job.BudgetLedger.Remaining : null);
            var credentials = await _provisioner
                .IssueAsync(job.Lease, job.LeaseConstraints, context, cancellationToken)
                .ConfigureAwait(false);
            var issued = credentials
                .Select(c => new IssuedCredential(c, c.Id))
                .ToArray();

            job.SetCredentials(issued);
            await _store
                .AddAsync(job.JobId, issued.Select(c => c.RevokeId).ToArray(), cancellationToken)
                .ConfigureAwait(false);
            return credentials;
        }
        catch (BudgetExhaustedException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.PaymentRequired)
        {
            throw new BudgetExhaustedException("Provisioned credential budget exhausted.", ex.Message);
        }
        catch (Exception ex)
        {
            throw new InternalErrorException("Credential provisioning failed.", ex.Message);
        }
    }

    /// <summary>Rotate (asynchronous).</summary>
    public async ValueTask RotateAsync(
        Job job,
        string credentialId,
        ProvisionedCredential next,
        CancellationToken cancellationToken)
    {
        var old = job.ReplaceCredential(credentialId, new IssuedCredential(next, next.Id));
        if (old is not null)
        {
            await TryRevokeAsync(job.JobId, old.RevokeId, cancellationToken).ConfigureAwait(false);
        }

        await _store.AddAsync(job.JobId, [next.Id], cancellationToken).ConfigureAwait(false);
        await job.EmitEventAsync(EventKinds.Status, new StatusBody
        {
            Phase = StatusPhases.CredentialRotated,
            CredentialId = next.Id,
            CredentialValue = next.Value,
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Revoke all for job (asynchronous).</summary>
    public async Task RevokeAllForJobAsync(JobId jobId, CancellationToken cancellationToken)
    {
        var ids = await _store.ListAsync(jobId, cancellationToken).ConfigureAwait(false);
        foreach (var id in ids)
        {
            await TryRevokeAsync(jobId, id, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Redact for.</summary>
    public IReadOnlyList<ProvisionedCredential> RedactFor(
        IReadOnlyList<ProvisionedCredential> credentials,
        bool isSubmitter) =>
        CredentialRedaction.EmptyForNonSubmitter(credentials, isSubmitter);

    private async ValueTask TryRevokeAsync(JobId jobId, string credentialId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxRevokeAttempts; attempt++)
        {
            try
            {
                await _provisioner.RevokeAsync(credentialId, cancellationToken).ConfigureAwait(false);
                await _store.RemoveAsync(jobId, credentialId, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxRevokeAttempts)
            {
                _logger.LogDebug(ex, "Credential revoke attempt {Attempt} failed", attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), _time, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Credential revocation failed after retries for job {JobId}", jobId);
            }
        }
    }
}
