using System.Text.Json;
using ARCP.Envelope;

namespace ARCP.Messages.Human;

/// <summary>§12.1 ask a human for arbitrary structured input.</summary>
public sealed record HumanInputRequest : MessageType
{
    /// <summary>Free-form prompt text shown to the human.</summary>
    public required string Prompt { get; init; }

    /// <summary>JSON-Schema-shaped object describing the response shape.</summary>
    public required JsonElement ResponseSchema { get; init; }

    /// <summary>Optional default value used on expiry per §12.4.</summary>
    public JsonElement? Default { get; init; }

    /// <summary>RFC 3339 deadline.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Optional destination hint (e.g. <c>"ntfy:phone"</c>).</summary>
    public string? Destination { get; init; }

    /// <inheritdoc />
    public override string WireType => "human.input.request";
}

/// <summary>§12.1 response to <see cref="HumanInputRequest" />.</summary>
public sealed record HumanInputResponse(
    JsonElement Value,
    string RespondedBy,
    DateTimeOffset RespondedAt) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "human.input.response";
}

/// <summary>§12.4 cancellation/expiry of a human input request.</summary>
public sealed record HumanInputCancelled(
    string Code,
    string? Message = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "human.input.cancelled";
}

/// <summary>§12.2 single option in a choice request.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Label">Display label.</param>
/// <param name="Description">Optional longer description.</param>
public sealed record HumanChoiceOption(
    string Id,
    string Label,
    string? Description = null);

/// <summary>§12.2 multi-option picker.</summary>
public sealed record HumanChoiceRequest(
    string Prompt,
    IReadOnlyList<HumanChoiceOption> Options,
    DateTimeOffset ExpiresAt,
    string? Default = null) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "human.choice.request";
}

/// <summary>§12.2 response to <see cref="HumanChoiceRequest" />.</summary>
public sealed record HumanChoiceResponse(
    string ChoiceId,
    string RespondedBy,
    DateTimeOffset RespondedAt) : MessageType
{
    /// <inheritdoc />
    public override string WireType => "human.choice.response";
}
