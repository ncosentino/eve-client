namespace NexusLabs.Eve;

/// <summary>
/// Identifies which exclusive payload a turn carries.
/// </summary>
/// <remarks>
/// eve <c>0.31.0</c> rejects a turn body containing both <c>message</c> and
/// <c>inputResponses</c> with HTTP 400, so the payload is chosen by the calling operation
/// rather than inferred from whichever fields happen to be populated.
/// </remarks>
internal enum EveTurnPayloadKind
{
    Message,
    InputResponses,
}
