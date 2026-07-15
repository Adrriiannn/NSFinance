import { gradients } from "../tokens/gradients";
import { shadows } from "../tokens/shadows";
import { darkTheme } from "./dark";
import { lightTheme } from "./light";
import type { SemanticTheme } from "./types";

export const themes = {
  light: lightTheme,
  dark: darkTheme
} as const satisfies Record<"light" | "dark", SemanticTheme>;

export { gradients, shadows };
export { semanticButtonStates, semanticButtonVariants } from "./types";
export type {
  SemanticButtonRoles,
  SemanticButtonState,
  SemanticButtonStateColors,
  SemanticButtonStates,
  SemanticButtonVariant,
  SemanticTheme
} from "./types";
