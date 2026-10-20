import {
  StringBody,
  arrayFeeder,
  atOnceUsers,
  getParameter,
  global,
  jsonPath,
  scenario,
  simulation,
} from "@gatling.io/core";
import { header, http, status } from "@gatling.io/http";

function requiredParameter(name) {
  const value = getParameter(name, "");
  if (!value) {
    throw new Error(`Missing Gatling parameter: ${name}`);
  }

  return value;
}

function csvParameter(name) {
  return requiredParameter(name).split(",");
}

const baseUrl = requiredParameter("baseUrl");
const collectorUrl = requiredParameter("collectorUrl");
const token = requiredParameter("token");
const customerId = requiredParameter("customerId");
const idempotencyKey = requiredParameter("idempotencyKey");
const idempotencyProductId = requiredParameter("idempotencyProductId");
const oversellOrders = csvParameter("oversellOrders");
const duplicateConfirmOrder = requiredParameter("duplicateConfirmOrder");
const duplicateCancelOrder = requiredParameter("duplicateCancelOrder");
const confirmCancelOrder = requiredParameter("confirmCancelOrder");
const atomicityOrder = requiredParameter("atomicityOrder");
const deadlockOrders = csvParameter("deadlockOrders");

const httpProtocol = http
  .baseUrl(baseUrl)
  .acceptHeader("application/json")
  .userAgentHeader("order-service-consistency-gatling/1.0");

function authenticated(request) {
  return request
    .header("Authorization", `Bearer ${token}`)
    .header("Content-Type", "application/json");
}

function observe(label) {
  return http(`collector-${label}`)
    .post(`${collectorUrl}/observations`)
    .header("Content-Type", "application/json")
    .body(
      StringBody((session) =>
        JSON.stringify({
          label,
          operation: session.contains("operation") ? session.get("operation") : label,
          status: Number(session.get("observedStatus")),
          instanceId: session.get("observedInstance"),
          orderId: session.contains("responseOrderId")
            ? session.get("responseOrderId")
            : session.contains("orderId")
              ? session.get("orderId")
              : null,
          userId: session.userId(),
        }),
      ),
    )
    .check(status().is(204))
    .silent();
}

const idempotencyPayload = JSON.stringify({
  customerId,
  currency: "BRL",
  items: [{ productId: idempotencyProductId, quantity: 1 }],
});

const idempotencyRace = scenario("Distributed idempotency race")
  .rendezVous(20)
  .exec(
    authenticated(
      http("idempotency-create")
        .post("/orders")
        .header("Idempotency-Key", idempotencyKey)
        .body(StringBody(idempotencyPayload)),
    ).check(
      status().in(200, 201).saveAs("observedStatus"),
      header("X-Instance-Id").in("api-1", "api-2").saveAs("observedInstance"),
      jsonPath("$.id").saveAs("responseOrderId"),
    ),
    observe("idempotency-create"),
  );

const oversellFeeder = arrayFeeder(
  oversellOrders.map((orderId) => ({ orderId, operation: "confirm" })),
).queue();

const oversellRace = scenario("Distributed overselling race")
  .feed(oversellFeeder)
  .rendezVous(oversellOrders.length)
  .exec(
    authenticated(
      http("oversell-confirm").post((session) => `/orders/${session.get("orderId")}/confirm`),
    ).check(
      status().in(200, 409).saveAs("observedStatus"),
      header("X-Instance-Id").in("api-1", "api-2").saveAs("observedInstance"),
    ),
    observe("oversell-confirm"),
  );

const duplicateConfirmFeeder = arrayFeeder(
  Array.from({ length: 20 }, () => ({
    orderId: duplicateConfirmOrder,
    operation: "confirm",
  })),
).queue();

const duplicateConfirmRace = scenario("Same order confirmation race")
  .feed(duplicateConfirmFeeder)
  .rendezVous(20)
  .exec(
    authenticated(http("duplicate-confirm").post(`/orders/${duplicateConfirmOrder}/confirm`)).check(
      status().is(200).saveAs("observedStatus"),
      header("X-Instance-Id").in("api-1", "api-2").saveAs("observedInstance"),
    ),
    observe("duplicate-confirm"),
  );

const duplicateCancelFeeder = arrayFeeder(
  Array.from({ length: 20 }, () => ({
    orderId: duplicateCancelOrder,
    operation: "cancel",
  })),
).queue();

const duplicateCancelRace = scenario("Same order cancellation race")
  .feed(duplicateCancelFeeder)
  .rendezVous(20)
  .exec(
    authenticated(http("duplicate-cancel").post(`/orders/${duplicateCancelOrder}/cancel`)).check(
      status().is(200).saveAs("observedStatus"),
      header("X-Instance-Id").in("api-1", "api-2").saveAs("observedInstance"),
    ),
    observe("duplicate-cancel"),
  );

const confirmCancelFeeder = arrayFeeder([
  { orderId: confirmCancelOrder, operation: "confirm" },
  { orderId: confirmCancelOrder, operation: "cancel" },
]).queue();

const confirmCancelRace = scenario("Confirm versus cancel race")
  .feed(confirmCancelFeeder)
  .rendezVous(2)
  .exec(
    authenticated(
      http("confirm-vs-cancel").post(
        (session) => `/orders/${session.get("orderId")}/${session.get("operation")}`,
      ),
    ).check(
      status().in(200, 409).saveAs("observedStatus"),
      header("X-Instance-Id").in("api-1", "api-2").saveAs("observedInstance"),
    ),
    observe("confirm-vs-cancel"),
  );

const atomicityFeeder = arrayFeeder([
  { orderId: atomicityOrder, operation: "confirm" },
]).queue();

const atomicity = scenario("Multi item atomic rollback")
  .feed(atomicityFeeder)
  .exec(
    authenticated(http("atomicity-confirm").post(`/orders/${atomicityOrder}/confirm`)).check(
      status().is(409).saveAs("observedStatus"),
      header("X-Instance-Id").in("api-1", "api-2").saveAs("observedInstance"),
    ),
    observe("atomicity-confirm"),
  );

const deadlockFeeder = arrayFeeder(
  deadlockOrders.map((orderId) => ({ orderId, operation: "confirm" })),
).queue();

const deadlockRace = scenario("Ordered multi product contention")
  .feed(deadlockFeeder)
  .rendezVous(deadlockOrders.length)
  .exec(
    authenticated(
      http("deadlock-confirm").post((session) => `/orders/${session.get("orderId")}/confirm`),
    ).check(
      status().is(200).saveAs("observedStatus"),
      header("X-Instance-Id").in("api-1", "api-2").saveAs("observedInstance"),
    ),
    observe("deadlock-confirm"),
  );

export default simulation((setUp) => {
  setUp(
    idempotencyRace.injectOpen(atOnceUsers(20)),
    oversellRace.injectOpen(atOnceUsers(oversellOrders.length)),
    duplicateConfirmRace.injectOpen(atOnceUsers(20)),
    duplicateCancelRace.injectOpen(atOnceUsers(20)),
    confirmCancelRace.injectOpen(atOnceUsers(2)),
    atomicity.injectOpen(atOnceUsers(1)),
    deadlockRace.injectOpen(atOnceUsers(deadlockOrders.length)),
  )
    .protocols(httpProtocol)
    .assertions(global().failedRequests().count().is(0))
    .maxDuration(120);
});
