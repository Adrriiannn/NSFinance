import type { ReactNode } from "react";
import { View } from "react-native";
import { AppText } from "../text/AppText";
import { formPresets } from "./form.presets";

type FormSectionProps = {
  title?: string;
  description?: string;
  children: ReactNode;
};

export function FormSection({ title, description, children }: FormSectionProps) {
  return (
    <View style={formPresets.section}>
      {title || description ? (
        <View style={formPresets.sectionHeader}>
          {title ? <AppText preset="sectionTitle">{title}</AppText> : null}
          {description ? <AppText preset="secondary">{description}</AppText> : null}
        </View>
      ) : null}
      <View style={formPresets.fieldGroup}>{children}</View>
    </View>
  );
}
