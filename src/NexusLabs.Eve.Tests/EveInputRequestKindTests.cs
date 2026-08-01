using System.Net;
using System.Text;

namespace NexusLabs.Eve.Tests;

public sealed class EveInputRequestKindTests
{
    [Test]
    [Arguments("question", EveInputRequestKind.Question)]
    [Arguments("tool-approval", EveInputRequestKind.ToolApproval)]
    [Arguments("session-limit", EveInputRequestKind.SessionLimit)]
    public async Task InputRequest_ProjectsFrameworkOwnedKind(
        string wireKind,
        EveInputRequestKind expectedKind,
        CancellationToken cancellationToken)
    {
        EveTurnOutcome outcome = await CollectOutcomeAsync(
            InputRequestedEvent($",\"kind\":\"{wireKind}\""),
            cancellationToken);

        await Assert.That(outcome.InputRequests.Count).IsEqualTo(1);
        await Assert.That(outcome.InputRequests[0].Kind).IsEqualTo(expectedKind);
        await Assert.That(outcome.InputRequests[0].RawKind).IsEqualTo(wireKind);
    }

    [Test]
    public async Task SessionLimitRequest_IsNotClassifiedAsToolApprovalByItsShape(
        CancellationToken cancellationToken)
    {
        EveTurnOutcome outcome = await CollectOutcomeAsync(
            """{"type":"input.requested","data":{"requests":[{"requestId":"limit_1","prompt":"The session reached its step limit.","kind":"session-limit","display":"confirmation","options":[{"id":"continue","label":"Continue"},{"id":"stop","label":"Stop"}],"action":{"kind":"tool-call","toolName":"bash"}}],"sequence":1,"stepIndex":0,"turnId":"turn_1"}}""",
            cancellationToken);

        await Assert.That(outcome.InputRequests.Count).IsEqualTo(1);
        EveInputRequest request = outcome.InputRequests[0];
        await Assert.That(request.Kind).IsEqualTo(EveInputRequestKind.SessionLimit);
        await Assert.That(request.Display)
            .IsEqualTo("confirmation")
            .Because("A confirmation display hint no longer implies a tool approval.");
        await Assert.That(request.Options.Count).IsEqualTo(2);
        await Assert.That(request.Options[0].Id).IsEqualTo("continue");
        await Assert.That(request.Options[1].Id).IsEqualTo("stop");
        await Assert.That(request.Action.GetProperty("toolName").GetString())
            .IsEqualTo("bash")
            .Because("An accompanying tool name no longer implies a tool approval.");
    }

    [Test]
    public async Task QuestionRequest_IsNotClassifiedByItsTwoOptionShape(
        CancellationToken cancellationToken)
    {
        EveTurnOutcome outcome = await CollectOutcomeAsync(
            """{"type":"input.requested","data":{"requests":[{"requestId":"question_1","prompt":"Ship it?","kind":"question","display":"confirmation","allowFreeform":true,"options":[{"id":"approve","label":"Yes"},{"id":"deny","label":"No"}],"action":{"kind":"tool-call"}}],"sequence":1,"stepIndex":0,"turnId":"turn_1"}}""",
            cancellationToken);

        await Assert.That(outcome.InputRequests.Count).IsEqualTo(1);
        EveInputRequest request = outcome.InputRequests[0];
        await Assert.That(request.Kind)
            .IsEqualTo(EveInputRequestKind.Question)
            .Because("Approve/deny options with a confirmation hint are not always an approval.");
        await Assert.That(request.RawKind).IsEqualTo("question");
        await Assert.That(request.AllowFreeform.GetValueOrDefault())
            .IsTrue()
            .Because("The stream reported allowFreeform.");
        await Assert.That(request.Options.Count).IsEqualTo(2);
    }

    [Test]
    public async Task UnrecognizedKind_StaysInspectableWithoutMisclassification(
        CancellationToken cancellationToken)
    {
        EveTurnOutcome outcome = await CollectOutcomeAsync(
            InputRequestedEvent(",\"kind\":\"escalation\""),
            cancellationToken);

        await Assert.That(outcome.InputRequests.Count).IsEqualTo(1);
        await Assert.That(outcome.InputRequests[0].Kind).IsEqualTo(EveInputRequestKind.Unknown);
        await Assert.That(outcome.InputRequests[0].RawKind)
            .IsEqualTo("escalation")
            .Because("A future discriminator must remain readable on the wire value.");
    }

    [Test]
    public async Task AbsentKind_IsReportedAsALegacyServerRatherThanAnUnknownValue(
        CancellationToken cancellationToken)
    {
        EveTurnOutcome outcome = await CollectOutcomeAsync(
            InputRequestedEvent(string.Empty),
            cancellationToken);

        await Assert.That(outcome.InputRequests.Count).IsEqualTo(1);
        await Assert.That(outcome.InputRequests[0].Kind).IsEqualTo(EveInputRequestKind.Unknown);
        await Assert.That(outcome.InputRequests[0].RawKind)
            .IsNull()
            .Because("eve versions before the discriminator omit it entirely.");
        await Assert.That(outcome.InputRequests[0].Action.GetProperty("kind").GetString())
            .IsEqualTo("tool-call")
            .Because("The action's own kind must not be read as the request discriminator.");
        await Assert.That(outcome.InputRequests[0].RequestId).IsEqualTo("request_1");
    }

    [Test]
    public async Task NonStringKind_FailsInsteadOfImpersonatingALegacyServer(
        CancellationToken cancellationToken)
    {
        await Assert.That(async () => await CollectOutcomeAsync(
                InputRequestedEvent(",\"kind\":7"),
                cancellationToken))
            .Throws<EveProtocolException>();
    }

    [Test]
    public async Task MultipleRequests_KeepTheirOwnKinds(CancellationToken cancellationToken)
    {
        EveTurnOutcome outcome = await CollectOutcomeAsync(
            """{"type":"input.requested","data":{"requests":[{"requestId":"approval_1","prompt":"Run bash?","kind":"tool-approval","display":"confirmation","options":[{"id":"approve","label":"Yes"}],"action":{"kind":"tool-call","toolName":"bash"}},{"requestId":"question_1","prompt":"Which environment?","kind":"question","display":"select","options":[{"id":"prod","label":"Production"}],"action":{"kind":"tool-call"}}],"sequence":1,"stepIndex":0,"turnId":"turn_1"}}""",
            cancellationToken);

        await Assert.That(outcome.InputRequests.Count).IsEqualTo(2);
        await Assert.That(outcome.InputRequests[0].Kind).IsEqualTo(EveInputRequestKind.ToolApproval);
        await Assert.That(outcome.InputRequests[0].RequestId).IsEqualTo("approval_1");
        await Assert.That(outcome.InputRequests[1].Kind).IsEqualTo(EveInputRequestKind.Question);
        await Assert.That(outcome.InputRequests[1].RequestId).IsEqualTo("question_1");
    }

    private static string InputRequestedEvent(string kindProperty) =>
        """{"type":"input.requested","data":{"requests":[{"requestId":"request_1","prompt":"Continue?","display":"confirmation","options":[{"id":"approve","label":"Approve"}],"action":{"kind":"tool-call"}"""
        + kindProperty
        + """}],"sequence":1,"stepIndex":0,"turnId":"turn_1"}}""";

    private static async Task<EveTurnOutcome> CollectOutcomeAsync(
        string inputRequestedEvent,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue((_, _) => Task.FromResult(StreamResponse(
            inputRequestedEvent,
            """{"type":"session.waiting","data":{"continuationToken":"eve:next","wait":"human-input"}}""")));
        EveSession session = new EveClient(
            transport,
            new EveClientOptions("https://agent.example.com")).CreateSession();

        EveMessageResponse response = await session.SendAsync("Continue", cancellationToken);
        return await response.GetOutcomeAsync(cancellationToken);
    }

    private static HttpResponseMessage AcceptedResponse() =>
        new(HttpStatusCode.Accepted)
        {
            Content = new StringContent(
                """{"ok":true,"sessionId":"session_1","continuationToken":"eve:accepted"}""",
                Encoding.UTF8,
                "application/json"),
        };

    private static HttpResponseMessage StreamResponse(params string[] events) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{string.Join('\n', events)}\n",
                Encoding.UTF8,
                EveProtocol.MessageStreamContentType),
        };
}
