import { StyleSheet, View } from "react-native";
import { EmptyState } from "../../src/components/ui/EmptyState";
import { AdaptiveHeader } from "../../src/layout/adaptive/AdaptiveHeader";
import { AdaptiveScreen } from "../../src/layout/adaptive/AdaptiveScreen";
import { spacing } from "../../src/theme/tokens";

export default function CalendarPlaceholderScreen() {
  return (
    <AdaptiveScreen contentStyle={styles.content}>
      <AdaptiveHeader
        title="Calendar"
        subtitle="Upcoming timeline and planning views will land here."
      />
      <View style={styles.center}>
        <EmptyState
          title="Calendar placeholder"
          message="This tab is a temporary placeholder for the upcoming calendar experience."
        />
      </View>
    </AdaptiveScreen>
  );
}

const styles = StyleSheet.create({
  content: {
    flex: 1
  },
  center: {
    flex: 1,
    justifyContent: "center",
    paddingBottom: spacing[24]
  }
});
