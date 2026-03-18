import { View } from "react-native";
import { AppText } from "../text/AppText";
import { bannerPresets, feedbackPresets, type FeedbackTone } from "./feedback.presets";

type SnackbarProps = {
  message: string;
  tone?: FeedbackTone;
};

export function Snackbar({ message, tone = "info" }: SnackbarProps) {
  return (
    <View style={[feedbackPresets.snackbar, bannerPresets[tone]]}>
      <AppText preset="caption" style={{ color: "#E2ECFF", fontWeight: "700" }}>
        {message}
      </AppText>
    </View>
  );
}
