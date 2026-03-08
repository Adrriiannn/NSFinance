import { Redirect } from "expo-router";
import { ActivityIndicator, StyleSheet, Text, View } from "react-native";
import { useAuthSession } from "../src/providers/AuthProvider";
import { palette, typography } from "../src/theme/tokens";

export default function IndexScreen() {
  const { isBootstrapping, isAuthenticated } = useAuthSession();

  if (isBootstrapping) {
    return (
      <View style={styles.loadingWrap}>
        <ActivityIndicator color={palette.primaryGlow} />
        <Text style={styles.loadingText}>Loading NSFinTech...</Text>
      </View>
    );
  }

  return <Redirect href={(isAuthenticated ? "/(tabs)" : "/login") as never} />;
}

const styles = StyleSheet.create({
  loadingWrap: {
    flex: 1,
    backgroundColor: palette.appBackground,
    alignItems: "center",
    justifyContent: "center",
    gap: 12
  },
  loadingText: {
    color: palette.textSecondary,
    ...typography.body
  }
});

