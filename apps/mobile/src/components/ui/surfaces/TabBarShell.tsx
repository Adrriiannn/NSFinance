import type { ReactNode } from "react";
import { View } from "react-native";
import type { StyleProp, ViewProps, ViewStyle } from "react-native";
import { surfacePresets } from "./surface.presets";

type TabBarShellProps = ViewProps & {
  children: ReactNode;
  style?: StyleProp<ViewStyle>;
};

export function TabBarShell({ children, style, ...rest }: TabBarShellProps) {
  return (
    <View {...rest} style={[surfacePresets.tabBarShell, style]}>
      {children}
    </View>
  );
}
