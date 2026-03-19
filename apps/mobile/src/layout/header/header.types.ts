import type { ReactNode } from "react";
import type { StyleProp, TextInputProps, ViewStyle } from "react-native";

export type HeaderPresetName =
  | "primaryDefault"
  | "primaryGreeting"
  | "primaryTwoRowSelector"
  | "primaryTwoRowSearch"
  | "secondaryDetail";

export type HeaderTitleMode = "centered" | "leading";
export type HeaderLeadingKind = "menu" | "back";
export type HeaderTitleVariant = "default" | "greeting";

export type HeaderPresetConfig = {
  name: HeaderPresetName;
  leading: HeaderLeadingKind;
  titleMode: HeaderTitleMode;
  titleVariant: HeaderTitleVariant;
  hasSecondRow: boolean;
  preserveTrailingSlot: boolean;
};

export type HeaderShellProps = {
  preset: HeaderPresetName;
  title: string;
  subtitle?: string;
  includeTopInset?: boolean;
  elevated?: boolean;
  leadingAction?: ReactNode;
  trailingAction?: ReactNode;
  secondRow?: ReactNode;
  style?: StyleProp<ViewStyle>;
  contentStyle?: StyleProp<ViewStyle>;
  hideDivider?: boolean;
};

export type HeaderRowProps = {
  children: ReactNode;
  height: number;
  style?: StyleProp<ViewStyle>;
};

export type HeaderTitleBlockProps = {
  title: string;
  subtitle?: string;
  mode: HeaderTitleMode;
  variant: HeaderTitleVariant;
};

export type HeaderActionButtonProps = {
  icon?: ReactNode;
  label?: string;
  onPress?: () => void;
  accessibilityLabel?: string;
  variant?: "icon" | "compact";
  style?: StyleProp<ViewStyle>;
};

export type HeaderDropdownOption = {
  label: string;
  value: string;
};

export type HeaderDropdownSlotProps = {
  title: string;
  value: string | null | undefined;
  placeholder?: string;
  options?: HeaderDropdownOption[];
  onChange?: (value: string) => void;
  onPress?: () => void;
  containerStyle?: StyleProp<ViewStyle>;
  disabled?: boolean;
};

export type HeaderSearchSlotProps = TextInputProps & {
  containerStyle?: StyleProp<ViewStyle>;
  onClear?: () => void;
};

