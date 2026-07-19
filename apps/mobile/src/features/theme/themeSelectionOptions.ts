import { themePacks, type ThemePackId } from "../../theme/runtime/themePacks";
import type { ThemePreference } from "../../theme/runtime/themePreference";

// Pure derivation for the theme picker (THEME-002): one ordered option list -
// System, the fixed packs from the registry (base appearances first), then
// Automatic. Future seasonal packs appear automatically once registered.

export type ThemeSelectionOption = {
  id: string;
  label: string;
  description: string;
  preference: ThemePreference;
  selected: boolean;
};

const BASE_PACK_ORDER: ThemePackId[] = ["light", "dark"];

function isSamePreference(left: ThemePreference, right: ThemePreference): boolean {
  if (left.kind !== right.kind) {
    return false;
  }

  if (left.kind === "fixed" && right.kind === "fixed") {
    return left.themeId === right.themeId;
  }

  return true;
}

export function buildThemeSelectionOptions(current: ThemePreference): ThemeSelectionOption[] {
  const packIds = Object.keys(themePacks) as ThemePackId[];
  const orderedPackIds = [
    ...BASE_PACK_ORDER.filter((id) => packIds.includes(id)),
    ...packIds.filter((id) => !BASE_PACK_ORDER.includes(id))
  ];

  const options: ThemeSelectionOption[] = [
    {
      id: "system",
      label: "System",
      description: "Follow your phone's appearance",
      preference: { kind: "system" },
      selected: isSamePreference(current, { kind: "system" })
    },
    ...orderedPackIds.map((packId) => {
      const pack = themePacks[packId];
      const preference: ThemePreference = { kind: "fixed", themeId: packId };
      const isBaseAppearancePack = packId === "light" || packId === "dark";
      return {
        id: `fixed:${packId}`,
        label: pack.displayName,
        description: isBaseAppearancePack
          ? `Always use the ${pack.displayName.toLowerCase()} theme`
          : `Pin the ${pack.displayName} theme`,
        preference,
        selected: isSamePreference(current, preference)
      };
    }),
    {
      id: "automatic",
      label: "Automatic",
      description: "Rotates with Ireland's seasons and holidays",
      preference: { kind: "automatic" },
      selected: isSamePreference(current, { kind: "automatic" })
    }
  ];

  return options;
}

export function describeCurrentThemeSelection(preference: ThemePreference): string {
  switch (preference.kind) {
    case "system":
      return "System";
    case "automatic":
      return "Automatic";
    case "fixed":
      return themePacks[preference.themeId]?.displayName ?? "System";
  }
}
