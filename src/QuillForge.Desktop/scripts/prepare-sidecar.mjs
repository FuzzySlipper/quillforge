import { execFileSync } from "node:child_process";
import { chmodSync, copyFileSync, cpSync, existsSync, mkdirSync, rmSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = dirname(fileURLToPath(import.meta.url));
const desktopRoot = resolve(scriptDir, "..");
const repoRoot = resolve(desktopRoot, "..", "..");
const webProjectRoot = resolve(repoRoot, "src", "QuillForge.Web");
const sourceProject = resolve(webProjectRoot, "QuillForge.Web.csproj");
const binariesDir = resolve(desktopRoot, "src-tauri", "binaries");
const resourcesDir = resolve(desktopRoot, "src-tauri", "resources", "backend-payload");
const publishRoot = resolve(desktopRoot, ".sidecar-publish");

const tripleToRid = new Map([
  ["x86_64-unknown-linux-gnu", "linux-x64"],
  ["aarch64-unknown-linux-gnu", "linux-arm64"],
  ["x86_64-apple-darwin", "osx-x64"],
  ["aarch64-apple-darwin", "osx-arm64"],
  ["x86_64-pc-windows-msvc", "win-x64"],
  ["aarch64-pc-windows-msvc", "win-arm64"],
]);

const hostTriple = process.env.QUILLFORGE_DESKTOP_HOST_TRIPLE ?? detectHostTriple();
const runtimeIdentifier = process.env.QUILLFORGE_DESKTOP_SIDECAR_RID ?? tripleToRid.get(hostTriple);

if (!runtimeIdentifier) {
  throw new Error(`Unsupported host triple '${hostTriple}'. Set QUILLFORGE_DESKTOP_SIDECAR_RID explicitly.`);
}

const isWindows = runtimeIdentifier.startsWith("win-");
const publishedExeName = isWindows ? "QuillForge.Web.exe" : "QuillForge.Web";
const targetExeName = `quillforge-backend-${hostTriple}${isWindows ? ".exe" : ""}`;
const publishDir = join(publishRoot, hostTriple);
const publishedExe = join(publishDir, publishedExeName);
const bundledSidecar = join(binariesDir, targetExeName);
const bundledPayloadDir = join(resourcesDir, hostTriple);
const releaseObjDir = join(webProjectRoot, "obj", "Release", "net10.0");
const releaseBinDir = join(webProjectRoot, "bin", "Release", "net10.0");

mkdirSync(binariesDir, { recursive: true });
mkdirSync(resourcesDir, { recursive: true });
rmSync(publishDir, { recursive: true, force: true });
rmSync(releaseObjDir, { recursive: true, force: true });
rmSync(releaseBinDir, { recursive: true, force: true });
mkdirSync(publishDir, { recursive: true });

console.log(`Preparing QuillForge backend sidecar for ${hostTriple} (${runtimeIdentifier})...`);

execFileSync(
  "dotnet",
  [
    "publish",
    sourceProject,
    "-c",
    "Release",
    "-r",
    runtimeIdentifier,
    "--self-contained",
    "true",
    "-p:PublishSingleFile=true",
    "-p:PublishTrimmed=false",
    "-p:AllowMissingPrunePackageData=true",
    "-o",
    publishDir,
  ],
  {
    cwd: repoRoot,
    stdio: "inherit",
  },
);

if (!existsSync(publishedExe)) {
  throw new Error(`Expected published backend executable at ${publishedExe}`);
}

copyFileSync(publishedExe, bundledSidecar);
rmSync(bundledPayloadDir, { recursive: true, force: true });
cpSync(publishDir, bundledPayloadDir, { recursive: true });
for (const staleArtifact of [
  publishedExeName,
  "QuillForge.Web.pdb",
  "QuillForge.Core.pdb",
  "QuillForge.Storage.pdb",
  "QuillForge.Providers.pdb",
  "Den.Persistence.pdb",
  "Client",
]) {
  rmSync(join(bundledPayloadDir, staleArtifact), { recursive: true, force: true });
}
if (!isWindows) {
  chmodSync(bundledSidecar, 0o755);
}

console.log(`Bundled sidecar ready at ${bundledSidecar}`);
console.log(`Bundled backend payload ready at ${bundledPayloadDir}`);

function detectHostTriple() {
  const output = execFileSync("rustc", ["-vV"], { encoding: "utf8" });
  const line = output
    .split("\n")
    .find((entry) => entry.toLowerCase().startsWith("host:"));

  if (!line) {
    throw new Error("Unable to detect Rust host triple from 'rustc -vV'.");
  }

  return line.split(":")[1].trim();
}
