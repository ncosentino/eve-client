import { MockLanguageModelV3 } from "ai/test";
import { defineAgent } from "eve";

const usage = {
  inputTokens: {
    cacheRead: 0,
    cacheWrite: 0,
    noCache: 1,
    total: 1,
  },
  outputTokens: {
    reasoning: 0,
    text: 1,
    total: 1,
  },
};

const model = new MockLanguageModelV3({
  modelId: "nexuslabs-eve-compatibility",
  provider: "nexuslabs-test",
  doStream: async (options) => {
    const prompt = JSON.stringify(options.prompt);
    const shouldWaitForCancellation = prompt.includes("WAIT_FOR_CANCEL");
    const shouldRequestApproval =
      prompt.includes("REQUEST_APPROVAL") && !prompt.includes("APPROVAL_TOOL_OK");
    const isCallbackAuthorizationProbe = prompt.includes("REQUEST_CALLBACK_AUTH");
    const isAcceptedDeliveryCorrelationProbe = prompt.includes(
      "STALE_CURSOR_ACCEPTED_DELIVERY",
    );
    const isPriorDeliveryCorrelationProbe = prompt.includes(
      "STALE_CURSOR_PRIOR_DELIVERY",
    );
    const callbackToolDiscovered = prompt.includes("callback-auth__probeHealth");
    const callbackToolCompleted = prompt.includes('"status":"ready"');
    const isFollowingTurnClientContextProbe = prompt.includes(
      "VERIFY_FOLLOWING_TURN_CLIENT_CONTEXT",
    );
    const isTurnScopedClientContextProbe =
      !isFollowingTurnClientContextProbe &&
      prompt.includes("VERIFY_TURN_SCOPED_CLIENT_CONTEXT");
    const hasTurnScopedClientContext = prompt.includes(
      "TURN_SCOPED_CLIENT_CONTEXT_140",
    );
    const clientContextToolCompleted = prompt.includes(
      '"status":"CLIENT_CONTEXT_TOOL_OK"',
    );
    const shouldCallClientContextTool =
      isTurnScopedClientContextProbe && !clientContextToolCompleted;
    const shouldSearchCallbackConnection =
      isCallbackAuthorizationProbe && !callbackToolDiscovered;
    const shouldCallCallbackConnection =
      isCallbackAuthorizationProbe && callbackToolDiscovered && !callbackToolCompleted;

    return {
      stream: new ReadableStream({
        start(controller) {
          controller.enqueue({ type: "stream-start", warnings: [] });

          if (shouldRequestApproval) {
            const input = JSON.stringify({ reason: "compatibility" });
            controller.enqueue({
              id: "call_approval",
              toolName: "request_approval",
              type: "tool-input-start",
            });
            controller.enqueue({
              delta: input,
              id: "call_approval",
              type: "tool-input-delta",
            });
            controller.enqueue({ id: "call_approval", type: "tool-input-end" });
            controller.enqueue({
              input,
              toolCallId: "call_approval",
              toolName: "request_approval",
              type: "tool-call",
            });
            controller.enqueue({
              finishReason: { raw: undefined, unified: "tool-calls" },
              type: "finish",
              usage,
            });
            controller.close();
            return;
          }

          if (shouldSearchCallbackConnection) {
            const input = JSON.stringify({
              connection: "callback-auth",
              keywords: "probe health",
              limit: 1,
            });
            controller.enqueue({
              id: "call_connection_search",
              toolName: "connection_search",
              type: "tool-input-start",
            });
            controller.enqueue({
              delta: input,
              id: "call_connection_search",
              type: "tool-input-delta",
            });
            controller.enqueue({
              id: "call_connection_search",
              type: "tool-input-end",
            });
            controller.enqueue({
              input,
              toolCallId: "call_connection_search",
              toolName: "connection_search",
              type: "tool-call",
            });
            controller.enqueue({
              finishReason: { raw: undefined, unified: "tool-calls" },
              type: "finish",
              usage,
            });
            controller.close();
            return;
          }

          if (shouldCallCallbackConnection) {
            const input = "{}";
            controller.enqueue({
              id: "call_callback_auth",
              toolName: "callback-auth__probeHealth",
              type: "tool-input-start",
            });
            controller.enqueue({
              delta: input,
              id: "call_callback_auth",
              type: "tool-input-delta",
            });
            controller.enqueue({
              id: "call_callback_auth",
              type: "tool-input-end",
            });
            controller.enqueue({
              input,
              toolCallId: "call_callback_auth",
              toolName: "callback-auth__probeHealth",
              type: "tool-call",
            });
            controller.enqueue({
              finishReason: { raw: undefined, unified: "tool-calls" },
              type: "finish",
              usage,
            });
            controller.close();
            return;
          }

          if (shouldCallClientContextTool) {
            const input = "{}";
            controller.enqueue({
              id: "call_client_context",
              toolName: "client_context_probe",
              type: "tool-input-start",
            });
            controller.enqueue({
              delta: input,
              id: "call_client_context",
              type: "tool-input-delta",
            });
            controller.enqueue({
              id: "call_client_context",
              type: "tool-input-end",
            });
            controller.enqueue({
              input,
              toolCallId: "call_client_context",
              toolName: "client_context_probe",
              type: "tool-call",
            });
            controller.enqueue({
              finishReason: { raw: undefined, unified: "tool-calls" },
              type: "finish",
              usage,
            });
            controller.close();
            return;
          }

          controller.enqueue({ id: "answer", type: "text-start" });

          if (shouldWaitForCancellation) {
            controller.enqueue({
              delta: "WAITING_FOR_CANCEL",
              id: "answer",
              type: "text-delta",
            });
            options.abortSignal?.addEventListener(
              "abort",
              () => {
                controller.error(
                  options.abortSignal?.reason ??
                    new DOMException("The turn was cancelled.", "AbortError"),
                );
              },
              { once: true },
            );
            return;
          }

          const responseText = isFollowingTurnClientContextProbe
            ? hasTurnScopedClientContext
              ? "CLIENT_CONTEXT_PRESENT_ON_FOLLOWING_TURN"
              : "CLIENT_CONTEXT_ABSENT_ON_FOLLOWING_TURN"
            : isTurnScopedClientContextProbe
              ? hasTurnScopedClientContext
                ? "CLIENT_CONTEXT_PRESENT_AFTER_TOOL"
                : "CLIENT_CONTEXT_ABSENT_AFTER_TOOL"
              : isAcceptedDeliveryCorrelationProbe
                ? "ACCEPTED_DELIVERY_RESPONSE"
                : isPriorDeliveryCorrelationProbe
                  ? "PRIOR_DELIVERY_RESPONSE"
                  : "CONNECTION_OK";
          controller.enqueue({
            delta: responseText,
            id: "answer",
            type: "text-delta",
          });
          controller.enqueue({ id: "answer", type: "text-end" });
          controller.enqueue({
            finishReason: { raw: undefined, unified: "stop" },
            type: "finish",
            usage,
          });
          controller.close();
        },
      }),
    };
  },
});

export default defineAgent({
  model,
  modelContextWindowTokens: 8_192,
});
