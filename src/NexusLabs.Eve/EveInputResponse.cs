namespace NexusLabs.Eve;

/// <summary>
/// Resolves one human-input request emitted by an eve session.
/// </summary>
public sealed record EveInputResponse
{
    /// <summary>
    /// Initializes a response for the specified request.
    /// </summary>
    /// <param name="requestId">The stable request identifier.</param>
    /// <param name="optionId">The selected option identifier, when applicable.</param>
    /// <param name="text">A free-form response, when applicable.</param>
    public EveInputResponse(string requestId, string? optionId = null, string? text = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        RequestId = requestId;
        OptionId = optionId;
        Text = text;
    }

    /// <summary>
    /// Gets the stable identifier of the request being answered.
    /// </summary>
    public string RequestId { get; }

    /// <summary>
    /// Gets the selected option identifier.
    /// </summary>
    public string? OptionId { get; }

    /// <summary>
    /// Gets the free-form answer.
    /// </summary>
    public string? Text { get; }
}
