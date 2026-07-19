import { darkTheme } from "./dark";
import { lightTheme } from "./light";
import type { SemanticTheme } from "./types";

// Seasonal colorized themes (THEME-001, first wave). Each derives from a base
// appearance and recolors only accent-driven paths: canvas tint, accent
// borders, primary/ghost action states, and accent tokens. Financial semantics
// (money, status), text, surfaces, and every other control state inherit from
// the governed base so seasonal dressing can never change financial meaning.

type SeasonalAccentParams = {
  name: string;
  base: SemanticTheme;
  accent: string;
  accentStrong: string;
  accentGlow: string;
  canvas?: string;
  elevatedCanvas?: string;
};

function withAlpha(hexColor: string, alpha: number): string {
  const normalized = hexColor.replace("#", "");
  const red = Number.parseInt(normalized.slice(0, 2), 16);
  const green = Number.parseInt(normalized.slice(2, 4), 16);
  const blue = Number.parseInt(normalized.slice(4, 6), 16);
  return `rgba(${red}, ${green}, ${blue}, ${alpha})`;
}

function deriveSeasonalTheme(params: SeasonalAccentParams): SemanticTheme {
  const { base } = params;
  const onPrimary = base.colors.onAction.primary;
  const primaryDisabled = base.colors.action.button.primary.disabled;
  const ghostDisabled = base.colors.action.button.ghost.disabled;

  const primaryButtonStates = {
    idle: {
      background: params.accent,
      border: params.accent,
      foreground: onPrimary
    },
    active: {
      background: params.accentStrong,
      border: params.accentStrong,
      foreground: onPrimary
    },
    disabled: primaryDisabled,
    loading: {
      background: params.accent,
      border: params.accent,
      foreground: onPrimary
    }
  };

  const ghostButtonStates = {
    idle: {
      background: "transparent",
      border: "transparent",
      foreground: params.accent
    },
    active: {
      background: withAlpha(params.accent, 0.12),
      border: withAlpha(params.accent, 0.24),
      foreground: params.accent
    },
    disabled: ghostDisabled,
    loading: {
      background: "transparent",
      border: "transparent",
      foreground: params.accent
    }
  };

  const secondaryButtonStates = {
    ...base.colors.action.button.secondary,
    active: {
      ...base.colors.action.button.secondary.active,
      border: params.accent
    }
  };

  return {
    name: params.name,
    isDark: base.isDark,
    colors: {
      ...base.colors,
      canvas: params.canvas ?? base.colors.canvas,
      elevatedCanvas: params.elevatedCanvas ?? base.colors.elevatedCanvas,
      border: {
        ...base.colors.border,
        subtle: withAlpha(params.accent, base.isDark ? 0.26 : 0.14),
        strong: withAlpha(params.accent, base.isDark ? 0.44 : 0.36)
      },
      action: {
        ...base.colors.action,
        primary: params.accent,
        primaryStrong: params.accentStrong,
        primaryGlow: params.accentGlow,
        button: {
          ...base.colors.action.button,
          primary: primaryButtonStates,
          secondary: secondaryButtonStates,
          ghost: ghostButtonStates,
          icon: secondaryButtonStates,
          compact: secondaryButtonStates,
          pillAction: secondaryButtonStates
        }
      },
      onAction: {
        ...base.colors.onAction,
        ghost: params.accent
      },
      accent: {
        primary: params.accent,
        primaryStrong: params.accentStrong,
        cyan: params.accentGlow,
        amber: params.accentGlow
      }
    }
  };
}

// Spring: fresh meadow green on the warm light base.
export const springTheme = deriveSeasonalTheme({
  name: "spring",
  base: lightTheme,
  accent: "#3E7B27",
  accentStrong: "#33661F",
  accentGlow: "#4E9431",
  canvas: "#F4F7F0",
  elevatedCanvas: "#EEF3E8"
});

// Summer: Atlantic teal, long bright days on the light base.
export const summerTheme = deriveSeasonalTheme({
  name: "summer",
  base: lightTheme,
  accent: "#0F7173",
  accentStrong: "#0B5B5D",
  accentGlow: "#14898C",
  canvas: "#F2F7F6",
  elevatedCanvas: "#EAF2F1"
});

// Autumn: harvest amber over a warm ember dark base.
export const autumnTheme = deriveSeasonalTheme({
  name: "autumn",
  base: darkTheme,
  accent: "#D97B29",
  accentStrong: "#C56A1B",
  accentGlow: "#E68C3D",
  canvas: "#171008",
  elevatedCanvas: "#1E1710"
});

// Winter: frost blue over a cold night dark base.
export const winterTheme = deriveSeasonalTheme({
  name: "winter",
  base: darkTheme,
  accent: "#7FB3D5",
  accentStrong: "#6AA3C8",
  accentGlow: "#93C2DF",
  canvas: "#0C1218",
  elevatedCanvas: "#111A22"
});
