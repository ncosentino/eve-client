import { defineAgent } from "eve";
import { model } from "../../agent";

export default defineAgent({
  description: "Deterministic child used to qualify recorded descendant streaming.",
  experimental: { workflow: { retention: "default" } },
  model,
  modelContextWindowTokens: 8_192,
});
