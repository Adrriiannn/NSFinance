import { View } from "react-native";
import { EmptyState } from "../../../../../src/components/ui/EmptyState";

export default function ExpenseTrackerCalendarPlaceholderScreen() {
  return (
    <View style={{ flex: 1, justifyContent: "center" }}>
      <EmptyState
        title="Calendar placeholder"
        message="This is a temporary placeholder for the planning hub calendar tab."
      />
    </View>
  );
}
