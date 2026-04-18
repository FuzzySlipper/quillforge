import { execFileSync } from "node:child_process";
import { copyFileSync, existsSync, mkdirSync, readdirSync, rmSync } from "node:fs";
import { basename, dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = dirname(fileURLToPath(import.meta.url));
const desktopRoot = resolve(scriptDir, "..");
const bundleRoot = resolveBundleRoot();
const releaseDir = resolve(process.env.QUILLFORGE_DESKTOP_RELEASE_DIR ?? join(desktopRoot, "release-assets"));
const targetTriple =
  process.env.QUILLFORGE_DESKTOP_TARGET_TRIPLE ??
  process.env.QUILLFORGE_DESKTOP_HOST_TRIPLE ??
  detectHostTriple();

const assetPlans = new Map([
  [
    "x86_64-unknown-linux-gnu",
    [
      { type: "file", bundleDir: "deb", extension: ".deb", output: "QuillForge-debian-amd64.deb" },
      { type: "file", bundleDir: "rpm", extension: ".rpm", output: "QuillForge-fedora-x86_64.rpm" },
    ],
  ],
  [
    "x86_64-apple-darwin",
    [
      { type: "app", bundleDir: "macos", extension: ".app", output: "QuillForge-macos-x64.app.zip" },
      { type: "file", bundleDir: "dmg", extension: ".dmg", output: "QuillForge-macos-x64.dmg" },
    ],
  ],
  [
    "aarch64-apple-darwin",
    [
      { type: "app", bundleDir: "macos", extension: ".app", output: "QuillForge-macos-arm64.app.zip" },
      { type: "file", bundleDir: "dmg", extension: ".dmg", output: "QuillForge-macos-arm64.dmg" },
    ],
  ],
  [
    "x86_64-pc-windows-msvc",
    [
      { type: "file", bundleDir: "nsis", extension: ".exe", output: "QuillForge-windows-x64-setup.exe" },
    ],
  ],
]);

const plan = assetPlans.get(targetTriple);
if (!plan) {
  throw new Error(`No release asset plan is configured for target '${targetTriple}'.`);
}

const expectedBundles = new Set(
  (process.env.QUILLFORGE_DESKTOP_EXPECTED_BUNDLES ?? "")
    .split(",")
    .map((value) => value.trim())
    .filter(Boolean),
);
const selectedPlan =
  expectedBundles.size === 0
    ? plan
    : plan.filter((entry) => expectedBundles.has(entry.bundleDir) || expectedBundles.has(entry.type));

if (selectedPlan.length === 0) {
  throw new Error(`No release assets remain after applying QUILLFORGE_DESKTOP_EXPECTED_BUNDLES.`);
}

if (process.platform !== "darwin" && selectedPlan.some((entry) => entry.type === "app")) {
  throw new Error(
    "macOS .app staging must run on macOS. Remove 'app' from QUILLFORGE_DESKTOP_EXPECTED_BUNDLES or rerun the staging step on a Darwin host.",
  );
}

rmSync(releaseDir, { recursive: true, force: true });
mkdirSync(releaseDir, { recursive: true });

for (const entry of selectedPlan) {
  const bundleDir = join(bundleRoot, entry.bundleDir);
  if (!existsSync(bundleDir)) {
    throw new Error(`Expected bundle directory at ${bundleDir}`);
  }

  const sourcePath = findSingleBundle(bundleDir, entry.extension);
  const outputPath = join(releaseDir, entry.output);

  if (entry.type === "app") {
    zipAppBundle(sourcePath, outputPath);
  } else {
    copyFileSync(sourcePath, outputPath);
  }

  console.log(`Staged ${basename(outputPath)} from ${sourcePath}`);
}

function resolveBundleRoot() {
  const targetTriple =
    process.env.QUILLFORGE_DESKTOP_TARGET_TRIPLE ??
    process.env.QUILLFORGE_DESKTOP_HOST_TRIPLE ??
    detectHostTriple();
  const srcTauriRoot = join(desktopRoot, "src-tauri");
  const candidates = [
    join(srcTauriRoot, "target", targetTriple, "release", "bundle"),
    join(srcTauriRoot, "target", targetTriple, "debug", "bundle"),
    join(srcTauriRoot, "target", "release", "bundle"),
    join(srcTauriRoot, "target", "debug", "bundle"),
  ];

  for (const candidate of candidates) {
    if (existsSync(candidate)) {
      return candidate;
    }
  }

  return candidates[0];
}

function findSingleBundle(bundleDir, extension) {
  const matches = [];
  collectMatches(bundleDir, extension, matches);

  if (matches.length !== 1) {
    throw new Error(
      `Expected exactly one '${extension}' artifact in ${bundleDir}, found ${matches.length}: ${matches.join(", ")}`,
    );
  }

  return matches[0];
}

function collectMatches(currentDir, extension, matches) {
  for (const entry of readdirSync(currentDir, { withFileTypes: true })) {
    const entryPath = join(currentDir, entry.name);
    if (entry.name.endsWith(extension)) {
      matches.push(entryPath);
      continue;
    }

    if (entry.isDirectory()) {
      collectMatches(entryPath, extension, matches);
    }
  }
}

function zipAppBundle(sourcePath, outputPath) {
  if (process.platform !== "darwin") {
    throw new Error(`App bundle archiving requires macOS because it relies on 'ditto'.`);
  }

  execFileSync("ditto", ["-c", "-k", "--sequesterRsrc", "--keepParent", sourcePath, outputPath], {
    stdio: "inherit",
  });
}

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
