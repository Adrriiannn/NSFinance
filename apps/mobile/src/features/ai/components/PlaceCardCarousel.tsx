import { Ionicons } from "@expo/vector-icons";
import { useCallback, useMemo, useRef, useState } from "react";
import {
  Animated,
  Linking,
  PanResponder,
  Share,
  StyleSheet,
  useWindowDimensions,
  View,
  Pressable
} from "react-native";
import { layout, spacing, useThemeTokens } from "../../../theme/tokens";
import { PlaceCardActions } from "./PlaceCardActions";
import { PlaceResultCard } from "./PlaceResultCard";
import {
  type CompanionPlaceCard,
  buildDirectionsUrl,
  buildSharePayload,
  ensureLinkingUrl,
  normalizePhoneForTel
} from "../utils/placeCardFormatting";

type PlaceCardCarouselProps = {
  places: CompanionPlaceCard[];
};

const arrowSpace = 38;
const maxCardWidth = 360;

export function PlaceCardCarousel({ places }: PlaceCardCarouselProps) {
  const tokens = useThemeTokens();
  const { width } = useWindowDimensions();
  const [currentIndex, setCurrentIndex] = useState(0);
  const slide = useRef(new Animated.Value(0)).current;
  const scale = useRef(new Animated.Value(1)).current;
  const opacity = useRef(new Animated.Value(1)).current;
  const cardWidth = Math.min(width - (layout.screenHorizontalPadding * 2) - arrowSpace, maxCardWidth);
  const currentPlace = places[currentIndex];

  const canGoPrevious = currentIndex > 0;
  const canGoNext = currentIndex < places.length - 1;

  const navigate = useCallback((direction: -1 | 1) => {
    const nextIndex = currentIndex + direction;
    if (nextIndex < 0 || nextIndex >= places.length) {
      return;
    }

    Animated.parallel([
      Animated.timing(slide, {
        toValue: direction * -28,
        duration: 120,
        useNativeDriver: true
      }),
      Animated.timing(opacity, {
        toValue: 0.45,
        duration: 120,
        useNativeDriver: true
      }),
      Animated.timing(scale, {
        toValue: 0.985,
        duration: 120,
        useNativeDriver: true
      })
    ]).start(() => {
      setCurrentIndex(nextIndex);
      slide.setValue(direction * 30);
      opacity.setValue(0.35);
      scale.setValue(0.985);
      Animated.parallel([
        Animated.timing(slide, {
          toValue: 0,
          duration: 170,
          useNativeDriver: true
        }),
        Animated.timing(opacity, {
          toValue: 1,
          duration: 170,
          useNativeDriver: true
        }),
        Animated.timing(scale, {
          toValue: 1,
          duration: 170,
          useNativeDriver: true
        })
      ]).start();
    });
  }, [currentIndex, opacity, places.length, scale, slide]);

  const panResponder = useMemo(
    () =>
      PanResponder.create({
        onMoveShouldSetPanResponder: (_event, gesture) => Math.abs(gesture.dx) > 14 && Math.abs(gesture.dx) > Math.abs(gesture.dy),
        onPanResponderRelease: (_event, gesture) => {
          if (gesture.dx < -42 && canGoNext) {
            navigate(1);
          } else if (gesture.dx > 42 && canGoPrevious) {
            navigate(-1);
          }
        }
      }),
    [canGoNext, canGoPrevious, navigate]
  );

  if (!currentPlace || places.length === 0) {
    return null;
  }

  async function openExternalUrl(url: string | null) {
    const target = ensureLinkingUrl(url);
    if (!target) {
      return;
    }

    try {
      await Linking.openURL(target);
    } catch (error) {
      console.warn("[PlaceCardCarousel] Failed to open URL", error);
    }
  }

  async function handleDirections(place: CompanionPlaceCard) {
    await openExternalUrl(buildDirectionsUrl(place));
  }

  async function handleShare(place: CompanionPlaceCard) {
    try {
      await Share.share(buildSharePayload(place));
    } catch (error) {
      console.warn("[PlaceCardCarousel] Failed to share place", error);
    }
  }

  async function handleCall(place: CompanionPlaceCard) {
    const phone = normalizePhoneForTel(place.phoneNumber);
    if (!phone) {
      return;
    }

    try {
      await Linking.openURL(`tel:${phone}`);
    } catch (error) {
      console.warn("[PlaceCardCarousel] Failed to open phone dialer", error);
    }
  }

  return (
    <View style={[styles.wrapper, { width: cardWidth + arrowSpace }]}>
      <View style={styles.viewport}>
        {canGoPrevious ? (
          <ArrowButton
            direction="left"
            color={tokens.isDarkTheme ? "rgba(230,230,230,0.62)" : "rgba(88,88,88,0.58)"}
            onPress={() => navigate(-1)}
          />
        ) : null}
        <Animated.View
          {...panResponder.panHandlers}
          style={[
            styles.animatedCard,
            {
              width: cardWidth,
              transform: [{ translateX: slide }, { scale }],
              opacity
            }
          ]}
        >
          <PlaceResultCard
            place={currentPlace}
            onOpenWebsite={(place) => {
              void openExternalUrl(place.websiteUrl ?? null);
            }}
            onOpenMenu={(place) => {
              void openExternalUrl(place.menuUrl ?? null);
            }}
            onCall={(place) => {
              void handleCall(place);
            }}
          />
        </Animated.View>
        {canGoNext ? (
          <ArrowButton
            direction="right"
            color={tokens.isDarkTheme ? "rgba(230,230,230,0.62)" : "rgba(88,88,88,0.58)"}
            onPress={() => navigate(1)}
          />
        ) : null}
      </View>
      <View style={[styles.actionsWrap, { width: cardWidth }]}>
        <PlaceCardActions
          place={currentPlace}
          onDirections={(place) => {
            void handleDirections(place);
          }}
          onShare={(place) => {
            void handleShare(place);
          }}
        />
      </View>
    </View>
  );
}

function ArrowButton({
  direction,
  color,
  onPress
}: {
  direction: "left" | "right";
  color: string;
  onPress: () => void;
}) {
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={direction === "left" ? "Previous place" : "Next place"}
      hitSlop={12}
      style={[
        styles.arrow,
        direction === "left" ? styles.leftArrow : styles.rightArrow
      ]}
      onPress={onPress}
    >
      <Ionicons
        name={direction === "left" ? "chevron-back" : "chevron-forward"}
        size={86}
        color={color}
      />
    </Pressable>
  );
}

const styles = StyleSheet.create({
  wrapper: {
    alignItems: "flex-start",
    marginTop: spacing[8]
  },
  viewport: {
    position: "relative",
    minHeight: 410,
    alignItems: "flex-start",
    justifyContent: "center"
  },
  animatedCard: {
    alignSelf: "flex-start"
  },
  actionsWrap: {
    alignSelf: "flex-start"
  },
  arrow: {
    position: "absolute",
    top: "38%",
    zIndex: 4,
    width: 44,
    minHeight: 96,
    alignItems: "center",
    justifyContent: "center",
    backgroundColor: "transparent"
  },
  leftArrow: {
    left: -34
  },
  rightArrow: {
    right: -34
  }
});
