import { defineTool } from "eve/tools";

export default defineTool({
  description: "A deterministic durable workflow tool used by the compatibility probe.",
  inputSchema: {
    type: "object",
    properties: { value: { type: "string" } },
    required: ["value"],
    additionalProperties: false,
  },
  async execute({ value }) {
    "use workflow";

    return { value };
  },
});
