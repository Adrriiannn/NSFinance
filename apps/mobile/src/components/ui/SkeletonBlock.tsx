import type { StyleProp, ViewStyle } from "react-native";
import { Skeleton } from "./feedback/Skeleton";

type SkeletonBlockProps = {
  style?: StyleProp<ViewStyle>;
};

export function SkeletonBlock({ style }: SkeletonBlockProps) {
  return <Skeleton style={style} />;
}
