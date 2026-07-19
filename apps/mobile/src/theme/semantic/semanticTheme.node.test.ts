import assert from "node:assert/strict";
import test from "node:test";
import {
  semanticButtonStates,
  semanticButtonVariants,
  themes,
  type SemanticTheme
} from "./index";
import { autumnTheme, springTheme, summerTheme, winterTheme } from "./seasonalThemes";

// Every selectable pack theme is held to the same contracts as the bases.
const themeList: readonly SemanticTheme[] = [
  ...Object.values(themes),
  springTheme,
  summerTheme,
  autumnTheme,
  winterTheme
];

function collectLeafPaths(value: unknown, prefix = ""): string[] {
  if (typeof value !== "object" || value === null) {
    return [prefix];
  }

  return Object.entries(value as Record<string, unknown>)
    .flatMap(([key, child]) => collectLeafPaths(child, prefix ? `${prefix}.${key}` : key))
    .sort();
}

function relativeLuminance(color: string): number {
  assert.match(color, /^#[0-9A-F]{6}$/i);
  const channels = [1, 3, 5].map((start) => Number.parseInt(color.slice(start, start + 2), 16) / 255);
  const linear = channels.map((channel) =>
    channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4
  );

  return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2];
}

function contrastRatio(first: string, second: string): number {
  const firstLuminance = relativeLuminance(first);
  const secondLuminance = relativeLuminance(second);
  const lighter = Math.max(firstLuminance, secondLuminance);
  const darker = Math.min(firstLuminance, secondLuminance);

  return (lighter + 0.05) / (darker + 0.05);
}

test("light and dark themes expose the same complete semantic shape", () => {
  assert.deepEqual(
    collectLeafPaths(themes.light.colors),
    collectLeafPaths(themes.dark.colors)
  );

  for (const theme of themeList) {
    assert.deepEqual(
      collectLeafPaths(theme.colors),
      collectLeafPaths(themes.light.colors),
      `${theme.name} must expose the complete semantic shape`
    );
  }

  for (const theme of themeList) {
    assert.deepEqual(
      Object.keys(theme.colors.action.button).sort(),
      [...semanticButtonVariants].sort()
    );

    for (const variant of semanticButtonVariants) {
      const role = theme.colors.action.button[variant];
      assert.deepEqual(Object.keys(role).sort(), [...semanticButtonStates].sort());

      for (const state of semanticButtonStates) {
        for (const value of Object.values(role[state])) {
          assert.equal(typeof value, "string");
          assert.notEqual(value.trim(), "");
        }
      }
    }
  }
});

test("primary action labels and loading indicators meet the contrast contract", () => {
  const contrastStates = ["idle", "active", "loading"] as const;

  for (const theme of themeList) {
    for (const state of contrastStates) {
      const stateColors = theme.colors.action.button.primary[state];
      const ratio = contrastRatio(stateColors.foreground, stateColors.background);

      assert.equal(stateColors.foreground, theme.colors.onAction.primary);
      assert.ok(
        ratio >= 4.5,
        `${theme.name} primary ${state} contrast was ${ratio.toFixed(2)}:1`
      );
    }

    assert.notDeepEqual(
      theme.colors.action.button.primary.disabled,
      theme.colors.action.button.primary.idle
    );
  }
});

test("focus borders remain visible against every supported surface", () => {
  for (const theme of themeList) {
    const surfaces = [
      theme.colors.canvas,
      theme.colors.surface.level0,
      theme.colors.surface.level1,
      theme.colors.surface.level2
    ];

    for (const surface of surfaces) {
      const ratio = contrastRatio(theme.colors.border.focus, surface);
      assert.ok(
        ratio >= 3,
        `${theme.name} focus border contrast was ${ratio.toFixed(2)}:1`
      );
    }

    assert.notEqual(
      theme.colors.border.focus,
      theme.colors.action.button.primary.idle.border
    );
  }
});
