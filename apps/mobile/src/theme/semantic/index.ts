import { gradients } from "../tokens/gradients";
import { shadows } from "../tokens/shadows";
import { darkTheme } from "./dark";
import { lightTheme } from "./light";

export type SemanticTheme = typeof darkTheme;

export const themes = {
  light: lightTheme,
  dark: darkTheme
} as const;

export const activeTheme = darkTheme;

export { gradients, shadows };
