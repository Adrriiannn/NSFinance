import type { ReactNode } from "react";
import {
  Modal as NativeModal,
  StyleSheet,
  View,
  type ColorValue,
  type ModalProps
} from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { normalizeSystemInset } from "../../../theme/systemInsets";

export type SystemModalSafeAreaEdge = "top" | "right" | "bottom" | "left";

type SystemModalProps = Omit<ModalProps, "children"> & {
  children: ReactNode;
  safeAreaEdges?: readonly SystemModalSafeAreaEdge[];
  safeAreaBackgroundColor?: ColorValue;
};

const defaultSafeAreaEdges: readonly SystemModalSafeAreaEdge[] = [
  "top",
  "right",
  "bottom",
  "left"
];

export function SystemModal({
  children,
  safeAreaEdges = defaultSafeAreaEdges,
  safeAreaBackgroundColor,
  ...modalProps
}: SystemModalProps) {
  const insets = useSafeAreaInsets();
  const includes = (edge: SystemModalSafeAreaEdge) => safeAreaEdges.includes(edge);

  return (
    <NativeModal {...modalProps}>
      <View
        style={[
          styles.viewport,
          {
            backgroundColor: safeAreaBackgroundColor ?? "transparent",
            paddingTop: includes("top") ? normalizeSystemInset(insets.top) : 0,
            paddingRight: includes("right") ? normalizeSystemInset(insets.right) : 0,
            paddingBottom: includes("bottom") ? normalizeSystemInset(insets.bottom) : 0,
            paddingLeft: includes("left") ? normalizeSystemInset(insets.left) : 0
          }
        ]}
      >
        {children}
      </View>
    </NativeModal>
  );
}

const styles = StyleSheet.create({
  viewport: {
    flex: 1
  }
});
