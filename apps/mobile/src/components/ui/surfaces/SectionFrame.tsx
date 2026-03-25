import type { ReactNode } from "react";
import type { StyleProp, ViewStyle } from "react-native";
import { StyleSheet, View } from "react-native";
import { borders, palette, radius, spacing, surfaces, typography } from "../../../theme/tokens";
import { AppText } from "../text/AppText";

type SectionFrameProps = {
  title: string;
  children: ReactNode;
  style?: StyleProp<ViewStyle>;
  contentStyle?: StyleProp<ViewStyle>;
};

export function SectionFrame({
  title,
  children,
  style,
  contentStyle
}: SectionFrameProps) {
  return (
    <View style={[styles.shell, style]}>
      <AppText style={styles.title}>{title}</AppText>
      <View style={[styles.content, contentStyle]}>{children}</View>
    </View>
  );
}

const styles = StyleSheet.create({
  shell: {
    borderWidth: borders.width.thin,
    borderColor: palette.borderStrong,
    borderRadius: radius.medium,
    backgroundColor: surfaces.card,
    paddingTop: spacing[12],
    paddingHorizontal: spacing[12],
    paddingBottom: spacing[12]
  },
  title: {
    color: "#D8D8D8",
    ...typography.cardTitle
  },
  content: {
    marginTop: spacing[10]
  }
});

