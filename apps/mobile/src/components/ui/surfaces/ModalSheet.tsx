import type { ReactNode } from "react";
import { Modal, Pressable, ScrollView, View, useWindowDimensions } from "react-native";
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
  maxHeightRatio?: number;
};

export function ModalSheet({
  visible,
  onClose,
  title,
  subtitle,
  children,
  footer,
  maxHeightRatio = 0.78
}: ModalSheetProps) {
  const surfacePresets = useSurfacePresets();
  const bottomInsetPolicy = useRuntimeBottomInsetPolicy();
  const { height } = useWindowDimensions();
  const clampedRatio = Math.min(0.95, Math.max(0.25, maxHeightRatio));
  const resolvedMaxHeight = Math.round(height * clampedRatio);

  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <Pressable style={[surfacePresets.overlay, { justifyContent: "flex-end" }]} onPress={onClose}>
        <Pressable
          style={[
            surfacePresets.modalSheet,
            { maxHeight: resolvedMaxHeight },
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
