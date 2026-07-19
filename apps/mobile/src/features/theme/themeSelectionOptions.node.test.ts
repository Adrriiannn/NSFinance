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
        source: "export function getItem() { return null; } export async function setItemAsync() {}"
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

const optionsModule = import("./themeSelectionOptions");

test("options list System first, packs in base order, Automatic last", async () => {
  const { buildThemeSelectionOptions } = await optionsModule;
  const options = buildThemeSelectionOptions({ kind: "system" });

  assert.deepEqual(
    options.map((option) => option.id),
    [
      "system",
      "fixed:light",
      "fixed:dark",
      "fixed:spring",
      "fixed:summer",
      "fixed:autumn",
      "fixed:winter",
      "automatic"
    ]
  );
  assert.equal(options[0]?.selected, true);
  assert.equal(options.filter((option) => option.selected).length, 1);
});

test("exactly one option is selected for every preference kind", async () => {
  const { buildThemeSelectionOptions } = await optionsModule;

  const fixedDark = buildThemeSelectionOptions({ kind: "fixed", themeId: "dark" });
  assert.equal(fixedDark.find((option) => option.selected)?.id, "fixed:dark");

  const automatic = buildThemeSelectionOptions({ kind: "automatic" });
  assert.equal(automatic.find((option) => option.selected)?.id, "automatic");
});

test("current selection labels are human-readable", async () => {
  const { describeCurrentThemeSelection } = await optionsModule;

  assert.equal(describeCurrentThemeSelection({ kind: "system" }), "System");
  assert.equal(describeCurrentThemeSelection({ kind: "automatic" }), "Automatic");
  assert.equal(describeCurrentThemeSelection({ kind: "fixed", themeId: "light" }), "Light");
  assert.equal(describeCurrentThemeSelection({ kind: "fixed", themeId: "dark" }), "Dark");
});
