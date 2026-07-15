import type { ReactNode } from "react";
import { View } from "react-native";
import type { AccessibilityProps } from "react-native";
import { AppText } from "../text/AppText";
import { useFeedbackPresets, type FeedbackTone } from "./feedback.presets";

type BannerProps = AccessibilityProps & {
  title: string;
  message?: ReactNode;
  tone?: FeedbackTone;
};

export function Banner({ title, message, tone = "info", ...accessibilityProps }: BannerProps) {
  const { bannerPresets, feedbackPresets } = useFeedbackPresets();

  return (
    <View
      {...accessibilityProps}
      style={[feedbackPresets.banner, bannerPresets[tone]]}
    >
      <AppText preset="label" style={feedbackPresets.bannerTitle}>
        {title}
      </AppText>
      {message ? (
        <AppText preset="secondary" style={feedbackPresets.bannerMessage}>
          {message}
        </AppText>
      ) : null}
    </View>
  );
}
