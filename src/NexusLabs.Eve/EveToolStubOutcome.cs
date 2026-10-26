using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Declares a JSON response or a server-side thrown error for a matched tool call.
/// </summary>
public sealed class EveToolStubOutcome
{
    private EveToolStubOutcome(JsonElement json) => Json = json;

    /// <summary>
    /// Gets the immutable wire declaration, including arbitrary response JSON.
    /// </summary>
    public JsonElement Json { get; }

    /// <summary>
    /// Creates a successful outcome without executing a tool in the client.
    /// </summary>
    /// <param name="response">Any JSON value, including JSON null.</param>
    /// <returns>A response-only outcome.</returns>
    /// <exception cref="ArgumentException">The JSON value is undefined.</exception>
    public static EveToolStubOutcome Respond(JsonElement response)
    {
        if (response.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("A stub response must be a defined JSON value.", nameof(response));
        }

        return new EveToolStubOutcome(EveJsonElementFactory.CreateObject(writer =>
        {
            writer.WritePropertyName("response");
            response.WriteTo(writer);
        }));
    }

    /// <summary>
    /// Creates a server-side thrown error; an omitted name defaults to <c>Error</c>.
    /// </summary>
    /// <param name="message">The error message. An empty message is permitted.</param>
    /// <param name="name">An optional nonempty error name, not a JavaScript class.</param>
    /// <returns>A throw-only outcome.</returns>
    /// <exception cref="ArgumentNullException">The message is null.</exception>
    /// <exception cref="ArgumentException">The supplied name is empty.</exception>
    public static EveToolStubOutcome Throw(string message, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (name is { Length: 0 })
        {
            throw new ArgumentException("A stub error name cannot be empty.", nameof(name));
        }

        return new EveToolStubOutcome(EveJsonElementFactory.CreateObject(writer =>
        {
            writer.WritePropertyName("throw");
            writer.WriteStartObject();
            writer.WriteString("message", message);
            if (name is not null)
            {
                writer.WriteString("name", name);
            }

            writer.WriteEndObject();
        }));
    }
}
