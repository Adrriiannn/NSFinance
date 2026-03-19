import { StyleSheet, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import {
  EXPENSE_HUB_CONTENT_PADDING_X,
  EXPENSE_HUB_CONTENT_TOP_GAP,
  getExpenseHubContentBottomInset
} from "../../../../../src/components/expenseTracker/expenseHubLayout";
import { EmptyState } from "../../../../../src/components/ui/EmptyState";
import { HeaderShell } from "../../../../../src/layout/appHeader";

export default function ExpenseTrackerCalendarPlaceholderScreen() {
  const insets = useSafeAreaInsets();

  return (
    <View style={styles.screen}>
      <HeaderShell
        preset="primaryDefault"
        title="Calendar"
        includeTopInset
        bleedHorizontal={EXPENSE_HUB_CONTENT_PADDING_X}
      />
      <View
        style={[
          styles.center,
          {
            paddingTop: EXPENSE_HUB_CONTENT_TOP_GAP,
            paddingBottom: getExpenseHubContentBottomInset(insets.bottom)
          }
        ]}
      >
        <EmptyState
          title="Calendar placeholder"
          message="This is a temporary placeholder for the planning hub calendar tab."
        />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  screen: {
    flex: 1
  },
  center: {
    flex: 1,
    justifyContent: "center"
  }
});
