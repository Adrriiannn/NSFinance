import { StyleSheet, View } from "react-native";

export function AppBackgroundLayer() {
  return (
    <View pointerEvents="none" style={StyleSheet.absoluteFill}>
      <View style={styles.orangeWash} />
      <View style={styles.neutralHaze} />
      <View style={styles.vignette} />
    </View>
  );
}

const styles = StyleSheet.create({
  orangeWash: {
    position: "absolute",
    top: -140,
    left: -120,
    width: 320,
    height: 320,
    borderRadius: 160,
    backgroundColor: "rgba(242,140,40,0.03)"
  },
  neutralHaze: {
    position: "absolute",
    top: 90,
    right: -80,
    width: 260,
    height: 260,
    borderRadius: 130,
    backgroundColor: "rgba(255,255,255,0.015)"
  },
  vignette: {
    ...StyleSheet.absoluteFillObject,
    borderColor: "rgba(0,0,0,0.2)",
    borderWidth: 1
  }
});

