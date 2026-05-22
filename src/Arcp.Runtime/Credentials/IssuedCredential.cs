// SPDX-License-Identifier: Apache-2.0
using Arcp.Core.Messages;

namespace Arcp.Runtime.Credentials;

internal sealed record IssuedCredential(ProvisionedCredential Wire, string RevokeId);
