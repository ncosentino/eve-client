import {
  ConnectionAuthorizationRequiredError,
  defineInteractiveAuthorization,
  defineOpenAPIConnection,
} from "eve/connections";

const authorizedPrincipals = new Set<string>();
const connectionName = "callback-auth";
const connectionToken = "compatibility-connection-token";
const port = process.env.EVE_TEST_PORT ?? "43125";

export default defineOpenAPIConnection({
  auth: defineInteractiveAuthorization({
    async getToken({ principal }) {
      if (principal.type === "user" && authorizedPrincipals.has(principal.id)) {
        return { token: connectionToken };
      }

      throw new ConnectionAuthorizationRequiredError(connectionName);
    },
    async startAuthorization() {
      return {
        challenge: {
          instructions: "Complete the deterministic compatibility callback.",
        },
      };
    },
    async completeAuthorization({ principal }) {
      if (principal.type === "user") {
        authorizedPrincipals.add(principal.id);
      }

      return { token: connectionToken };
    },
  }),
  baseUrl: `http://127.0.0.1:${port}`,
  description:
    "Deterministic callback-authorized connection used by the C# compatibility probe.",
  operations: { allow: ["probeHealth"] },
  spec: {
    info: {
      title: "Compatibility probe",
      version: "1.0.0",
    },
    openapi: "3.0.0",
    paths: {
      "/eve/v1/health": {
        get: {
          operationId: "probeHealth",
          responses: {
            "200": {
              description: "The deterministic Eve health response.",
            },
          },
        },
      },
    },
  },
});
