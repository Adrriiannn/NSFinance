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
  hideOrb?: boolean;
  centerText?: boolean;
};

export function EmptyState({
  title,
  message,
  actionLabel,
  onActionPress,
  style,
  hideOrb = false,
  centerText = false
}: EmptyStateProps) {
  return (
    <View style={[feedbackPresets.emptyState, style]}>
      {hideOrb ? null : <View style={feedbackPresets.emptyStateOrb} />}
      <AppText preset="sectionTitle" style={centerText ? { textAlign: "center" } : undefined}>
        {title}
      </AppText>
      <AppText preset="secondary" style={centerText ? { textAlign: "center" } : undefined}>
        {message}
      </AppText>
      {actionLabel ? (
        <Button variant="compact" label={actionLabel} onPress={onActionPress} />
      ) : null}
    </View>
  );
}
