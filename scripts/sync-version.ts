// Rewrites the Purview.BuildSdk version pins that an adopter copies by hand - the quick-start snippets,
// the starter template, and this repository's own .editorconfig marker - to the version in package.json,
// the single source of truth.
//
// Only text that is copied verbatim lives here. Everything that is packed is stamped at pack time
// instead (see StampPurviewPackedVersionTarget and StampPurviewEditorConfigVersionTarget in
// BuildSdk.csproj), so it never needs syncing. PackagedVersionFallbackTests fails the build if a file
// listed here drifts, and names this script as the fix.
//
//   just version-sync         rewrite every stale pin
//   just version-sync-check   report drift and exit non-zero, writing nothing
//
// CHANGELOG.md is deliberately absent. Its marker lines are history and have to keep the version they
// shipped with; only the first line of .editorconfig is live, and it carries the marker exactly once.

import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";

const root = join(import.meta.dir, "..");
const { version } = JSON.parse(readFileSync(join(root, "package.json"), "utf8"));
const checkOnly = process.argv.includes("--check");

const files = ["README.md", "docs/wiki/Getting-Started.md", "templates/global.json", ".editorconfig"];

const replacements: [RegExp, string][] = [
	// The global.json pin. A '$(...)' property reference documents auto-detection and is not a pin.
	[/("Purview\.BuildSdk"\s*:\s*")(?!\$)[^"]+(")/g, `$1${version}$2`],
	// The example marker line of a bootstrapped .editorconfig, and the live one in this repository's own.
	[/(# Purview\.BuildSdk )\S+( - managed file)/g, `$1${version}$2`],
];

let changed = 0;
const stale: string[] = [];

for (const file of files) {
	const path = join(root, file);
	const before = readFileSync(path, "utf8");
	const after = replacements.reduce((text, [pattern, replacement]) => text.replace(pattern, replacement), before);

	if (after === before) {
		continue;
	}

	if (checkOnly) {
		stale.push(file);
		continue;
	}

	writeFileSync(path, after);
	changed++;
	console.log(`  updated ${file}`);
}

if (checkOnly) {
	if (stale.length > 0) {
		console.error(`  stale at ${version}: ${stale.join(", ")}`);
		console.error("Run `just version-sync` to update them.");
		process.exit(1);
	}

	console.log(`All pins already at ${version}.`);
	process.exit(0);
}

console.log(changed === 0 ? `All pins already at ${version}.` : `Pinned ${changed} file(s) to ${version}.`);
