import { useMemo } from "react";
import type { TextStyle, ViewStyle } from "react-native";
import type {
  SemanticButtonStateColors,
  SemanticButtonStates,
  SemanticButtonVariant
} from "../../../theme/semantic";
import { useThemeTokens } from "../../../theme/tokens";
import type { ButtonVisualState } from "./button.states";

export type ButtonVariant = SemanticButtonVariant;

type ButtonStatePreset = {
  container: ViewStyle;
  label: TextStyle;
  activityColor: string;
};

type ButtonPreset = {
  container: ViewStyle;
  label: TextStyle;
  states: Readonly<Record<ButtonVisualState, ButtonStatePreset>>;
  iconOnly?: boolean;
};

type ButtonPresetStateStyles = {
  buttonPresets: Record<ButtonVariant, ButtonPreset>;
  buttonStateStyles: {
    focused: ViewStyle;
    pressed: ViewStyle;
  };
};

function createButtonStatePreset(colors: SemanticButtonStateColors): ButtonStatePreset {
  return {
    container: {
      backgroundColor: colors.background,
      borderColor: colors.border
    },
    label: {
      color: colors.foreground
    },
    activityColor: colors.foreground
  };
}

function createButtonStatePresets(
  states: SemanticButtonStates
): Readonly<Record<ButtonVisualState, ButtonStatePreset>> {
  return {
    idle: createButtonStatePreset(states.idle),
    active: createButtonStatePreset(states.active),
    disabled: createButtonStatePreset(states.disabled),
    loading: createButtonStatePreset(states.loading)
  };
}

export function useButtonPresetStyles(): ButtonPresetStateStyles {
  const { borders, controls, radius, sizing, spacing, typography } = useThemeTokens();

  return useMemo(() => {
    const baseContainer: ViewStyle = {
      borderWidth: borders.width.thin,
      alignItems: "center",
      justifyContent: "center",
      flexDirection: "row",
      gap: spacing[8]
    };

    const textButtonContainer: ViewStyle = {
      ...baseContainer,
      paddingVertical: spacing[8]
    };

    const baseLabel: TextStyle = {
      ...typography.buttonLabel,
      textAlign: "center"
    };

    const standardMinHeight = Math.max(
      sizing.button.heights.standard,
      sizing.touchTarget.minimum
    );
    const compactMinHeight = Math.max(
      sizing.button.heights.compact,
      sizing.touchTarget.minimum
    );
    const pillActionMinHeight = Math.max(
      sizing.button.heights.pillAction,
      sizing.touchTarget.minimum
    );
    const iconSize = Math.max(sizing.button.heights.icon, sizing.touchTarget.minimum);

    const buttonPresets: Record<ButtonVariant, ButtonPreset> = {
      primary: {
        container: {
          ...textButtonContainer,
          minHeight: standardMinHeight,
          borderRadius: radius.medium,
          paddingHorizontal: sizing.button.horizontalPadding.standard
        },
        label: baseLabel,
        states: createButtonStatePresets(controls.button.primary)
      },
      secondary: {
        container: {
          ...textButtonContainer,
          minHeight: standardMinHeight,
          borderRadius: radius.medium,
          paddingHorizontal: sizing.button.horizontalPadding.standard
        },
        label: baseLabel,
        states: createButtonStatePresets(controls.button.secondary)
      },
      ghost: {
        container: {
          ...textButtonContainer,
          minHeight: standardMinHeight,
          borderRadius: radius.medium,
          paddingHorizontal: sizing.button.horizontalPadding.standard
        },
        label: baseLabel,
        states: createButtonStatePresets(controls.button.ghost)
      },
      destructive: {
        container: {
          ...textButtonContainer,
          minHeight: standardMinHeight,
          borderRadius: radius.medium,
          paddingHorizontal: sizing.button.horizontalPadding.standard
        },
        label: baseLabel,
        states: createButtonStatePresets(controls.button.destructive)
      },
      icon: {
        container: {
          ...baseContainer,
          width: iconSize,
          height: iconSize,
          borderRadius: radius.medium
        },
        label: baseLabel,
        states: createButtonStatePresets(controls.button.icon),
        iconOnly: true
      },
      compact: {
        container: {
          ...textButtonContainer,
          minHeight: compactMinHeight,
          borderRadius: radius.medium,
          paddingHorizontal: sizing.button.horizontalPadding.compact
        },
        label: {
          ...baseLabel,
          ...typography.caption,
          fontWeight: "500"
        },
        states: createButtonStatePresets(controls.button.compact)
      },
      pillAction: {
        container: {
          ...textButtonContainer,
          minHeight: pillActionMinHeight,
          borderRadius: radius.medium,
          paddingHorizontal: sizing.button.horizontalPadding.standard
        },
        label: baseLabel,
        states: createButtonStatePresets(controls.button.pillAction)
      }
    };

    const buttonStateStyles = {
      focused: {
        borderColor: controls.focusBorder,
        borderWidth: borders.width.focus
      } as ViewStyle,
      pressed: {
        transform: [{ scale: controls.pressedScale }]
      } as ViewStyle
    };

    return {
      buttonPresets,
      buttonStateStyles
    };
  }, [borders, controls, radius, sizing, spacing, typography]);
}
