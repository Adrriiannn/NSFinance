import { Ionicons, MaterialCommunityIcons } from "@expo/vector-icons";
import { Image, Pressable, StyleSheet, Text, View } from "react-native";
import { radius, spacing, typography, useThemeTokens } from "../../../theme/tokens";
import {
  type CompanionPlaceCard,
  formatDistanceKm,
  formatDuration,
  formatPhoneDisplay,
  formatPriceLevel,
  formatRating,
  formatWebsiteDisplay,
  getDistanceColor,
  humanizeCategory
} from "../utils/placeCardFormatting";

type PlaceResultCardProps = {
  place: CompanionPlaceCard;
  onOpenWebsite?: (place: CompanionPlaceCard) => void;
  onOpenMenu?: (place: CompanionPlaceCard) => void;
  onCall?: (place: CompanionPlaceCard) => void;
};

export function PlaceResultCard({
  place,
  onOpenWebsite,
  onOpenMenu,
  onCall
}: PlaceResultCardProps) {
  const tokens = useThemeTokens();
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

  return (
    <View
      accessibilityLabel={`Place card for ${place.name}`}
      style={[
        styles.card,
        {
          backgroundColor: cardColors.background,
          borderColor: cardColors.border
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

      {place.photoUrl ? (
        <Image
          source={{ uri: place.photoUrl }}
          resizeMode="cover"
          style={styles.photo}
          accessibilityLabel={`Photo of ${place.name}`}
        />
      ) : (
        <View style={[styles.photo, styles.placeholder, { backgroundColor: cardColors.placeholder }]}>
          <Ionicons name="location-outline" size={20} color={cardColors.secondary} />
          <Text style={[styles.placeholderText, { color: cardColors.secondary }]}>
            No photo available
          </Text>
        </View>
      )}

      {address ? (
        <View style={styles.addressRow}>
          <Ionicons name="location-outline" size={16} color={cardColors.secondary} />
          <Text style={[styles.addressText, { color: cardColors.secondary }]} numberOfLines={2}>
            {address}
          </Text>
        </View>
      ) : null}

      <View style={[styles.divider, { backgroundColor: cardColors.separator }]} />

      <View style={styles.detailGrid}>
        <View style={styles.detailColumn}>
          {rating ? (
            <DetailRow
              icon="star-outline"
              label="Rating"
              value={rating}
              textColor={cardColors.text}
              mutedColor={cardColors.secondary}
            />
          ) : null}
          {typeof place.openNow === "boolean" ? (
            <DetailRow
              icon="time-outline"
              label="Open now"
              value={place.openNow ? "Open" : "Closed"}
              valueColor={place.openNow ? tokens.palette.success : tokens.palette.negative}
              textColor={cardColors.text}
              mutedColor={cardColors.secondary}
            />
          ) : null}
          {price ? (
            <PriceRow
              activeCount={price.activeCount}
              textColor={cardColors.text}
              mutedColor={cardColors.secondary}
            />
          ) : null}
          {websiteDisplay ? (
            <DetailRow
              icon="globe-outline"
              label="Website"
              value={websiteDisplay}
              valueColor={tokens.palette.success}
              textColor={cardColors.text}
              mutedColor={cardColors.secondary}
              accessibilityLabel={`Open website for ${place.name}`}
              onPress={() => onOpenWebsite?.(place)}
            />
          ) : null}
        </View>

        <View style={[styles.verticalDivider, { backgroundColor: cardColors.separator }]} />

        <View style={styles.detailColumn}>
          {category ? (
            <DetailRow
              icon="pricetag-outline"
              label="Category"
              value={category}
              textColor={cardColors.text}
              mutedColor={cardColors.secondary}
            />
          ) : null}
          {closesIn || opensIn ? (
            <DetailRow
              icon="alarm-outline"
              label={closesIn ? "Closes in" : "Opens in"}
              value={closesIn ?? opensIn ?? ""}
              valueColor={closesIn ? tokens.palette.negative : tokens.palette.success}
              textColor={cardColors.text}
              mutedColor={cardColors.secondary}
            />
          ) : null}
          {phoneDisplay ? (
            <DetailRow
              icon="call-outline"
              label="Call now"
              value={phoneDisplay}
              valueColor={tokens.palette.success}
              textColor={cardColors.text}
              mutedColor={cardColors.secondary}
              accessibilityLabel={`Call ${place.name}`}
              onPress={() => onCall?.(place)}
            />
          ) : null}
          {menuDisplay ? (
            <DetailRow
              icon="silverware-fork-knife"
              iconFamily="material"
              label="Menu"
              value={menuDisplay}
              valueColor={tokens.palette.success}
              textColor={cardColors.text}
              mutedColor={cardColors.secondary}
              accessibilityLabel={`Open menu for ${place.name}`}
              onPress={() => onOpenMenu?.(place)}
            />
          ) : null}
        </View>
      </View>
    </View>
  );
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
        <Text style={[styles.detailValue, { color: valueColor ?? mutedColor }]} numberOfLines={2}>
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
  textColor,
  mutedColor
}: {
  activeCount: 1 | 2 | 3;
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
              style={{ color: index < activeCount ? "#D79A24" : "rgba(117,117,117,0.48)" }}
            >
              €
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
    shadowColor: "#000",
    shadowOpacity: 0.18,
    shadowRadius: 12,
    shadowOffset: { width: 0, height: 6 },
    elevation: 3
  },
  header: {
    minHeight: 30,
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
    alignItems: "flex-start",
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
  }
});
