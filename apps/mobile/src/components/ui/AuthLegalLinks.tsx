import { Pressable, StyleSheet, Text, View } from "react-native";
import { palette, spacing, typography } from "../../theme/tokens";

type AuthLegalLinksProps = {
  onPressTerms: () => void;
  onPressPrivacy: () => void;
};

export function AuthLegalLinks({ onPressTerms, onPressPrivacy }: AuthLegalLinksProps) {
  return (
    <View style={styles.row}>
      <Pressable onPress={onPressTerms} style={({ pressed }) => [pressed ? styles.pressed : null]}>
        <Text style={styles.link}>Terms of Service</Text>
      </Pressable>
      <Pressable onPress={onPressPrivacy} style={({ pressed }) => [pressed ? styles.pressed : null]}>
        <Text style={styles.link}>Privacy Policy</Text>
      </Pressable>
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  link: {
    color: palette.primaryGlow,
    ...typography.caption
  },
  pressed: {
    opacity: 0.75
  }
});
