import { execFileSync } from "node:child_process";

const forwardedArgs = process.argv.slice(2);
const hasBundleOverride = forwardedArgs.includes("--bundles");
const bundleTargets = resolveBundleTargets(process.platform);
const args = ["tauri", "build", ...forwardedArgs];

if (!hasBundleOverride && bundleTargets.length > 0) {
  args.push("--bundles", bundleTargets.join(","));
}

execFileSync("npx", args, { stdio: "inherit" });

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
