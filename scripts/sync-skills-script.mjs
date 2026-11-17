// Generates skills.mjs from skills.mts, the agent-skill script the purview-buildsdk-justfile skill
// ships, and copies both into this repository's .build/just/.
//
// skills.mts is the source; skills.mjs is the fallback for a machine with Node but neither Bun nor
// Deno. Node's own type stripper does the work, so the output is exactly the source minus its types,
// with every comment kept. The stripper replaces each erased character with a space to keep
// positions, which reads badly, so the characters it erased are removed instead, along with a space
// left dangling before ',', ')' or ';' and any line erasing emptied.
//
//   just skills-script-sync    regenerate skills.mjs and copy both into .build/just/
//   node scripts/sync-skills-script.mjs --check
//                              exit 1 when skills.mjs is stale, writing nothing
//
// Plain JavaScript on purpose: it needs module.stripTypeScriptTypes (Node 22.13+), and exits 3 when
// that is missing so a caller can tell "cannot check here" from "stale".

import { copyFileSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import * as nodeModule from "node:module";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const assets = join(root, "skills", "purview-buildsdk-justfile", "assets", "just");
const modules = join(root, ".build", "just");
const source = join(assets, "skills.mts");
const output = join(assets, "skills.mjs");
const checkOnly = process.argv.includes("--check");

if (typeof nodeModule.stripTypeScriptTypes !== "function") {
	console.error(`module.stripTypeScriptTypes is not available in Node ${process.version}; Node 22.13 or later is needed.`);
	process.exit(3);
}

const header = [
	"// GENERATED from skills.mts by scripts/sync-skills-script.mjs in purview-dev/build-sdk - do not edit.",
	"// It is skills.mts with the TypeScript removed, for a machine with Node but neither Bun nor Deno.",
	"",
];

export function generate(typescript) {
	const text = typescript.replace(/\r\n/g, "\n");
	const stripped = nodeModule.stripTypeScriptTypes(text, { mode: "strip" });
	if (stripped.length !== text.length) throw new Error("The type stripper changed the text length; positions cannot be compared.");

	const lines = [];
	let start = 0;
	for (const sourceLine of text.split("\n")) {
		const strippedLine = stripped.slice(start, start + sourceLine.length);
		start += sourceLine.length + 1;

		// A character is erased when the stripper blanked it. A space is erased too when it sits inside an
		// erased run - the one in ': string' - so 'name: string = x' becomes 'name = x', not 'name  = x'.
		const erased = [...sourceLine].map((character, index) => strippedLine[index] === " " && character !== " ");
		const nearest = (index, step) => {
			for (let at = index + step; at >= 0 && at < sourceLine.length; at += step) {
				if (sourceLine[at] !== " ") return at;
			}
			return -1;
		};
		for (let index = 0; index < sourceLine.length; index++) {
			if (sourceLine[index] !== " ") continue;
			const before = nearest(index, -1);
			const after = nearest(index, 1);
			if (before >= 0 && after >= 0 && erased[before] && erased[after]) erased[index] = true;
		}

		let line = "";
		let erasedBefore = false;
		for (let index = 0; index < sourceLine.length; index++) {
			if (erased[index]) {
				erasedBefore = true;
				continue;
			}

			// A space the erasure left in front of punctuation: 'value as Type)' becomes 'value)'.
			if (erasedBefore && /[,);\]]/.test(strippedLine[index])) line = line.replace(/ +$/, "");
			if (strippedLine[index] !== " ") erasedBefore = false;
			line += strippedLine[index];
		}

		line = line.replace(/\s+$/, "");
		// A line that held only types - a type alias, an interface member - goes entirely.
		if (line.length === 0 && sourceLine.trim().length > 0) continue;
		lines.push(line);
	}

	// Erased declarations can leave blank lines next to each other; keep one.
	const collapsed = lines.filter((line, index) => !(line === "" && lines[index - 1] === ""));
	return [...header, ...collapsed].join("\n").replace(/\n*$/, "\n");
}

const expected = generate(readFileSync(source, "utf8"));
const current = (() => {
	try {
		return readFileSync(output, "utf8").replace(/\r\n/g, "\n");
	} catch {
		return "";
	}
})();

if (checkOnly) {
	if (current !== expected) {
		console.error("skills.mjs is out of date with skills.mts. Run: just skills-script-sync");
		process.exit(1);
	}

	console.log("skills.mjs matches skills.mts.");
	process.exit(0);
}

if (current !== expected) writeFileSync(output, expected);
for (const name of ["skills.mts", "skills.mjs"]) copyFileSync(join(assets, name), join(modules, name));
console.log("skills.mjs regenerated; skills.mts and skills.mjs copied to .build/just/.");
