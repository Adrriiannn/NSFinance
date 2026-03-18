import type { TextStyle } from "react-native";
import { palette, typography } from "../../../theme/tokens";

export type TextPresetName =
  | "display"
  | "screenTitle"
  | "sectionTitle"
  | "cardTitle"
  | "label"
  | "body"
  | "secondary"
  | "caption"
  | "moneyValue"
  | "moneyLarge"
  | "positiveMoney"
  | "negativeMoney"
  | "buttonLabel"
  | "fieldLabel"
  | "helper";

export const textPresets: Record<TextPresetName, TextStyle> = {
  display: {
    color: palette.textPrimary,
    ...typography.display
  },
  screenTitle: {
    color: palette.textPrimary,
    ...typography.title1
  },
  sectionTitle: {
    color: palette.textPrimary,
    ...typography.sectionTitle
  },
  cardTitle: {
    color: palette.textPrimary,
    ...typography.cardTitle
  },
  label: {
    color: palette.textPrimary,
    ...typography.label
  },
  body: {
    color: palette.textPrimary,
    ...typography.body
  },
  secondary: {
    color: palette.textSecondary,
    ...typography.body2
  },
  caption: {
    color: palette.textSecondary,
    ...typography.caption
  },
  moneyValue: {
    color: palette.textPrimary,
    ...typography.amount
  },
  moneyLarge: {
    color: palette.textPrimary,
    ...typography.amountLarge
  },
  positiveMoney: {
    color: palette.success,
    ...typography.amount
  },
  negativeMoney: {
    color: palette.negative,
    ...typography.amount
  },
  buttonLabel: {
    color: palette.textPrimary,
    ...typography.buttonLabel
  },
  fieldLabel: {
    color: palette.textPrimary,
    ...typography.fieldLabel
  },
  helper: {
    color: palette.textSecondary,
    ...typography.helper
  }
};
