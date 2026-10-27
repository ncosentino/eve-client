namespace NexusLabs.Eve;

/// <summary>
/// Answers a version-27 human interaction using its stable interaction identity.
/// </summary>
public sealed record EveInteractionAnswer
{
    /// <summary>Initializes an option, free-text, or combined answer.</summary>
    /// <param name="interactionId">The identifier from interaction.opened, not a response or delivery ID.</param>
    /// <param name="optionId">The selected option, when applicable.</param>
    /// <param name="text">The free-text answer, when applicable.</param>
    public EveInteractionAnswer(string interactionId, string? optionId = null, string? text = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interactionId);
        InteractionId = interactionId;
        OptionId = optionId;
        Text = text;
    }

    /// <summary>Gets the interaction being answered.</summary>
    public string InteractionId { get; }

    /// <summary>Gets the selected option.</summary>
    public string? OptionId { get; }

    /// <summary>Gets the free-text answer.</summary>
    public string? Text { get; }
}
