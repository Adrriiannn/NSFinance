import { ReactNode } from "react";
import { Pressable, StyleProp, StyleSheet, View, ViewStyle } from "react-native";
import { layout, palette, radius, shadows, surfaces } from "../../theme/tokens";

type GlassCardProps = {
  children: ReactNode;
  style?: StyleProp<ViewStyle>;
  onPress?: () => void;
};

export function GlassCard({ children, style, onPress }: GlassCardProps) {
  return (
    <Pressable
      onPress={onPress}
      disabled={!onPress}
      style={({ pressed }) => [
        styles.card,
        style,
        onPress && pressed ? styles.pressed : null
      ]}
    >
      <View style={styles.topEdge} />
      {children}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: surfaces.card,
    borderRadius: radius.large,
    borderWidth: 1,
    borderColor: palette.border,
    overflow: "hidden",
    padding: layout.cardPadding,
    ...shadows.soft
  },
  topEdge: {
    position: "absolute",
    top: 0,
    left: 0,
    right: 0,
    height: 1,
    backgroundColor: "rgba(226,236,255,0.24)"
  },
  pressed: {
    opacity: 0.94,
    transform: [{ scale: 0.992 }]
  }
});
