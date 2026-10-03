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

export const model = new MockLanguageModelV3({
  modelId: "nexuslabs-eve-compatibility",
  provider: "nexuslabs-test",
  doStream: async (options) => {
    const prompt = JSON.stringify(options.prompt);
    const shouldCallChild =
      prompt.includes("REQUEST_CHILD_STREAM") && !prompt.includes("CHILD_TOOL_OK");
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
    const callbackToolDiscovered = prompt.includes('"tool":"probeHealth"');
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

          if (prompt.includes("STRUCTURED_RESPONSE")) {
            if (!options.tools?.some(
              (tool) => tool.type === "function" && tool.name === "final_output",
            )) {
              throw new Error("The structured request did not supply the final_output tool.");
            }
            controller.enqueue({
              input: JSON.stringify({ status: "STRUCTURED_OK" }),
              toolCallId: "call_structured",
              toolName: "final_output",
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

          if (shouldCallChild) {
            const input = "{}";
            controller.enqueue({
              input,
              toolCallId: "call_child",
              toolName: "child_stream",
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
              query: "probe health",
              signIn: true,
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
            const input = JSON.stringify({
              connection: "callback-auth",
              tool: "probeHealth",
              input: {},
            });
            controller.enqueue({
              id: "call_callback_auth",
              toolName: "connection_execute",
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
              toolName: "connection_execute",
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

          controller.enqueue({ id: "thought", type: "reasoning-start" });
          controller.enqueue({
            delta: "DETERMINISTIC_REASONING",
            id: "thought",
            type: "reasoning-delta",
          });
          controller.enqueue({ id: "thought", type: "reasoning-end" });
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

          const responseText = prompt.includes("CHILD_STREAM_RESPONSE")
            ? "CHILD_STREAM_OK"
            : isFollowingTurnClientContextProbe
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
