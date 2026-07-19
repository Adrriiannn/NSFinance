import assert from "node:assert/strict";
import { registerHooks } from "node:module";
import test from "node:test";

registerHooks({
  resolve(specifier, context, nextResolve) {
    if (specifier === "expo-secure-store") {
      return { shortCircuit: true, url: "test:expo-secure-store" };
    }

    if (specifier === "react-native") {
      return { shortCircuit: true, url: "test:react-native" };
    }

    return nextResolve(specifier, context);
  },
  load(url, context, nextLoad) {
    if (url === "test:expo-secure-store") {
      return {
        format: "module",
        shortCircuit: true,
        source: `
          export function getItem() { return null; }
          export async function setItemAsync() {}
        `
      };
    }

    if (url === "test:react-native") {
      return {
        format: "module",
        shortCircuit: true,
        source: 'export const Appearance = { getColorScheme: () => "dark" };'
      };
    }

    return nextLoad(url, context);
  }
});

const preferenceModule = import("./themePreference");

test("preference encoding round-trips every kind", async () => {
  const { encodeThemePreference, decodeThemePreference } = await preferenceModule;

  const cases = [
    { kind: "system" as const },
    { kind: "automatic" as const },
    { kind: "fixed" as const, themeId: "light" as const },
    { kind: "fixed" as const, themeId: "dark" as const }
  ];

  for (const preference of cases) {
    assert.deepEqual(decodeThemePreference(encodeThemePreference(preference)), preference);
  }
});

test("legacy stored values migrate without loss", async () => {
  const { decodeThemePreference } = await preferenceModule;

  assert.deepEqual(decodeThemePreference("light"), { kind: "fixed", themeId: "light" });
  assert.deepEqual(decodeThemePreference("dark"), { kind: "fixed", themeId: "dark" });
  assert.deepEqual(decodeThemePreference("system"), { kind: "system" });
  assert.deepEqual(decodeThemePreference("SYSTEM "), { kind: "system" });
  assert.deepEqual(decodeThemePreference(null), { kind: "system" });
  assert.deepEqual(decodeThemePreference("v2:fixed:not-a-pack"), { kind: "system" });
  assert.deepEqual(decodeThemePreference("v9:mystery"), { kind: "system" });
});

test("resolution honors fixed, system, and automatic kinds", async () => {
  const { resolveThemePackId } = await preferenceModule;

  assert.equal(resolveThemePackId({ kind: "fixed", themeId: "light" }, "dark"), "light");
  assert.equal(resolveThemePackId({ kind: "system" }, "light"), "light");
  assert.equal(resolveThemePackId({ kind: "system" }, "dark"), "dark");
  assert.equal(resolveThemePackId({ kind: "system" }, null), "dark");

  // Automatic resolves through the Irish calendar with the seasonal fallback:
  // high summer maps to the light base, Halloween week to the dark base.
  assert.equal(
    resolveThemePackId({ kind: "automatic" }, "dark", { year: 2026, month: 6, day: 15 }),
    "light"
  );
  assert.equal(
    resolveThemePackId({ kind: "automatic" }, "light", { year: 2026, month: 10, day: 28 }),
    "dark"
  );
  assert.equal(
    resolveThemePackId({ kind: "automatic" }, "light", { year: 2026, month: 12, day: 25 }),
    "dark"
  );
});

test("legacy adapter maps modes both ways", async () => {
  const { preferenceFromThemeMode, themeModeFromPreference } = await preferenceModule;

  assert.deepEqual(preferenceFromThemeMode("light"), { kind: "fixed", themeId: "light" });
  assert.deepEqual(preferenceFromThemeMode("system"), { kind: "system" });
  assert.equal(themeModeFromPreference({ kind: "fixed", themeId: "dark" }), "dark");
  assert.equal(themeModeFromPreference({ kind: "system" }), "system");
  assert.equal(themeModeFromPreference({ kind: "automatic" }), "system");
});
