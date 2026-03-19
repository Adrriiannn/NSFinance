import { StyleSheet, View } from "react-native";
import { HEADER_CONSTANTS } from "./header.constants";
import type { HeaderRowProps } from "./header.types";

export function HeaderRow({ children, height, style }: HeaderRowProps) {
  return <View style={[styles.row, { minHeight: height }, style]}>{children}</View>;
}

const styles = StyleSheet.create({
  row: {
    width: "100%",
    paddingHorizontal: HEADER_CONSTANTS.paddingX,
    flexDirection: "row",
    alignItems: "center",
    gap: HEADER_CONSTANTS.contentGap
  }
});

