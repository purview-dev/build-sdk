// Rewrites the Purview.BuildSdk version pins that an adopter copies by hand - the quick-start snippets
// and the starter template - to the version in package.json, the single source of truth.
//
// Only text that is copied verbatim lives here. Everything that is packed is stamped at pack time
// instead (see StampPurviewPackedVersionTarget and StampPurviewEditorConfigVersionTarget in
// BuildSdk.csproj), so it never needs syncing. PackagedVersionFallbackTests fails the build if a file
// listed here drifts, and names this script as the fix.
//
//   just version-sync

import { readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";

const root = join(import.meta.dir, "..");
const { version } = JSON.parse(readFileSync(join(root, "package.json"), "utf8"));

const files = ["README.md", "docs/wiki/Getting-Started.md", "templates/global.json"];

const replacements: [RegExp, string][] = [
	// The global.json pin. A '$(...)' property reference documents auto-detection and is not a pin.
	[/("Purview\.BuildSdk"\s*:\s*")(?!\$)[^"]+(")/g, `$1${version}$2`],
	// The example marker line of a bootstrapped .editorconfig.
	[/(# Purview\.BuildSdk )\S+( - managed file)/g, `$1${version}$2`],
];

let changed = 0;

for (const file of files) {
	const path = join(root, file);
	const before = readFileSync(path, "utf8");
	const after = replacements.reduce((text, [pattern, replacement]) => text.replace(pattern, replacement), before);

	if (after !== before) {
		writeFileSync(path, after);
		changed++;
		console.log(`  updated ${file}`);
	}
}

console.log(changed === 0 ? `All pins already at ${version}.` : `Pinned ${changed} file(s) to ${version}.`);
