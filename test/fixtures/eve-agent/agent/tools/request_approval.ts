import { defineTool } from "eve/tools";
import { always } from "eve/tools/approval";

export default defineTool({
  approval: always(),
  description: "Deterministic approval-gated tool used by the C# compatibility probe.",
  inputSchema: {
    additionalProperties: false,
    properties: {
      reason: { type: "string" },
    },
    required: ["reason"],
    type: "object",
  },
  execute: () => ({ status: "APPROVAL_TOOL_OK" }),
});
