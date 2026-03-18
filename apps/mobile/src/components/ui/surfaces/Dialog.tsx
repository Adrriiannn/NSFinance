import type { ReactNode } from "react";
import { Modal, Pressable, View } from "react-native";
import { AppText } from "../text/AppText";
import { surfacePresets } from "./surface.presets";

type DialogProps = {
  visible: boolean;
  onClose: () => void;
  title?: string;
  children: ReactNode;
  footer?: ReactNode;
};

export function Dialog({ visible, onClose, title, children, footer }: DialogProps) {
  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <Pressable
        style={[surfacePresets.overlay, { alignItems: "center", justifyContent: "center", padding: 20 }]}
        onPress={onClose}
      >
        <Pressable style={surfacePresets.dialog} onPress={() => undefined}>
          {title ? <AppText preset="sectionTitle">{title}</AppText> : null}
          <View style={{ gap: 12 }}>{children}</View>
          {footer}
        </Pressable>
      </Pressable>
    </Modal>
  );
}
