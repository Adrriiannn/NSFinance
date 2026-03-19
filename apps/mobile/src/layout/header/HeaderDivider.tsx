import { StyleSheet, View } from "react-native";
import { HEADER_CONSTANTS, HEADER_SURFACES } from "./header.constants";

export function HeaderDivider({ visible = true }: { visible?: boolean }) {
  return (
    <View
      style={[
        styles.divider,
        HEADER_SURFACES.divider,
        !visible ? styles.hidden : null
      ]}
    />
  );
}

const styles = StyleSheet.create({
  divider: {
    height: HEADER_CONSTANTS.stickyDividerHeight,
    width: "100%"
  },
  hidden: {
    opacity: 0
  }
});

