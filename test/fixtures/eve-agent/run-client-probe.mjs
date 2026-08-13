import { once } from "node:events";
import { spawn } from "node:child_process";
import { readFile, rm } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";

const fixtureDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(fixtureDirectory, "..", "..", "..");
const eveBin = path.join(fixtureDirectory, "node_modules", "eve", "bin", "eve.js");
const eveVersion = JSON.parse(
  await readFile(
    path.join(fixtureDirectory, "node_modules", "eve", "package.json"),
    "utf8",
  ),
).version;
const probeProject = path.join(
  repositoryRoot,
  "tests",
  "NexusLabs.Eve.CompatibilityProbe",
  "NexusLabs.Eve.CompatibilityProbe.csproj",
);
const port = Number(process.env.EVE_TEST_PORT ?? "43125");
const baseUrl = `http://127.0.0.1:${port}`;
const output = [];

await rm(path.join(fixtureDirectory, ".eve"), { force: true, recursive: true });
await rm(path.join(fixtureDirectory, ".output"), { force: true, recursive: true });
await run(process.execPath, [eveBin, "build"], fixtureDirectory);

const server = spawn(
  process.execPath,
  [eveBin, "start", "--host", "127.0.0.1", "--port", String(port)],
  {
    cwd: fixtureDirectory,
    env: {
      ...process.env,
      NO_COLOR: "1",
    },
    stdio: ["ignore", "pipe", "pipe"],
  },
);

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.once(signal, () => {
    void stopProcess(server).finally(() => {
      process.exit(signal === "SIGINT" ? 130 : 143);
    });
  });
}

for (const stream of [server.stdout, server.stderr]) {
  stream.setEncoding("utf8");
  stream.on("data", (chunk) => {
    output.push(chunk);
    process.stdout.write(chunk);
  });
}

try {
  await waitForHealth(`${baseUrl}/eve/v1/health`, server, output);

  const probeArgs = [
    "run",
    "--project",
    probeProject,
    "--configuration",
    "Release",
  ];
  if (process.env.EVE_PROBE_NO_BUILD === "1") {
    probeArgs.push("--no-build");
  }
  probeArgs.push("--", baseUrl, eveVersion);

  const probe = spawn(
    "dotnet",
    probeArgs,
    {
      cwd: repositoryRoot,
      env: process.env,
      stdio: "inherit",
    },
  );

  const [code] = await once(probe, "exit");
  if (code !== 0) {
    throw new Error(`The C# compatibility probe exited with code ${code}.`);
  }
  console.log(`Eve ${eveVersion} compatibility probe passed.`);
} finally {
  await stopProcess(server);
}

async function waitForHealth(url, processHandle, logs) {
  const deadline = Date.now() + 90_000;

  while (Date.now() < deadline) {
    if (processHandle.exitCode !== null) {
      throw new Error(
        `The Eve fixture exited before becoming healthy.\n${logs.join("")}`,
      );
    }

    try {
      const response = await fetch(url);
      if (response.ok) {
        return;
      }
    } catch {
      // The server has not bound its port yet.
    }

    await new Promise((resolve) => setTimeout(resolve, 250));
  }

  throw new Error(`Timed out waiting for the Eve fixture.\n${logs.join("")}`);
}

async function stopProcess(processHandle) {
  if (processHandle.exitCode !== null) {
    return;
  }

  processHandle.kill();
  const deadline = Date.now() + 5_000;
  while (
    processHandle.exitCode === null &&
    processHandle.signalCode === null &&
    Date.now() < deadline
  ) {
    await new Promise((resolve) => setTimeout(resolve, 50));
  }

  if (processHandle.exitCode === null && processHandle.signalCode === null) {
    processHandle.kill("SIGKILL");
  }
}

async function run(command, args, cwd) {
  const chunks = [];
  const child = spawn(command, args, {
    cwd,
    env: process.env,
    stdio: ["ignore", "pipe", "pipe"],
  });
  for (const stream of [child.stdout, child.stderr]) {
    stream.setEncoding("utf8");
    stream.on("data", (chunk) => chunks.push(chunk));
  }
  const [code] = await once(child, "exit");
  if (code !== 0) {
    throw new Error(`${command} exited with code ${code}.\n${chunks.join("")}`);
  }
}
