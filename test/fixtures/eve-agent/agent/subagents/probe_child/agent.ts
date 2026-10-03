import { defineAgent } from "eve";
import { model } from "../../agent";

export default defineAgent({
  description: "Deterministic child used to qualify the direct child stream route.",
  model,
  modelContextWindowTokens: 8_192,
});
