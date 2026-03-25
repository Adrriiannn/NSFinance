import { useLocalSearchParams } from "expo-router";
import { StyleSheet, View } from "react-native";
import { EmptyState } from "../../../src/components/ui/EmptyState";
import { PlanningHubShell } from "../../../src/components/planningHub/PlanningHubShell";
import { AdaptiveScreen } from "../../../src/layout/adaptive/AdaptiveScreen";
import { HeaderShell } from "../../../src/layout/appHeader";
import { spacing } from "../../../src/theme/tokens";

export default function CalendarPlaceholderScreen() {
  const params = useLocalSearchParams<{ source?: string }>();
  const isPlanningContext = params.source === "planningHub" || params.source === "expense";

  const body = (
    <>
      <HeaderShell preset="primaryDefault" includeTopInset title="Calendar" />
      <View style={styles.center}>
        <EmptyState
          title="Calendar placeholder"
          message={
            isPlanningContext
              ? "This shared calendar is currently showing the planning-hub context."
              : "This shared calendar is currently showing the finance-hub context."
          }
          hideOrb
          centerText
        />
      </View>
    </>
  );

  if (isPlanningContext) {
    return (
      <PlanningHubShell>
        <View style={styles.content}>{body}</View>
      </PlanningHubShell>
    );
  }

  return <AdaptiveScreen contentStyle={styles.content}>{body}</AdaptiveScreen>;
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

