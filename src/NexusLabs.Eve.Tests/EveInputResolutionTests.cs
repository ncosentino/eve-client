using System.Net;
using System.Text;

namespace NexusLabs.Eve.Tests;

public sealed class EveInputResolutionTests
{
    [Test]
    public async Task Outcome_ProjectsResponseBearingAndResponseLessResolutionsInOrder(
        CancellationToken cancellationToken)
    {
        EveTurnOutcome outcome = await CollectOutcomeAsync(
            """
            {"type":"input.requested","data":{"requests":[{"requestId":"approval_1","prompt":"Run bash?","kind":"tool-approval","options":[{"id":"approve","label":"Approve"},{"id":"deny","label":"Deny"}],"action":{"kind":"tool-call","toolName":"bash"}},{"requestId":"question_1","prompt":"Which environment?","kind":"question","allowFreeform":true,"action":{"kind":"tool-call","toolName":"ask_question"}},{"requestId":"question_2","prompt":"Which region?","kind":"question","allowFreeform":true,"action":{"kind":"tool-call","toolName":"ask_question"}}],"sequence":7,"stepIndex":2,"turnId":"turn_1"}}
            """,
            """
            {"type":"input.resolved","data":{"resolutions":[{"kind":"tool-approval","outcome":"approved","requestId":"approval_1","response":{"optionId":"approve","requestId":"approval_1"}},{"kind":"question","outcome":"ignored","requestId":"question_1"}],"sequence":7,"stepIndex":2,"turnId":"turn_1"}}
            """,
            """
            {"type":"step.started","data":{"sequence":8,"stepIndex":3,"turnId":"turn_1"}}
            """,
            cancellationToken);

        await Assert.That(outcome.Events.Count).IsEqualTo(4);
        await Assert.That(outcome.Events[0].Kind).IsEqualTo(EveStreamEventKind.InputRequested);
        await Assert.That(outcome.Events[1].Kind).IsEqualTo(EveStreamEventKind.InputResolved);
        await Assert.That(outcome.Events[2].Kind)
            .IsEqualTo(EveStreamEventKind.StepStarted)
            .Because("The durable resolution must precede the resumed model step.");
        await Assert.That(outcome.Events[3].Kind).IsEqualTo(EveStreamEventKind.SessionWaiting);
        await Assert.That(outcome.InputRequests.Count).IsEqualTo(3);
        await Assert.That(outcome.InputRequests[2].RequestId)
            .IsEqualTo("question_2")
            .Because("Resolving other requests must not remove an unrelated pending request.");
        await Assert.That(outcome.InputResolutions.Count).IsEqualTo(2);

        EveInputResolution approval = outcome.InputResolutions[0];
        await Assert.That(approval.RequestId).IsEqualTo("approval_1");
        await Assert.That(approval.Kind).IsEqualTo(EveInputRequestKind.ToolApproval);
        await Assert.That(approval.RawKind).IsEqualTo("tool-approval");
        await Assert.That(approval.Outcome).IsEqualTo(EveInputResolutionOutcome.Approved);
        await Assert.That(approval.RawOutcome).IsEqualTo("approved");
        await Assert.That(approval.Response).IsNotNull();
        await Assert.That(approval.Response!.RequestId).IsEqualTo("approval_1");
        await Assert.That(approval.Response.OptionId).IsEqualTo("approve");
        await Assert.That(approval.Response.Text).IsNull();
        await Assert.That(approval.TurnId).IsEqualTo("turn_1");
        await Assert.That(approval.StepIndex).IsEqualTo(2);
        await Assert.That(approval.Sequence).IsEqualTo(7);
        await Assert.That(approval.Raw.GetProperty("requestId").GetString())
            .IsEqualTo("approval_1");

        EveInputResolution ignored = outcome.InputResolutions[1];
        await Assert.That(ignored.RequestId).IsEqualTo("question_1");
        await Assert.That(ignored.Kind).IsEqualTo(EveInputRequestKind.Question);
        await Assert.That(ignored.Outcome).IsEqualTo(EveInputResolutionOutcome.Ignored);
        await Assert.That(ignored.Response)
            .IsNull()
            .Because("Ignored requests intentionally carry no accepted response.");
    }

    [Test]
    [Arguments("answered", EveInputResolutionOutcome.Answered)]
    [Arguments("approved", EveInputResolutionOutcome.Approved)]
    [Arguments("denied", EveInputResolutionOutcome.Denied)]
    [Arguments("ignored", EveInputResolutionOutcome.Ignored)]
    [Arguments("invalid", EveInputResolutionOutcome.Invalid)]
    public async Task Outcome_ProjectsEveryKnownResolutionOutcome(
        string rawOutcome,
        EveInputResolutionOutcome expectedOutcome,
        CancellationToken cancellationToken)
    {
        EveTurnOutcome outcome = await CollectOutcomeAsync(
            $$$"""
            {"type":"input.resolved","data":{"resolutions":[{"kind":"question","outcome":"{{{rawOutcome}}}","requestId":"question_1"}],"sequence":1,"stepIndex":0,"turnId":"turn_1"}}
            """,
            cancellationToken);

        await Assert.That(outcome.InputResolutions.Count).IsEqualTo(1);
        await Assert.That(outcome.InputResolutions[0].Outcome).IsEqualTo(expectedOutcome);
        await Assert.That(outcome.InputResolutions[0].RawOutcome).IsEqualTo(rawOutcome);
    }

    [Test]
    public async Task Outcome_PreservesUnknownKindAndOutcome(
        CancellationToken cancellationToken)
    {
        EveTurnOutcome outcome = await CollectOutcomeAsync(
            """
            {"type":"input.resolved","data":{"resolutions":[{"kind":"escalation","outcome":"expired","requestId":"request_1"}],"sequence":1,"stepIndex":0,"turnId":"turn_1"}}
            """,
            cancellationToken);

        EveInputResolution resolution = outcome.InputResolutions[0];
        await Assert.That(resolution.Kind).IsEqualTo(EveInputRequestKind.Unknown);
        await Assert.That(resolution.RawKind).IsEqualTo("escalation");
        await Assert.That(resolution.Outcome).IsEqualTo(EveInputResolutionOutcome.Unknown);
        await Assert.That(resolution.RawOutcome).IsEqualTo("expired");
    }

    [Test]
    public async Task AttachedStream_RecognizesReplayedResolutionBeforeResumedStep(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """
            {"type":"input.resolved","data":{"resolutions":[{"kind":"question","outcome":"answered","requestId":"question_1","response":{"requestId":"question_1","text":"Production"}}],"sequence":4,"stepIndex":1,"turnId":"turn_1"}}
            """,
            """
            {"type":"step.started","data":{"sequence":5,"stepIndex":2,"turnId":"turn_1"}}
            """)));
        EveSession session = new EveClient(
            transport,
            new EveClientOptions("https://agent.example.com")).AttachSession("session_1");
        List<EveStreamEvent> events = [];

        await foreach (EveStreamEvent streamEvent in session.StreamAsync(
            new EveStreamOptions
            {
                ReconnectPolicy = EveStreamReconnectPolicy.Disabled,
            },
            cancellationToken))
        {
            events.Add(streamEvent);
        }

        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(events[0].Kind).IsEqualTo(EveStreamEventKind.InputResolved);
        await Assert.That(events[1].Kind).IsEqualTo(EveStreamEventKind.StepStarted);
        await Assert.That(events[0].Data.GetProperty("resolutions")[0]
            .GetProperty("response")
            .GetProperty("text")
            .GetString()).IsEqualTo("Production");
    }

    [Test]
    [Arguments("""{"type":"input.resolved","data":{"sequence":1,"stepIndex":0,"turnId":"turn_1"}}""")]
    [Arguments("""{"type":"input.resolved","data":{"resolutions":{},"sequence":1,"stepIndex":0,"turnId":"turn_1"}}""")]
    [Arguments("""{"type":"input.resolved","data":{"resolutions":[7],"sequence":1,"stepIndex":0,"turnId":"turn_1"}}""")]
    [Arguments("""{"type":"input.resolved","data":{"resolutions":[{"kind":"question","outcome":"answered"}],"sequence":1,"stepIndex":0,"turnId":"turn_1"}}""")]
    [Arguments("""{"type":"input.resolved","data":{"resolutions":[{"outcome":"answered","requestId":"question_1"}],"sequence":1,"stepIndex":0,"turnId":"turn_1"}}""")]
    [Arguments("""{"type":"input.resolved","data":{"resolutions":[{"kind":"question","requestId":"question_1"}],"sequence":1,"stepIndex":0,"turnId":"turn_1"}}""")]
    [Arguments("""{"type":"input.resolved","data":{"resolutions":[],"sequence":1,"stepIndex":0,"turnId":7}}""")]
    [Arguments("""{"type":"input.resolved","data":{"resolutions":[],"sequence":1,"stepIndex":"zero","turnId":"turn_1"}}""")]
    [Arguments("""{"type":"input.resolved","data":{"resolutions":[],"sequence":"one","stepIndex":0,"turnId":"turn_1"}}""")]
    [Arguments("""{"type":"input.resolved","data":{"resolutions":[{"kind":"question","outcome":"answered","requestId":"question_1","response":"answer"}],"sequence":1,"stepIndex":0,"turnId":"turn_1"}}""")]
    [Arguments("""{"type":"input.resolved","data":{"resolutions":[{"kind":"question","outcome":"answered","requestId":"question_1","response":{"requestId":" ","text":"Production"}}],"sequence":1,"stepIndex":0,"turnId":"turn_1"}}""")]
    [Arguments("""{"type":"input.resolved","data":{"resolutions":[{"kind":"question","outcome":"answered","requestId":"question_1","response":{"requestId":"question_1","text":7}}],"sequence":1,"stepIndex":0,"turnId":"turn_1"}}""")]
    public async Task Outcome_RejectsMalformedResolutionShape(
        string inputResolvedEvent,
        CancellationToken cancellationToken)
    {
        await Assert.That(async () => await CollectOutcomeAsync(
                inputResolvedEvent,
                cancellationToken))
            .Throws<EveProtocolException>();
    }

    private static async Task<EveTurnOutcome> CollectOutcomeAsync(
        string firstEvent,
        CancellationToken cancellationToken) =>
        await CollectOutcomeAsync([firstEvent], cancellationToken);

    private static async Task<EveTurnOutcome> CollectOutcomeAsync(
        string firstEvent,
        string secondEvent,
        string thirdEvent,
        CancellationToken cancellationToken) =>
        await CollectOutcomeAsync([firstEvent, secondEvent, thirdEvent], cancellationToken);

    private static async Task<EveTurnOutcome> CollectOutcomeAsync(
        IReadOnlyList<string> events,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue((_, _) => Task.FromResult(StreamResponse(
            [.. events, SessionWaitingEvent])));
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
                """{"ok":true,"sessionId":"session_1"}""",
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

    private const string SessionWaitingEvent =
        """{"type":"session.waiting","data":{"continuationToken":"eve:next","wait":"human-input"}}""";
}
