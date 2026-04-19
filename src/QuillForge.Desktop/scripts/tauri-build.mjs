import { execFileSync } from "node:child_process";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const forwardedArgs = process.argv.slice(2);
const hasBundleOverride = forwardedArgs.includes("--bundles");
const bundleTargets = resolveBundleTargets(process.platform);
const args = ["build", ...forwardedArgs];
const tauriCli = resolveTauriCli();

if (!hasBundleOverride && bundleTargets.length > 0) {
  args.push("--bundles", bundleTargets.join(","));
}

execFileSync(process.execPath, [tauriCli, ...args], { stdio: "inherit" });

function resolveBundleTargets(platform) {
  switch (platform) {
    case "darwin":
      return ["app", "dmg"];
    case "win32":
      return ["nsis"];
    default:
      return ["deb"];
  }
}

function resolveTauriCli() {
  try {
    return require.resolve("@tauri-apps/cli/tauri.js");
  } catch (error) {
    throw new Error(
      "Unable to resolve the local Tauri CLI. Run 'npm install' in src/QuillForge.Desktop before building.",
      { cause: error },
    );
  }
}
