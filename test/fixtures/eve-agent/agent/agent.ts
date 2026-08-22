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
    const callbackToolDiscovered = prompt.includes("callback-auth__probeHealth");
    const callbackToolCompleted = prompt.includes('"status":"ready"');
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

          controller.enqueue({
            delta: "CONNECTION_OK",
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
