import type { StyleProp, ViewStyle } from "react-native";
import { EmptyState as BaseEmptyState } from "./feedback/EmptyState";

type EmptyStateProps = {
  title: string;
  message: string;
  actionLabel?: string;
  onActionPress?: () => void;
  style?: StyleProp<ViewStyle>;
  hideOrb?: boolean;
  centerText?: boolean;
};

export function EmptyState(props: EmptyStateProps) {
  return <BaseEmptyState {...props} />;
}
