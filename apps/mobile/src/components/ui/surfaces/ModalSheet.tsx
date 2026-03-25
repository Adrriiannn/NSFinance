import type { ReactNode } from "react";
import { Modal, Pressable, ScrollView, View } from "react-native";
import { useRuntimeBottomInsetPolicy } from "../../../theme/insets";
import { AppText } from "../text/AppText";
import { useSurfacePresets } from "./surface.presets";

type ModalSheetProps = {
  visible: boolean;
  onClose: () => void;
  title?: string;
  subtitle?: string;
  children: ReactNode;
  footer?: ReactNode;
};

export function ModalSheet({
  visible,
  onClose,
  title,
  subtitle,
  children,
  footer
}: ModalSheetProps) {
  const surfacePresets = useSurfacePresets();
  const bottomInsetPolicy = useRuntimeBottomInsetPolicy();

  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <Pressable style={[surfacePresets.overlay, { justifyContent: "flex-end" }]} onPress={onClose}>
        <Pressable
          style={[
            surfacePresets.modalSheet,
            { paddingBottom: 20 + bottomInsetPolicy.bottomActionInsetTight }
          ]}
          onPress={() => undefined}
        >
          <View style={surfacePresets.modalHandle} />
          {title ? (
            <View style={{ gap: 4 }}>
              <AppText preset="sectionTitle">{title}</AppText>
              {subtitle ? <AppText preset="secondary">{subtitle}</AppText> : null}
            </View>
          ) : null}
          <ScrollView
            showsVerticalScrollIndicator={false}
            contentContainerStyle={{ gap: 12, paddingBottom: bottomInsetPolicy.bottomScrollableInset }}
          >
            {children}
          </ScrollView>
          {footer}
        </Pressable>
      </Pressable>
    </Modal>
  );
}
