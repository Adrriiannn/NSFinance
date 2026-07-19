import assert from "node:assert/strict";
import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const mobileRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");

const RETIRED_PRIMITIVE_FILES = [
  ["src", "components", "ui", "PrimaryButton.tsx"],
  ["src", "components", "ui", "SecondaryButton.tsx"],
  ["src", "components", "ui", "TertiaryButton.tsx"],
  ["src", "components", "ui", "IconButton.tsx"],
  ["src", "components", "ui", "TextField.tsx"],
  ["src", "components", "ui", "PasswordField.tsx"],
  ["src", "components", "ui", "Chip.tsx"],
  ["src", "components", "ui", "GlassCard.tsx"],
  ["src", "components", "ui", "EmptyState.tsx"],
  ["src", "components", "ui", "SkeletonBlock.tsx"],
  ["src", "components", "ui", "FloatingActionButton.tsx"],
  ["src", "components", "Card.tsx"]
] as const;

const RETIRED_IMPORT_SPECIFIERS = [
  "ui/PrimaryButton",
  "ui/SecondaryButton",
  "ui/TertiaryButton",
  'ui/IconButton"',
  'ui/TextField"',
  'ui/PasswordField"',
  'ui/Chip"',
  "ui/GlassCard",
  'ui/EmptyState"',
  "ui/SkeletonBlock"
] as const;

function collectSourceFiles(root: string): string[] {
  const collected: string[] = [];
  const stack = [root];

  while (stack.length > 0) {
    const current = stack.pop();

    if (!current) {
      continue;
    }

    for (const entry of readdirSync(current)) {
      const fullPath = join(current, entry);
      const stats = statSync(fullPath);

      if (stats.isDirectory()) {
        if (entry === "node_modules" || entry.startsWith(".")) {
          continue;
        }

        stack.push(fullPath);
        continue;
      }

      if (/\.(ts|tsx)$/.test(entry) && !entry.endsWith(".d.ts")) {
        collected.push(fullPath);
      }
    }
  }

  return collected;
}

test("retired legacy primitives do not return as files", () => {
  for (const segments of RETIRED_PRIMITIVE_FILES) {
    const target = join(mobileRoot, ...segments);
    assert.equal(
      existsSync(target),
      false,
      `${segments.join("/")} was retired in the primitive consolidation and must not be reintroduced`
    );
  }
});

test("no source file imports a retired legacy primitive module", () => {
  const sourceRoots = [join(mobileRoot, "app"), join(mobileRoot, "src")];
  const offenders: string[] = [];

  for (const root of sourceRoots) {
    for (const file of collectSourceFiles(root)) {
      const source = readFileSync(file, "utf8");

      for (const specifier of RETIRED_IMPORT_SPECIFIERS) {
        if (source.includes(`${specifier.endsWith('"') ? specifier.slice(0, -1) : specifier}"`) && source.includes("import")) {
          const importPattern = new RegExp(
            `from\\s+"[^"]*${specifier.endsWith('"') ? specifier.slice(0, -1) : specifier}"`
          );

          if (importPattern.test(source)) {
            offenders.push(`${file} -> ${specifier}`);
          }
        }
      }
    }
  }

  assert.deepEqual(
    offenders,
    [],
    `retired primitive imports found:\n${offenders.join("\n")}`
  );
});
