import { StyleSheet, View } from "react-native";

type AuthDividerProps = {
  widthPercent?: number;
};

export function AuthDivider({ widthPercent = 70 }: AuthDividerProps) {
  return (
    <View style={styles.wrap}>
      <View style={[styles.line, { width: `${widthPercent}%` }]} />
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    alignItems: "center",
    justifyContent: "center"
  },
  line: {
    height: StyleSheet.hairlineWidth,
    backgroundColor: "rgba(242,140,40,0.22)"
  }
});

