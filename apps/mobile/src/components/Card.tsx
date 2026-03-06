import { ReactNode } from "react";
import { StyleSheet, View } from "react-native";
import { palette, radius, spacing } from "../theme/tokens";

type CardProps = {
  children: ReactNode;
};

export function Card({ children }: CardProps) {
  return <View style={styles.card}>{children}</View>;
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: palette.surface,
    borderRadius: radius.md,
    borderColor: palette.border,
    borderWidth: 1,
    padding: spacing.md,
    marginBottom: spacing.md
  }
});
