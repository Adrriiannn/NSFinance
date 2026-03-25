import { useMemo, type ReactNode } from "react";
import { StyleSheet, View, type StyleProp, type ViewProps, type ViewStyle } from "react-native";
import { useRuntimeBottomInsetPolicy } from "../../../theme/insets";

type BottomInsetMode = "content" | "scrollable" | "action" | "actionTight" | "drawer" | "floating";

type BottomInsetAwareViewProps = ViewProps & {
  mode?: BottomInsetMode;
  extraBottom?: number;
  style?: StyleProp<ViewStyle>;
  children?: ReactNode;
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

export function BottomInsetAwareView({
  mode = "content",
  extraBottom = 0,
  style,
  children,
  ...rest
}: BottomInsetAwareViewProps) {
  const policy = useRuntimeBottomInsetPolicy();

  const resolvedStyle = useMemo(() => {
    const baseStyle = StyleSheet.flatten(style) ?? {};
    const basePaddingBottom = typeof baseStyle.paddingBottom === "number" ? baseStyle.paddingBottom : 0;

    return [
      style,
      {
        paddingBottom: basePaddingBottom + resolveModeInset(mode, policy) + extraBottom
      }
    ] as StyleProp<ViewStyle>;
  }, [extraBottom, mode, policy, style]);

  return (
    <View style={resolvedStyle} {...rest}>
      {children}
    </View>
  );
}
