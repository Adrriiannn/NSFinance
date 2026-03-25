import { useMemo } from "react";
import { ScrollView, StyleSheet, type ScrollViewProps, type StyleProp, type ViewStyle } from "react-native";
import { useRuntimeBottomInsetPolicy } from "../../../theme/insets";

type BottomInsetMode = "content" | "scrollable" | "action" | "actionTight" | "drawer" | "floating";

type BottomInsetAwareScrollViewProps = ScrollViewProps & {
  mode?: BottomInsetMode;
  extraBottom?: number;
  contentContainerStyle?: StyleProp<ViewStyle>;
};

function resolveModeInset(
  mode: BottomInsetMode,
  policy: ReturnType<typeof useRuntimeBottomInsetPolicy>
) {
  if (mode === "action") {
    return policy.bottomActionInset;
  }

  if (mode === "actionTight") {
    return policy.bottomActionInsetTight;
  }

  if (mode === "drawer") {
    return policy.bottomDrawerInset;
  }

  if (mode === "floating") {
    return policy.bottomFloatingInset;
  }

  if (mode === "scrollable") {
    return policy.bottomScrollableInset;
  }

  return policy.bottomContentInset;
}

export function BottomInsetAwareScrollView({
  mode = "scrollable",
  extraBottom = 0,
  contentContainerStyle,
  ...rest
}: BottomInsetAwareScrollViewProps) {
  const policy = useRuntimeBottomInsetPolicy();

  const resolvedContentContainerStyle = useMemo(() => {
    const baseStyle = StyleSheet.flatten(contentContainerStyle) ?? {};
    const basePaddingBottom = typeof baseStyle.paddingBottom === "number" ? baseStyle.paddingBottom : 0;

    return [
      contentContainerStyle,
      {
        paddingBottom: basePaddingBottom + resolveModeInset(mode, policy) + extraBottom
      }
    ] as StyleProp<ViewStyle>;
  }, [contentContainerStyle, extraBottom, mode, policy]);

  return <ScrollView contentContainerStyle={resolvedContentContainerStyle} {...rest} />;
}
