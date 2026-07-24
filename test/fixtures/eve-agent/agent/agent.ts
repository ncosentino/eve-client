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
    const shouldWaitForCancellation = JSON.stringify(options.prompt).includes(
      "WAIT_FOR_CANCEL",
    );

    return {
      stream: new ReadableStream({
        start(controller) {
          controller.enqueue({ type: "stream-start", warnings: [] });
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
