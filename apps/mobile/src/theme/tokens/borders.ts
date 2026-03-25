import { StyleSheet } from "react-native";

export const borders = {
  width: {
    none: 0,
    hairline: StyleSheet.hairlineWidth,
    thin: 1,
    medium: 1.5
  },
  color: {
    subtle: "rgba(242, 140, 40, 0.18)",
    default: "rgba(242, 140, 40, 0.32)",
    strong: "rgba(242, 140, 40, 0.52)"
  }
} as const;
