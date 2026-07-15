import type { SemanticButtonState } from "../../../theme/semantic";

export const BUTTON_STATE_PRECEDENCE = [
  "loading",
  "disabled",
  "active",
  "idle"
] as const satisfies readonly SemanticButtonState[];

export type ButtonVisualState = SemanticButtonState;

type ButtonStateInput = {
  isLoading?: boolean;
  isDisabled?: boolean;
  isFocused?: boolean;
  isPressed?: boolean;
};

export function resolveButtonVisualState({
  isLoading = false,
  isDisabled = false,
  isFocused = false,
  isPressed = false
}: ButtonStateInput): ButtonVisualState {
  if (isLoading) {
    return "loading";
  }

  if (isDisabled) {
    return "disabled";
  }

  if (isFocused || isPressed) {
    return "active";
  }

  return "idle";
}
