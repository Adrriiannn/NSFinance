import type { ReactNode } from "react";
import { View } from "react-native";
import { AppText } from "../text/AppText";
import { bannerPresets, feedbackPresets, type FeedbackTone } from "./feedback.presets";

type BannerProps = {
  title: string;
  message?: ReactNode;
  tone?: FeedbackTone;
};

export function Banner({ title, message, tone = "info" }: BannerProps) {
  return (
    <View style={[feedbackPresets.banner, bannerPresets[tone]]}>
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
