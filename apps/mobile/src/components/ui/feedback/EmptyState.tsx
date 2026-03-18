import type { StyleProp, ViewStyle } from "react-native";
import { View } from "react-native";
import { Button } from "../buttons/Button";
import { AppText } from "../text/AppText";
import { feedbackPresets } from "./feedback.presets";

type EmptyStateProps = {
  title: string;
  message: string;
  actionLabel?: string;
  onActionPress?: () => void;
  style?: StyleProp<ViewStyle>;
};

export function EmptyState({
  title,
  message,
  actionLabel,
  onActionPress,
  style
}: EmptyStateProps) {
  return (
    <View style={[feedbackPresets.emptyState, style]}>
      <View style={feedbackPresets.emptyStateOrb} />
      <AppText preset="sectionTitle">{title}</AppText>
      <AppText preset="secondary" style={{ textAlign: "center" }}>
        {message}
      </AppText>
      {actionLabel ? (
        <Button variant="compact" label={actionLabel} onPress={onActionPress} />
      ) : null}
    </View>
  );
}
