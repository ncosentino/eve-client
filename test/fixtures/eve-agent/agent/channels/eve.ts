import type { AuthFn } from "eve/channels/auth";
import { eveChannel } from "eve/channels/eve";
import { none } from "eve/channels/auth";
import type { SessionAuthContext } from "eve/context";

const compatibilityPrincipal: SessionAuthContext = {
  attributes: {},
  authenticator: "compatibility-probe",
  issuer: "nexuslabs-eve-fixture",
  principalId: "compatibility-user",
  principalType: "user",
  subject: "compatibility-user",
};
const authenticateCompatibilityUser: AuthFn<Request> = (request) =>
  request.headers.get("authorization") === "Bearer compatibility-user"
    ? compatibilityPrincipal
    : null;

export default eveChannel({
  auth: [authenticateCompatibilityUser, none()],
});
