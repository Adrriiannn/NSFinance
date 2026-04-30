import { Ionicons, MaterialCommunityIcons } from "@expo/vector-icons";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Animated, Image, Modal, PanResponder, Pressable, StyleSheet, Text, useWindowDimensions, View } from "react-native";
import { resolveApiRequestUrl } from "../../../lib/api/diagnostics";
import { radius, spacing, typography, useThemeTokens } from "../../../theme/tokens";
import {
  type CompanionPlaceCard,
  formatDistanceKm,
  formatDuration,
  formatPhoneDisplay,
  formatPriceLevel,
  formatRating,
  formatWebsiteDisplay,
  getCategoryColor,
  getDistanceColor,
  getRatingColor,
  humanizeCategory
} from "../utils/placeCardFormatting";

type PlaceResultCardProps = {
  place: CompanionPlaceCard;
  height?: number;
  onOpenWebsite?: (place: CompanionPlaceCard) => void;
  onOpenMenu?: (place: CompanionPlaceCard) => void;
  onCall?: (place: CompanionPlaceCard) => void;
};

export function PlaceResultCard({
  place,
  height,
  onOpenWebsite,
  onOpenMenu,
  onCall
}: PlaceResultCardProps) {
  const tokens = useThemeTokens();
  const [photoIndex, setPhotoIndex] = useState(0);
  const [photoViewerOpen, setPhotoViewerOpen] = useState(false);
  const [failedPhotoUrls, setFailedPhotoUrls] = useState<Set<string>>(() => new Set());
  const cardColors = {
    background: tokens.surfaces.card,
    border: tokens.isDarkTheme ? "rgba(255,190,122,0.3)" : tokens.palette.border,
    text: tokens.palette.textPrimary,
    secondary: tokens.palette.textSecondary,
    separator: tokens.palette.border,
    placeholder: tokens.surfaces.field
  };
  const distanceText = formatDistanceKm(place.distanceMeters);
  const address = place.formattedAddress || place.shortFormattedAddress;
  const category = humanizeCategory(place.primaryTypeDisplayName || place.category);
  const rating = formatRating(place.rating);
  const websiteDisplay = formatWebsiteDisplay(place.websiteUrl);
  const menuDisplay = place.menuUrl ? formatWebsiteDisplay(place.menuUrl) : null;
  const phoneDisplay = formatPhoneDisplay(place.phoneNumber);
  const price = formatPriceLevel(place.priceLevel);
  const closesIn = formatDuration(place.closesInMinutes);
  const opensIn = formatDuration(place.opensInMinutes);
  const accentColor = tokens.palette.accent;
  const leftDetailRows = [
    rating ? (
      <DetailRow
        key="rating"
        icon="star-outline"
        label="Rating"
        value={rating}
        valueColor={getRatingColor(place.rating, accentColor)}
        textColor={cardColors.text}
        mutedColor={cardColors.secondary}
      />
    ) : null,
    typeof place.openNow === "boolean" ? (
      <DetailRow
        key="open-now"
        icon="time-outline"
        label="Open now"
        value={place.openNow ? "Open" : "Closed"}
        valueColor={place.openNow ? accentColor : tokens.palette.negative}
        textColor={cardColors.text}
        mutedColor={cardColors.secondary}
      />
    ) : null,
    price ? (
      <PriceRow
        key="price"
        activeCount={price.activeCount}
        activeColor={accentColor}
        textColor={cardColors.text}
        mutedColor={cardColors.secondary}
      />
    ) : null,
    websiteDisplay ? (
      <DetailRow
        key="website"
        icon="globe-outline"
        label="Website"
        value={websiteDisplay}
        valueColor={accentColor}
        textColor={cardColors.text}
        mutedColor={cardColors.secondary}
        accessibilityLabel={`Open website for ${place.name}`}
        onPress={() => onOpenWebsite?.(place)}
      />
    ) : null
  ].filter(Boolean);
  const rightDetailRows = [
    category ? (
      <DetailRow
        key="category"
        icon="pricetag-outline"
        label="Category"
        value={category}
        valueColor={getCategoryColor(category, place.name)}
        textColor={cardColors.text}
        mutedColor={cardColors.secondary}
      />
    ) : null,
    closesIn || opensIn ? (
      <DetailRow
        key="hours"
        icon="alarm-outline"
        label={closesIn ? "Closes in" : "Opens in"}
        value={closesIn ?? opensIn ?? ""}
        valueColor={closesIn ? tokens.palette.negative : accentColor}
        textColor={cardColors.text}
        mutedColor={cardColors.secondary}
      />
    ) : null,
    phoneDisplay ? (
      <DetailRow
        key="call"
        icon="call-outline"
        label="Call now"
        value={phoneDisplay}
        valueColor={accentColor}
        textColor={cardColors.text}
        mutedColor={cardColors.secondary}
        accessibilityLabel={`Call ${place.name}`}
        onPress={() => onCall?.(place)}
      />
    ) : null,
    menuDisplay ? (
      <DetailRow
        key="menu"
        icon="silverware-fork-knife"
        iconFamily="material"
        label="Menu"
        value={menuDisplay}
        valueColor={accentColor}
        textColor={cardColors.text}
        mutedColor={cardColors.secondary}
        accessibilityLabel={`Open menu for ${place.name}`}
        onPress={() => onOpenMenu?.(place)}
      />
    ) : null
  ].filter(Boolean);
  const photos = useMemo(() => {
    const source = [
      ...(Array.isArray(place.photoUrls) ? place.photoUrls : []),
      place.photoUrl
    ].filter((url): url is string => Boolean(url?.trim()));

    return Array.from(new Set(source))
      .map(resolvePhotoUrl)
      .filter((url) => !failedPhotoUrls.has(url));
  }, [failedPhotoUrls, place.photoUrl, place.photoUrls]);
  const activePhoto = photos[Math.min(photoIndex, Math.max(photos.length - 1, 0))];
  const photoPanResponder = useMemo(
    () =>
      PanResponder.create({
        onMoveShouldSetPanResponder: (_event, gesture) => photos.length > 1 && Math.abs(gesture.dx) > 16 && Math.abs(gesture.dx) > Math.abs(gesture.dy),
        onPanResponderRelease: (_event, gesture) => {
          if (gesture.dx < -36) {
            setPhotoIndex((current) => Math.min(photos.length - 1, current + 1));
          } else if (gesture.dx > 36) {
            setPhotoIndex((current) => Math.max(0, current - 1));
          }
        }
      }),
    [photos.length]
  );

  return (
    <View
      accessibilityLabel={`Place card for ${place.name}`}
      style={[
        styles.card,
        {
          backgroundColor: cardColors.background,
          borderColor: cardColors.border,
          height
        }
      ]}
    >
      <View style={styles.header}>
        <Text style={[styles.title, { color: cardColors.text }]} numberOfLines={2}>
          {place.name}
        </Text>
        {distanceText ? (
          <Text style={[styles.distance, { color: getDistanceColor(place.distanceMeters) }]}>
            {distanceText}
          </Text>
        ) : null}
      </View>

      {activePhoto ? (
        <Pressable
          accessibilityRole="imagebutton"
          accessibilityLabel={`Open photo of ${place.name}`}
          onPress={() => setPhotoViewerOpen(true)}
          {...photoPanResponder.panHandlers}
        >
          <Image
            source={{ uri: activePhoto }}
            resizeMode="cover"
            style={styles.photo}
            accessibilityLabel={`Photo of ${place.name}`}
            onError={() => {
              setFailedPhotoUrls((current) => new Set(current).add(activePhoto));
            }}
          />
        </Pressable>
      ) : (
        <View style={[styles.photo, styles.placeholder, { backgroundColor: cardColors.placeholder }]}>
          <Ionicons name="location-outline" size={20} color={cardColors.secondary} />
          <Text style={[styles.placeholderText, { color: cardColors.secondary }]}>
            No photo available
          </Text>
        </View>
      )}

      <View style={styles.addressRow}>
        <Ionicons name="location-outline" size={16} color={cardColors.secondary} style={!address ? styles.invisible : null} />
        <Text style={[styles.addressText, { color: cardColors.secondary }, !address ? styles.invisible : null]} numberOfLines={2}>
          {address ?? "Address unavailable"}
        </Text>
      </View>

      <View style={[styles.divider, { backgroundColor: cardColors.separator }]} />

      <View style={styles.detailGrid}>
        <View style={styles.detailColumn}>
          {leftDetailRows}
        </View>

        <View style={[styles.verticalDivider, { backgroundColor: cardColors.separator }]} />

        <View style={styles.detailColumn}>
          {rightDetailRows}
        </View>
      </View>
      <PlacePhotoViewer
        visible={photoViewerOpen}
        photos={photos}
        initialIndex={photoIndex}
        placeName={place.name}
        onIndexChange={setPhotoIndex}
        onClose={() => setPhotoViewerOpen(false)}
      />
    </View>
  );
}

function resolvePhotoUrl(url: string): string {
  return /^https?:\/\//i.test(url) ? url : resolveApiRequestUrl(url);
}

function PlacePhotoViewer({
  visible,
  photos,
  initialIndex,
  placeName,
  onIndexChange,
  onClose
}: {
  visible: boolean;
  photos: string[];
  initialIndex: number;
  placeName: string;
  onIndexChange: (index: number) => void;
  onClose: () => void;
}) {
  const tokens = useThemeTokens();
  const { width, height } = useWindowDimensions();
  const [index, setIndex] = useState(initialIndex);
  const scale = useRef(new Animated.Value(1)).current;
  const translateX = useRef(new Animated.Value(0)).current;
  const translateY = useRef(new Animated.Value(0)).current;
  const lastScaleRef = useRef(1);
  const pinchStartDistanceRef = useRef<number | null>(null);

  const resetZoom = useCallback(() => {
    lastScaleRef.current = 1;
    pinchStartDistanceRef.current = null;
    Animated.parallel([
      Animated.spring(scale, { toValue: 1, useNativeDriver: true }),
      Animated.spring(translateX, { toValue: 0, useNativeDriver: true }),
      Animated.spring(translateY, { toValue: 0, useNativeDriver: true })
    ]).start();
  }, [scale, translateX, translateY]);

  useEffect(() => {
    if (visible) {
      setIndex(Math.max(0, Math.min(photos.length - 1, initialIndex)));
      resetZoom();
    }
  }, [initialIndex, photos.length, resetZoom, visible]);

  const moveToIndex = useCallback((nextIndex: number) => {
    const clamped = Math.max(0, Math.min(photos.length - 1, nextIndex));
    setIndex(clamped);
    onIndexChange(clamped);
    resetZoom();
  }, [onIndexChange, photos.length, resetZoom]);

  const viewerPanResponder = useMemo(
    () =>
      PanResponder.create({
        onMoveShouldSetPanResponder: (_event, gesture) => Math.abs(gesture.dx) > 4 || Math.abs(gesture.dy) > 4,
        onPanResponderGrant: (event) => {
          if (event.nativeEvent.touches.length >= 2) {
            pinchStartDistanceRef.current = getTouchDistance(event.nativeEvent.touches);
          }
        },
        onPanResponderMove: (event, gesture) => {
          if (event.nativeEvent.touches.length >= 2) {
            const start = pinchStartDistanceRef.current ?? getTouchDistance(event.nativeEvent.touches);
            pinchStartDistanceRef.current = start;
            const nextScale = Math.max(1, Math.min(4, lastScaleRef.current * (getTouchDistance(event.nativeEvent.touches) / Math.max(start, 1))));
            scale.setValue(nextScale);
            return;
          }

          if (lastScaleRef.current > 1.02) {
            translateX.setValue(gesture.dx);
            translateY.setValue(gesture.dy);
          }
        },
        onPanResponderRelease: (_event, gesture) => {
          scale.stopAnimation((value) => {
            lastScaleRef.current = Math.max(1, Math.min(4, value));
          });
          pinchStartDistanceRef.current = null;

          if (lastScaleRef.current <= 1.02 && Math.abs(gesture.dx) > 54 && Math.abs(gesture.dx) > Math.abs(gesture.dy)) {
            moveToIndex(index + (gesture.dx < 0 ? 1 : -1));
          }
        }
      }),
    [index, moveToIndex, scale, translateX, translateY]
  );

  if (!visible || photos.length === 0) {
    return null;
  }

  return (
    <Modal visible transparent animationType="fade" onRequestClose={onClose}>
      <Pressable style={styles.viewerBackdrop} onPress={onClose}>
        <Pressable style={[styles.viewerContent, { width, height }]} onPress={resetZoom} {...viewerPanResponder.panHandlers}>
          <Animated.Image
            source={{ uri: photos[index] }}
            resizeMode="contain"
            accessibilityLabel={`Photo of ${placeName}`}
            style={[
              styles.viewerImage,
              {
                transform: [{ scale }, { translateX }, { translateY }]
              }
            ]}
          />
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Close photo"
            style={[styles.viewerClose, { backgroundColor: tokens.surfaces.card, borderColor: tokens.palette.borderStrong }]}
            onPress={onClose}
          >
            <Ionicons name="close" size={24} color={tokens.palette.textPrimary} />
          </Pressable>
        </Pressable>
      </Pressable>
    </Modal>
  );
}

function getTouchDistance(touches: readonly { pageX: number; pageY: number }[]): number {
  if (touches.length < 2) {
    return 1;
  }

  const [first, second] = touches;
  const dx = first.pageX - second.pageX;
  const dy = first.pageY - second.pageY;
  return Math.sqrt((dx * dx) + (dy * dy));
}

type DetailRowProps = {
  icon: keyof typeof Ionicons.glyphMap | keyof typeof MaterialCommunityIcons.glyphMap;
  iconFamily?: "ion" | "material";
  label: string;
  value: string;
  textColor: string;
  mutedColor: string;
  valueColor?: string;
  accessibilityLabel?: string;
  onPress?: () => void;
};

function DetailRow({
  icon,
  iconFamily = "ion",
  label,
  value,
  textColor,
  mutedColor,
  valueColor,
  accessibilityLabel,
  onPress
}: DetailRowProps) {
  const content = (
    <>
      {iconFamily === "material" ? (
        <MaterialCommunityIcons name={icon as keyof typeof MaterialCommunityIcons.glyphMap} size={13} color={mutedColor} />
      ) : (
        <Ionicons name={icon as keyof typeof Ionicons.glyphMap} size={13} color={mutedColor} />
      )}
      <View style={styles.detailTextWrap}>
        <Text style={[styles.detailLabel, { color: textColor }]} numberOfLines={1}>
          {label}
        </Text>
        <Text style={[styles.detailValue, { color: valueColor ?? mutedColor }]} numberOfLines={1}>
          {value}
        </Text>
      </View>
    </>
  );

  if (onPress) {
    return (
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={accessibilityLabel}
        style={({ pressed }) => [styles.detailRow, pressed ? styles.pressed : null]}
        onPress={onPress}
      >
        {content}
      </Pressable>
    );
  }

  return <View style={styles.detailRow}>{content}</View>;
}

function PriceRow({
  activeCount,
  activeColor,
  textColor,
  mutedColor
}: {
  activeCount: 1 | 2 | 3;
  activeColor: string;
  textColor: string;
  mutedColor: string;
}) {
  return (
    <View style={styles.detailRow}>
      <Ionicons name="cash-outline" size={13} color={mutedColor} />
      <View style={styles.detailTextWrap}>
        <Text style={[styles.detailLabel, { color: textColor }]} numberOfLines={1}>
          Price range
        </Text>
        <Text style={styles.priceValue}>
          {[0, 1, 2].map((index) => (
            <Text
              key={index}
              style={{ color: index < activeCount ? activeColor : "rgba(117,117,117,0.48)" }}
            >
              {"\u20ac"}
            </Text>
          ))}
        </Text>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    borderRadius: radius.medium,
    borderWidth: 1,
    padding: spacing[10],
    overflow: "hidden",
    shadowColor: "#000",
    shadowOpacity: 0.18,
    shadowRadius: 12,
    shadowOffset: { width: 0, height: 6 },
    elevation: 3
  },
  header: {
    minHeight: 40,
    flexDirection: "row",
    alignItems: "flex-start",
    gap: spacing[8]
  },
  title: {
    flex: 1,
    ...typography.bodyStrong,
    fontWeight: "700",
    lineHeight: 18
  },
  distance: {
    ...typography.caption,
    fontWeight: "700",
    paddingTop: 1
  },
  photo: {
    width: "100%",
    aspectRatio: 1.62,
    borderRadius: radius.small,
    marginTop: spacing[8]
  },
  placeholder: {
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[6]
  },
  placeholderText: {
    ...typography.caption,
    fontWeight: "500"
  },
  addressRow: {
    minHeight: 34,
    flexDirection: "row",
    alignItems: "flex-start",
    gap: spacing[4],
    marginTop: spacing[8]
  },
  addressText: {
    flex: 1,
    ...typography.caption,
    fontSize: 11,
    lineHeight: 14
  },
  divider: {
    height: StyleSheet.hairlineWidth,
    marginVertical: spacing[8]
  },
  detailGrid: {
    flexDirection: "row",
    gap: spacing[6]
  },
  detailColumn: {
    flex: 1,
    gap: spacing[6]
  },
  verticalDivider: {
    width: StyleSheet.hairlineWidth
  },
  detailRow: {
    minHeight: 32,
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[4]
  },
  detailTextWrap: {
    flex: 1,
    minWidth: 0
  },
  detailLabel: {
    ...typography.caption,
    fontSize: 11,
    fontWeight: "700",
    lineHeight: 13
  },
  detailValue: {
    ...typography.caption,
    fontSize: 11,
    lineHeight: 13,
    marginTop: 1
  },
  priceValue: {
    ...typography.caption,
    lineHeight: 13,
    marginTop: 1
  },
  pressed: {
    opacity: 0.72
  },
  invisible: {
    opacity: 0
  },
  viewerBackdrop: {
    flex: 1,
    backgroundColor: "rgba(0,0,0,0.92)",
    alignItems: "center",
    justifyContent: "center"
  },
  viewerContent: {
    alignItems: "center",
    justifyContent: "center"
  },
  viewerImage: {
    width: "100%",
    height: "100%"
  },
  viewerClose: {
    position: "absolute",
    top: spacing[20],
    right: spacing[20],
    width: 44,
    height: 44,
    borderRadius: radius.medium,
    borderWidth: 1,
    alignItems: "center",
    justifyContent: "center"
  }
});
