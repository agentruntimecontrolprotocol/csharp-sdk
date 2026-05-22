// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Arcp.Core.Messages;

namespace Arcp.Runtime.Credentials;

/// <summary>Helpers for removing credential secrets before non-owner views are serialized.</summary>
internal static class CredentialRedaction
{
    public static IReadOnlyList<ProvisionedCredential> EmptyForNonSubmitter(
        IReadOnlyList<ProvisionedCredential> credentials,
        bool isSubmitter) =>
        isSubmitter ? credentials : [];

    public static IReadOnlyList<ProvisionedCredential> StripValues(IReadOnlyList<ProvisionedCredential> credentials) =>
        credentials
            .Select(c => c with { Value = string.Empty })
            .ToArray();

    public static JobEventPayload RedactCredentialRotation(JobEventPayload payload)
    {
        if (!string.Equals(payload.Kind, EventKinds.Status, StringComparison.Ordinal))
            return payload;

        var body = payload.Body.Deserialize<StatusBody>();
        if (body is null || !string.Equals(body.Phase, StatusPhases.CredentialRotated, StringComparison.Ordinal))
            return payload;

        return payload with
        {
            Body = Arcp.Core.Wire.ArcpJson.ToJsonElement(body with { CredentialValue = null }),
        };
    }
}
