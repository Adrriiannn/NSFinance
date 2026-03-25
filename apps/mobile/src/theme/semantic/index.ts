import { gradients } from "../tokens/gradients";
import { shadows } from "../tokens/shadows";
import { darkTheme } from "./dark";
import { lightTheme } from "./light";

export type SemanticTheme = typeof darkTheme | typeof lightTheme;

export const themes = {
  light: lightTheme,
  dark: darkTheme
} as const;

export { gradients, shadows };
