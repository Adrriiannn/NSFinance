import { StyleSheet, Text, View } from "react-native";
import { HEADER_CONSTANTS, HEADER_TYPOGRAPHY } from "./header.constants";
import type { HeaderTitleBlockProps } from "./header.types";

export function HeaderTitleBlock({
  title,
  subtitle,
  mode,
  variant
}: HeaderTitleBlockProps) {
  const isGreeting = variant === "greeting";

  return (
    <View
      style={[
        styles.wrap,
        mode === "centered" ? styles.centeredWrap : styles.leadingWrap,
        {
          maxWidth:
            mode === "centered"
              ? HEADER_CONSTANTS.titleMaxWidthCentered
              : isGreeting
                ? HEADER_CONSTANTS.greetingTitleMaxWidth
                : HEADER_CONSTANTS.titleMaxWidthDefault
        }
      ]}
    >
      <Text
        numberOfLines={1}
        style={[
          isGreeting ? HEADER_TYPOGRAPHY.headerGreetingTitle : HEADER_TYPOGRAPHY.headerTitle,
          mode === "centered" ? HEADER_TYPOGRAPHY.headerCenteredTitle : null
        ]}
      >
        {title}
      </Text>
      {subtitle ? (
        <Text
          numberOfLines={1}
          style={[
            HEADER_TYPOGRAPHY.headerSubtitle,
            {
              marginTop: isGreeting
                ? HEADER_CONSTANTS.greetingTitleSubtitleGap
                : HEADER_CONSTANTS.titleSubtitleGap,
              maxWidth: isGreeting
                ? HEADER_CONSTANTS.greetingSubtitleMaxWidth
                : HEADER_CONSTANTS.subtitleMaxWidth,
              textAlign: mode === "centered" ? "center" : "left"
            }
          ]}
        >
          {subtitle}
        </Text>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: {
    flexShrink: 1
  },
  centeredWrap: {
    alignItems: "center",
    marginHorizontal: "auto"
  },
  leadingWrap: {
    alignItems: "flex-start"
  }
});

