import { defineTool } from "eve/tools";

export default defineTool({
  description: "A deterministic tool that forces a second model call in one turn.",
  inputSchema: {
    additionalProperties: false,
    properties: {},
    type: "object",
  },
  execute: () => ({ status: "CLIENT_CONTEXT_TOOL_OK" }),
});
