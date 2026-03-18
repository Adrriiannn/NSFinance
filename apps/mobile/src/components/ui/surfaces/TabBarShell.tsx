import type { ReactNode } from "react";
import { View } from "react-native";
import type { StyleProp, ViewStyle } from "react-native";
import { surfacePresets } from "./surface.presets";

type TabBarShellProps = {
  children: ReactNode;
  style?: StyleProp<ViewStyle>;
};

export function TabBarShell({ children, style }: TabBarShellProps) {
  return <View style={[surfacePresets.tabBarShell, style]}>{children}</View>;
}
