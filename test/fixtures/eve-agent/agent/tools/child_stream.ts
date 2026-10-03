import { defineWorkflowTool } from "eve/tools";

export default defineWorkflowTool({
  description: "Opens a deterministic local child for the compatibility probe.",
  inputSchema: {
    additionalProperties: false,
    properties: {},
    type: "object",
  },
  async execute(_input, context) {
    "use workflow";

    const child = context.agent("probe_child");
    const response = await child.send("CHILD_STREAM_RESPONSE");
    const result = await response.result();
    if (result.message !== "CHILD_STREAM_OK") {
      throw new Error("The deterministic child did not produce its expected response.");
    }
    return { status: "CHILD_TOOL_OK" };
  },
});
