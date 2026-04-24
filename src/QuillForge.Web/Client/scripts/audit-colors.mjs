#!/usr/bin/env node
import { readdirSync, readFileSync, statSync } from "node:fs";
import { relative, resolve } from "node:path";

const repoRoot = resolve(import.meta.dirname, "../../../..");
const scanRoots = [
  "src/QuillForge.Web/Client/src",
  "src/QuillForge.Desktop/src",
  "src/QuillForge.Desktop/src-tauri/src",
];

const extensions = new Set([".css", ".ts", ".tsx", ".js", ".jsx"]);
const ignoredRelativePaths = new Set([
  // Intentionally user-selectable prose palettes / marketing tour art direction.
  "src/QuillForge.Web/Client/src/textTheme.ts",
  "src/QuillForge.Web/Client/src/story-tour.css",
  "src/QuillForge.Web/Client/src/StoryTourPage.tsx",
]);

const ignoredSegments = new Set([
  "node_modules",
  "dist",
  "wwwroot",
  "obj",
  "bin",
  ".sidecar-publish",
  "target",
]);

const defaultPalettes = [
  "slate",
  "gray",
  "zinc",
  "neutral",
  "stone",
  "red",
  "orange",
  "amber",
  "yellow",
  "lime",
  "green",
  "emerald",
  "teal",
  "cyan",
  "sky",
  "blue",
  "indigo",
  "violet",
  "purple",
  "fuchsia",
  "pink",
  "rose",
];
const utilityPrefixes = [
  "bg",
  "text",
  "border",
  "ring",
  "outline",
  "divide",
  "shadow",
  "from",
  "via",
  "to",
  "accent",
  "caret",
  "decoration",
  "placeholder",
  "fill",
  "stroke",
];

const paletteUtility = new RegExp(
  `\\b(?:${utilityPrefixes.join("|")})-(?:${defaultPalettes.join("|")})-\\d{2,3}(?:/\\d{1,3})?\\b|` +
    "\\b(?:bg|text|border|ring|shadow)-(?:white|black)(?:/\\d{1,3})?\\b",
  "g",
);
const hardcodedColor = /#[0-9A-Fa-f]{3,8}\b|\brgba?\(|\bhsla?\(|\[(?:#[0-9A-Fa-f]{3,8}|rgba?\(|hsla?\()/g;

// This audit catches direct UI color bypasses in app/desktop source. It intentionally
// does not validate CSS variable allowlists or token-mirroring arrays; keep those
// synchronized with Shared/Styles/quillforge-tokens.css when semantic aliases change.

function extensionOf(filePath) {
  const index = filePath.lastIndexOf(".");
  return index >= 0 ? filePath.slice(index) : "";
}

function normalizePath(filePath) {
  return filePath.replaceAll("\\", "/");
}

function* walk(dir) {
  for (const entry of readdirSync(dir)) {
    const fullPath = resolve(dir, entry);
    const rel = normalizePath(relative(repoRoot, fullPath));
    if (ignoredSegments.has(entry) || ignoredRelativePaths.has(rel)) {
      continue;
    }

    const stats = statSync(fullPath);
    if (stats.isDirectory()) {
      yield* walk(fullPath);
      continue;
    }

    if (stats.isFile() && extensions.has(extensionOf(entry))) {
      yield fullPath;
    }
  }
}

const findings = [];
for (const root of scanRoots) {
  const absoluteRoot = resolve(repoRoot, root);
  for (const filePath of walk(absoluteRoot)) {
    const rel = normalizePath(relative(repoRoot, filePath));
    const lines = readFileSync(filePath, "utf8").split(/\r?\n/);
    lines.forEach((line, index) => {
      const matches = [...line.matchAll(paletteUtility), ...line.matchAll(hardcodedColor)];
      if (matches.length === 0) {
        return;
      }

      findings.push({
        file: rel,
        line: index + 1,
        matches: [...new Set(matches.map((match) => match[0]))],
        text: line.trim(),
      });
    });
  }
}

if (findings.length > 0) {
  console.error("Found non-token color usages. Prefer --qf-* tokens or Tailwind semantic aliases such as bg-surface, text-danger, bg-info-soft, and text-accent-contrast.\n");
  for (const finding of findings) {
    console.error(`${finding.file}:${finding.line}: ${finding.matches.join(", ")}`);
    console.error(`  ${finding.text}`);
  }
  process.exit(1);
}

console.log("No non-token color usages found in audited app/desktop sources.");
console.log("Allowed outside this audit: shared token definitions, public SVG artwork, textTheme.ts prose palettes, and story-tour art direction.");
