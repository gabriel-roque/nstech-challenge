import { spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { createServer } from "node:http";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { isDeepStrictEqual } from "node:util";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const gatlingDirectory = resolve(scriptDirectory, "..");
const repositoryDirectory = resolve(gatlingDirectory, "../..");
const baseUrl = process.env.BASE_URL ?? "http://localhost:8080";
const demoPassword = process.env.DEMO_PASSWORD ?? "Demo@123456";
const customerId = "00000000-0000-0000-0000-000000000001";
const runId = `${new Date().toISOString().replaceAll(/\D/g, "").slice(0, 14)}-${randomUUID().slice(0, 8)}`;
const reportDirectory = resolve(
  process.env.CONSISTENCY_REPORT_DIR ?? resolve(repositoryDirectory, "TestResults/consistency"),
  runId,
);
const gatlingReportDirectory = resolve(reportDirectory, "gatling");
const products = Object.fromEntries(
  [
    "idempotency",
    "oversell",
    "duplicateConfirm",
    "duplicateCancel",
    "confirmCancel",
    "atomicAvailable",
    "atomicEmpty",
    "deadlockA",
    "deadlockB",
  ].map((name) => [name, randomUUID()]),
);
const productIds = Object.values(products);
const checks = [];
const observations = [];
let fixtures;
let databaseBeforeRestart;
let databaseAfterRestart;
let gatlingExitCode = -1;
let collectorServer;
let collectorUrl;

await mkdir(gatlingReportDirectory, { recursive: true });

function runCommand(command, arguments_, options = {}) {
  const { cwd = repositoryDirectory, inherit = false, allowFailure = false } = options;

  return new Promise((resolvePromise, rejectPromise) => {
    const child = spawn(command, arguments_, {
      cwd,
      env: process.env,
      stdio: inherit ? ["ignore", "inherit", "inherit"] : ["ignore", "pipe", "pipe"],
    });
    let stdout = "";
    let stderr = "";

    if (!inherit) {
      child.stdout.setEncoding("utf8");
      child.stderr.setEncoding("utf8");
      child.stdout.on("data", (chunk) => {
        stdout += chunk;
      });
      child.stderr.on("data", (chunk) => {
        stderr += chunk;
      });
    }

    child.on("error", rejectPromise);
    child.on("close", (code) => {
      const result = { code: code ?? 1, stdout, stderr };
      if (result.code !== 0 && !allowFailure) {
        rejectPromise(
          new Error(
            `${command} ${arguments_.join(" ")} failed with exit code ${result.code}\n${stderr || stdout}`,
          ),
        );
        return;
      }

      resolvePromise(result);
    });
  });
}

async function sql(command) {
  for (let attempt = 1; attempt <= 20; attempt += 1) {
    const result = await runCommand(
      "docker",
      [
        "compose",
        "exec",
        "-T",
        "postgres",
        "psql",
        "--username",
        "orders",
        "--dbname",
        "orders",
        "--set",
        "ON_ERROR_STOP=1",
        "--tuples-only",
        "--no-align",
        "--command",
        command,
      ],
      { allowFailure: true },
    );

    if (result.code === 0) {
      return result.stdout.trim();
    }

    if (!result.stderr.includes("too many clients already") || attempt === 20) {
      throw new Error(`PostgreSQL command failed with exit code ${result.code}: ${result.stderr || result.stdout}`);
    }

    await new Promise((resolvePromise) => setTimeout(resolvePromise, 250));
  }

  throw new Error("PostgreSQL command retry loop ended unexpectedly.");
}

async function waitForReadiness(timeoutMilliseconds = 120_000) {
  const deadline = Date.now() + timeoutMilliseconds;
  let lastError;

  while (Date.now() < deadline) {
    try {
      const response = await fetch(`${baseUrl}/health/ready`, {
        signal: AbortSignal.timeout(2_000),
      });
      if (response.ok) {
        return;
      }

      lastError = new Error(`Readiness returned HTTP ${response.status}`);
    } catch (error) {
      lastError = error;
    }

    await new Promise((resolvePromise) => setTimeout(resolvePromise, 1_000));
  }

  throw new Error(`Application did not become ready: ${lastError?.message ?? "timeout"}`);
}

async function apiRequest(path, options = {}) {
  const response = await fetch(`${baseUrl}${path}`, {
    ...options,
    signal: AbortSignal.timeout(30_000),
  });
  const text = await response.text();
  let body;

  try {
    body = text ? JSON.parse(text) : null;
  } catch {
    body = text;
  }

  return { response, body };
}

async function authenticate() {
  const { response, body } = await apiRequest("/auth/token", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username: "user01", password: demoPassword }),
  });

  if (response.status !== 200 || !body?.accessToken) {
    throw new Error(`Authentication failed with HTTP ${response.status}: ${JSON.stringify(body)}`);
  }

  return body.accessToken;
}

async function createOrder(token, items) {
  const idempotencyKey = randomUUID();
  const { response, body } = await apiRequest("/orders", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
      "Idempotency-Key": idempotencyKey,
    },
    body: JSON.stringify({ customerId, currency: "BRL", items }),
  });

  if (response.status !== 201 || !body?.id) {
    throw new Error(`Fixture order creation failed with HTTP ${response.status}: ${JSON.stringify(body)}`);
  }

  return body.id;
}

async function transitionOrder(token, orderId, transition) {
  const { response, body } = await apiRequest(`/orders/${orderId}/${transition}`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
  });

  if (response.status !== 200) {
    throw new Error(
      `Fixture ${transition} failed for ${orderId} with HTTP ${response.status}: ${JSON.stringify(body)}`,
    );
  }
}

async function insertProducts() {
  const stocks = {
    idempotency: 100,
    oversell: 10,
    duplicateConfirm: 10,
    duplicateCancel: 10,
    confirmCancel: 10,
    atomicAvailable: 5,
    atomicEmpty: 1,
    deadlockA: 20,
    deadlockB: 20,
  };
  const values = Object.entries(products)
    .map(
      ([name, id], index) =>
        `('${id}'::uuid, 'ACID-${runId.slice(-8)}-${index}', 'ACID ${name}', 'BRL', 10.00, ${stocks[name]}, NOW(), NOW())`,
    )
    .join(",\n");

  await sql(`
    INSERT INTO products (id, sku, name, currency, unit_price, available_quantity, created_at, updated_at)
    VALUES ${values};
  `);
}

async function prepareFixtures(token) {
  await insertProducts();

  const oversellOrders = [];
  for (let index = 0; index < 20; index += 1) {
    oversellOrders.push(
      await createOrder(token, [{ productId: products.oversell, quantity: 1 }]),
    );
  }

  const duplicateConfirmOrder = await createOrder(token, [
    { productId: products.duplicateConfirm, quantity: 2 },
  ]);
  const duplicateCancelOrder = await createOrder(token, [
    { productId: products.duplicateCancel, quantity: 2 },
  ]);
  await transitionOrder(token, duplicateCancelOrder, "confirm");

  const confirmCancelOrder = await createOrder(token, [
    { productId: products.confirmCancel, quantity: 3 },
  ]);
  const atomicityOrder = await createOrder(token, [
    { productId: products.atomicAvailable, quantity: 2 },
    { productId: products.atomicEmpty, quantity: 1 },
  ]);
  await sql(`UPDATE products SET available_quantity = 0, updated_at = NOW() WHERE id = '${products.atomicEmpty}'::uuid;`);

  const deadlockOrders = [];
  for (let index = 0; index < 20; index += 1) {
    const items =
      index % 2 === 0
        ? [
            { productId: products.deadlockA, quantity: 1 },
            { productId: products.deadlockB, quantity: 1 },
          ]
        : [
            { productId: products.deadlockB, quantity: 1 },
            { productId: products.deadlockA, quantity: 1 },
          ];
    deadlockOrders.push(await createOrder(token, items));
  }

  return {
    idempotencyKey: randomUUID(),
    oversellOrders,
    duplicateConfirmOrder,
    duplicateCancelOrder,
    confirmCancelOrder,
    atomicityOrder,
    deadlockOrders,
  };
}

function startCollector() {
  collectorServer = createServer((request, response) => {
    if (request.method !== "POST" || request.url !== "/observations") {
      response.writeHead(404).end();
      return;
    }

    let body = "";
    request.setEncoding("utf8");
    request.on("data", (chunk) => {
      body += chunk;
      if (body.length > 64 * 1024) {
        request.destroy();
      }
    });
    request.on("end", () => {
      try {
        observations.push(JSON.parse(body));
        response.writeHead(204).end();
      } catch (error) {
        response.writeHead(400, { "Content-Type": "text/plain" }).end(error.message);
      }
    });
  });

  return new Promise((resolvePromise, rejectPromise) => {
    collectorServer.once("error", rejectPromise);
    collectorServer.listen(0, "127.0.0.1", () => {
      const address = collectorServer.address();
      collectorUrl = `http://127.0.0.1:${address.port}`;
      resolvePromise();
    });
  });
}

function stopCollector() {
  if (!collectorServer) {
    return Promise.resolve();
  }

  const server = collectorServer;
  collectorServer = undefined;
  return new Promise((resolvePromise, rejectPromise) => {
    server.close((error) => (error ? rejectPromise(error) : resolvePromise()));
  });
}

async function runGatling(token) {
  const gatlingExecutable = resolve(gatlingDirectory, "node_modules/.bin/gatling");
  const arguments_ = [
    "run",
    "--non-interactive",
    "--simulation",
    "orderConsistency",
    "--results-folder",
    gatlingReportDirectory,
    `baseUrl=${baseUrl}`,
    `collectorUrl=${collectorUrl}`,
    `token=${token}`,
    `customerId=${customerId}`,
    `idempotencyKey=${fixtures.idempotencyKey}`,
    `idempotencyProductId=${products.idempotency}`,
    `oversellOrders=${fixtures.oversellOrders.join(",")}`,
    `duplicateConfirmOrder=${fixtures.duplicateConfirmOrder}`,
    `duplicateCancelOrder=${fixtures.duplicateCancelOrder}`,
    `confirmCancelOrder=${fixtures.confirmCancelOrder}`,
    `atomicityOrder=${fixtures.atomicityOrder}`,
    `deadlockOrders=${fixtures.deadlockOrders.join(",")}`,
  ];
  const result = await runCommand(gatlingExecutable, arguments_, {
    cwd: gatlingDirectory,
    inherit: true,
    allowFailure: true,
  });

  gatlingExitCode = result.code;
}

function uuidList(values) {
  return values.map((value) => `'${value}'::uuid`).join(",");
}

async function readDatabaseState() {
  const output = await sql(`
    WITH run_orders AS (
      SELECT o.*
      FROM orders o
      WHERE EXISTS (
        SELECT 1
        FROM order_items oi
        WHERE oi.order_id = o.id
          AND oi.product_id IN (${uuidList(productIds)})
      )
    )
    SELECT json_build_object(
      'products', COALESCE((
        SELECT json_agg(
          json_build_object(
            'id', p.id::text,
            'stock', p.available_quantity
          ) ORDER BY p.id
        )
        FROM products p
        WHERE p.id IN (${uuidList(productIds)})
      ), '[]'::json),
      'orders', COALESCE((
        SELECT json_agg(
          json_build_object(
            'id', o.id::text,
            'idempotencyKey', o.idempotency_key::text,
            'status', o.status,
            'total', o.total
          ) ORDER BY o.id
        )
        FROM run_orders o
      ), '[]'::json),
      'invalidTotals', (
        SELECT COUNT(*)
        FROM run_orders o
        WHERE o.total <> (
          SELECT COALESCE(SUM(oi.unit_price * oi.quantity), 0)
          FROM order_items oi
          WHERE oi.order_id = o.id
        )
      )
    )::text;
  `);

  return JSON.parse(output);
}

function addCheck(category, name, passed, expected, actual) {
  checks.push({ category, name, passed: Boolean(passed), expected, actual });
}

function observationsFor(label) {
  return observations.filter((observation) => observation.label === label);
}

function statusCount(items, statusCode) {
  return items.filter((item) => item.status === statusCode).length;
}

function productStock(state, productId) {
  return state.products.find((product) => product.id === productId)?.stock;
}

function orderState(state, orderId) {
  return state.orders.find((order) => order.id === orderId);
}

function validateHttpResults() {
  const expectedObservations = 103;
  const instances = new Set(observations.map((observation) => observation.instanceId));
  addCheck("Topology", "all race responses collected", observations.length === expectedObservations, expectedObservations, observations.length);
  addCheck("Topology", "both API instances participated", instances.has("api-1") && instances.has("api-2"), ["api-1", "api-2"], [...instances].sort());
  addCheck("HTTP", "Gatling assertions passed", gatlingExitCode === 0, 0, gatlingExitCode);
  addCheck("HTTP", "no server errors", observations.every((item) => item.status < 500), "no HTTP 5xx", observations.filter((item) => item.status >= 500));

  const distributedLabels = ["idempotency-create", "oversell-confirm", "deadlock-confirm"];
  for (const label of distributedLabels) {
    const labelInstances = new Set(observationsFor(label).map((item) => item.instanceId));
    addCheck("Topology", `${label} crossed both APIs`, labelInstances.has("api-1") && labelInstances.has("api-2"), ["api-1", "api-2"], [...labelInstances].sort());
  }

  const idempotency = observationsFor("idempotency-create");
  const returnedOrderIds = new Set(idempotency.map((item) => item.orderId));
  addCheck("Idempotency", "one request created the order", statusCount(idempotency, 201) === 1, 1, statusCount(idempotency, 201));
  addCheck("Idempotency", "all other requests replayed the order", statusCount(idempotency, 200) === 19, 19, statusCount(idempotency, 200));
  addCheck("Idempotency", "all responses returned one order id", returnedOrderIds.size === 1, 1, [...returnedOrderIds]);

  const oversell = observationsFor("oversell-confirm");
  addCheck("Isolation", "stock winners", statusCount(oversell, 200) === 10, 10, statusCount(oversell, 200));
  addCheck("Isolation", "stock conflicts", statusCount(oversell, 409) === 10, 10, statusCount(oversell, 409));

  const duplicateConfirm = observationsFor("duplicate-confirm");
  addCheck("Isolation", "duplicate confirms are idempotent", statusCount(duplicateConfirm, 200) === 20, 20, statusCount(duplicateConfirm, 200));
  const duplicateCancel = observationsFor("duplicate-cancel");
  addCheck("Isolation", "duplicate cancels are idempotent", statusCount(duplicateCancel, 200) === 20, 20, statusCount(duplicateCancel, 200));

  const confirmCancel = observationsFor("confirm-vs-cancel");
  const cancel = confirmCancel.find((item) => item.operation === "cancel");
  const confirm = confirmCancel.find((item) => item.operation === "confirm");
  addCheck("Isolation", "concurrent cancel succeeded", cancel?.status === 200, 200, cancel?.status ?? null);
  addCheck("Isolation", "concurrent confirm was serialized", [200, 409].includes(confirm?.status), "200 or 409", confirm?.status ?? null);

  const atomicity = observationsFor("atomicity-confirm");
  addCheck("Atomicity", "multi-item confirmation was rejected", statusCount(atomicity, 409) === 1, 1, statusCount(atomicity, 409));
  const deadlock = observationsFor("deadlock-confirm");
  addCheck("Isolation", "ordered multi-product contention completed", statusCount(deadlock, 200) === 20, 20, statusCount(deadlock, 200));
}

function validateDatabaseState(state, phase) {
  const idempotencyOrders = state.orders.filter(
    (order) => order.idempotencyKey === fixtures.idempotencyKey,
  );
  const idempotencyResponseIds = new Set(
    observationsFor("idempotency-create").map((item) => item.orderId),
  );
  addCheck(phase, "one idempotent order persisted", idempotencyOrders.length === 1, 1, idempotencyOrders.length);
  addCheck(phase, "persisted id matches every response", idempotencyOrders.length === 1 && idempotencyResponseIds.size === 1 && idempotencyResponseIds.has(idempotencyOrders[0].id), idempotencyOrders[0]?.id ?? "one persisted id", [...idempotencyResponseIds]);

  const oversellStates = fixtures.oversellOrders.map((id) => orderState(state, id)?.status);
  addCheck(phase, "oversell stock is never negative", productStock(state, products.oversell) === 0, 0, productStock(state, products.oversell));
  addCheck(phase, "ten oversell orders confirmed", oversellStates.filter((status) => status === 2).length === 10, 10, oversellStates.filter((status) => status === 2).length);
  addCheck(phase, "ten oversell orders remained placed", oversellStates.filter((status) => status === 1).length === 10, 10, oversellStates.filter((status) => status === 1).length);

  addCheck(phase, "duplicate confirm debited once", productStock(state, products.duplicateConfirm) === 8, 8, productStock(state, products.duplicateConfirm));
  addCheck(phase, "duplicate confirm final state", orderState(state, fixtures.duplicateConfirmOrder)?.status === 2, 2, orderState(state, fixtures.duplicateConfirmOrder)?.status);
  addCheck(phase, "duplicate cancel restored once", productStock(state, products.duplicateCancel) === 10, 10, productStock(state, products.duplicateCancel));
  addCheck(phase, "duplicate cancel final state", orderState(state, fixtures.duplicateCancelOrder)?.status === 3, 3, orderState(state, fixtures.duplicateCancelOrder)?.status);

  addCheck(phase, "confirm/cancel stock is consistent", productStock(state, products.confirmCancel) === 10, 10, productStock(state, products.confirmCancel));
  addCheck(phase, "confirm/cancel final state is canceled", orderState(state, fixtures.confirmCancelOrder)?.status === 3, 3, orderState(state, fixtures.confirmCancelOrder)?.status);

  addCheck(phase, "atomic rollback preserved first product", productStock(state, products.atomicAvailable) === 5, 5, productStock(state, products.atomicAvailable));
  addCheck(phase, "atomic rollback preserved empty product", productStock(state, products.atomicEmpty) === 0, 0, productStock(state, products.atomicEmpty));
  addCheck(phase, "atomic rollback preserved order state", orderState(state, fixtures.atomicityOrder)?.status === 1, 1, orderState(state, fixtures.atomicityOrder)?.status);

  const deadlockStates = fixtures.deadlockOrders.map((id) => orderState(state, id)?.status);
  addCheck(phase, "multi-product stock A is exact", productStock(state, products.deadlockA) === 0, 0, productStock(state, products.deadlockA));
  addCheck(phase, "multi-product stock B is exact", productStock(state, products.deadlockB) === 0, 0, productStock(state, products.deadlockB));
  addCheck(phase, "all multi-product orders confirmed", deadlockStates.every((status) => status === 2), "all status 2", deadlockStates);

  addCheck(phase, "all fixture products remain non-negative", state.products.every((product) => product.stock >= 0), "all stock >= 0", state.products);
  addCheck(phase, "all order totals match their items", Number(state.invalidTotals) === 0, 0, Number(state.invalidTotals));
  addCheck(phase, "expected number of orders persisted", state.orders.length === 45, 45, state.orders.length);
}

async function verifyDurability(token) {
  if (process.env.CONSISTENCY_SKIP_RESTART === "1") {
    addCheck("Durability", "crash/restart validation", true, "executed unless CONSISTENCY_SKIP_RESTART=1", "skipped by configuration");
    databaseAfterRestart = databaseBeforeRestart;
    return;
  }

  await runCommand("docker", ["compose", "kill", "postgres"], { inherit: true });
  await runCommand("docker", ["compose", "start", "postgres"], { inherit: true });
  await runCommand("docker", ["compose", "restart", "api-1", "api-2", "nginx"], { inherit: true });
  await waitForReadiness();
  databaseAfterRestart = await readDatabaseState();
  addCheck("Durability", "database state survived PostgreSQL SIGKILL", isDeepStrictEqual(databaseAfterRestart, databaseBeforeRestart), databaseBeforeRestart, databaseAfterRestart);

  const idempotencyOrder = databaseAfterRestart.orders.find(
    (order) => order.idempotencyKey === fixtures.idempotencyKey,
  );
  const { response, body } = await apiRequest(`/orders/${idempotencyOrder?.id}`, {
    headers: { Authorization: `Bearer ${token}` },
  });
  addCheck("Durability", "committed order remains readable after restart", response.status === 200 && body?.id === idempotencyOrder?.id, idempotencyOrder?.id, { status: response.status, id: body?.id });
}

async function cleanupFixtures() {
  await sql(`
    DELETE FROM orders o
    WHERE EXISTS (
      SELECT 1 FROM order_items oi
      WHERE oi.order_id = o.id
        AND oi.product_id IN (${uuidList(productIds)})
    );
    DELETE FROM products WHERE id IN (${uuidList(productIds)});
  `);
}

async function writeReports(fatalError = null, fixturesRemoved = false) {
  const passed = !fatalError && checks.every((check) => check.passed);
  const summary = {
    runId,
    startedAgainst: baseUrl,
    passed,
    fatalError: fatalError ? String(fatalError.stack ?? fatalError) : null,
    gatlingExitCode,
    observations,
    databaseBeforeRestart,
    databaseAfterRestart,
    checks,
    fixturesPreserved: !fixturesRemoved,
  };
  await writeFile(resolve(reportDirectory, "summary.json"), `${JSON.stringify(summary, null, 2)}\n`);
  await writeFile(resolve(reportDirectory, "observations.json"), `${JSON.stringify(observations, null, 2)}\n`);
  if (databaseBeforeRestart) {
    await writeFile(resolve(reportDirectory, "database-state.json"), `${JSON.stringify({ beforeRestart: databaseBeforeRestart, afterRestart: databaseAfterRestart }, null, 2)}\n`);
  }

  const lines = [
    `# Distributed consistency report`,
    "",
    `- Run: \`${runId}\``,
    `- Target: \`${baseUrl}\``,
    `- Result: **${passed ? "PASS" : "FAIL"}**`,
    `- Gatling exit code: \`${gatlingExitCode}\``,
    `- Fixtures: ${fixturesRemoved ? "removed after successful validation" : "preserved for diagnosis"}`,
    "",
  ];
  if (fatalError) {
    lines.push("## Fatal error", "", "```text", String(fatalError.stack ?? fatalError), "```", "");
  }
  for (const category of [...new Set(checks.map((check) => check.category))]) {
    lines.push(`## ${category}`, "");
    for (const check of checks.filter((candidate) => candidate.category === category)) {
      lines.push(`- ${check.passed ? "PASS" : "FAIL"}: ${check.name}`);
      if (!check.passed) {
        lines.push(`  - Expected: \`${JSON.stringify(check.expected)}\``);
        lines.push(`  - Actual: \`${JSON.stringify(check.actual)}\``);
      }
    }
    lines.push("");
  }
  await writeFile(resolve(reportDirectory, "summary.md"), `${lines.join("\n")}\n`);

  console.log(`\nDistributed consistency: ${passed ? "PASS" : "FAIL"}`);
  console.log(`Report: ${resolve(reportDirectory, "summary.md")}`);
  for (const check of checks.filter((candidate) => !candidate.passed)) {
    console.error(`FAIL [${check.category}] ${check.name}`);
  }

  return passed;
}

let fatalError;
let passed = false;
try {
  console.log(`Consistency run ${runId}`);
  console.log(`Target: ${baseUrl}`);
  await waitForReadiness();
  const token = await authenticate();
  fixtures = await prepareFixtures(token);
  await startCollector();
  await runGatling(token);
  await stopCollector();
  validateHttpResults();
  databaseBeforeRestart = await readDatabaseState();
  validateDatabaseState(databaseBeforeRestart, "Consistency");
  await verifyDurability(token);
  if (databaseAfterRestart) {
    validateDatabaseState(databaseAfterRestart, "Post-restart consistency");
  }
} catch (error) {
  fatalError = error;
  console.error(error.stack ?? error);
} finally {
  try {
    await stopCollector();
  } catch (error) {
    fatalError ??= error;
  }

  let fixturesRemoved = false;
  if (!fatalError && checks.every((check) => check.passed)) {
    try {
      await cleanupFixtures();
      fixturesRemoved = true;
    } catch (error) {
      fatalError = new Error(`Fixture cleanup failed: ${error.message}`, { cause: error });
    }
  }
  passed = await writeReports(fatalError, fixturesRemoved);
}

process.exitCode = passed ? 0 : 1;
