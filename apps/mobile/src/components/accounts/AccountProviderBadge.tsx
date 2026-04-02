import { Ionicons } from "@expo/vector-icons";
import { useMemo } from "react";
import { Image, Text, View } from "react-native";
import type { AccountDto } from "../../types/api";
import { palette, spacing, surfaces, typography, createRuntimeStyleSheet, useThemeTokens } from "../../theme/tokens";
import { resolveProviderBadge } from "../../features/accounts/providerBranding";

type AccountProviderBadgeProps = {
  account: Pick<
    AccountDto,
    "providerId" | "providerDisplayName" | "providerIconUrl" | "providerLogoUrl"
  >;
  compact?: boolean;
};

export function AccountProviderBadge({ account, compact = false }: AccountProviderBadgeProps) {
  const { isDarkTheme } = useThemeTokens();
  const resolved = useMemo(
    () =>
      resolveProviderBadge({
        providerId: account.providerId,
        providerDisplayName: account.providerDisplayName,
        providerIconUrl: account.providerIconUrl,
        providerLogoUrl: account.providerLogoUrl
      }),
    [account.providerDisplayName, account.providerIconUrl, account.providerId, account.providerLogoUrl]
  );

  const accessibilityLabel = resolved.displayName
    ? `${resolved.displayName} logo`
    : "Connected bank logo";
  const hasRealArtwork = Boolean(resolved.logoSource);
  const isRevolutDarkWordmark = Boolean(
    hasRealArtwork && isDarkTheme && resolved.bankLogoKey === "revolut"
  );

  return (
    <View
      accessibilityRole="image"
      accessibilityLabel={accessibilityLabel}
      style={[
        styles.badge,
        hasRealArtwork ? styles.badgeArtwork : styles.badgeFallback,
        compact ? styles.badgeCompact : null
      ]}
    >
      {resolved.logoSource ? (
        <View style={isRevolutDarkWordmark ? styles.revolutLogoGlowWrap : null}>
          <Image
            source={resolved.logoSource}
            style={[
              styles.logo,
              compact ? styles.logoCompact : null,
              isRevolutDarkWordmark ? styles.revolutLogoGlow : null
            ]}
            resizeMode="contain"
          />
        </View>
      ) : resolved.monogram ? (
        <Text style={[styles.monogram, compact ? styles.monogramCompact : null]}>{resolved.monogram}</Text>
      ) : (
        <Ionicons
          name="business-outline"
          size={compact ? 15 : 16}
          color={palette.textSecondary}
        />
      )}
    </View>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  badge: {
    minWidth: 58,
    height: 38,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    paddingHorizontal: spacing[10],
    alignItems: "center",
    justifyContent: "center",
    overflow: "hidden"
  },
  badgeArtwork: {
    backgroundColor: surfaces.field
  },
  badgeFallback: {
    backgroundColor: surfaces.fieldStrong
  },
  badgeCompact: {
    minWidth: 46,
    height: 32,
    paddingHorizontal: spacing[8]
  },
  logo: {
    width: 30,
    height: 22
  },
  logoCompact: {
    width: 24,
    height: 18
  },
  revolutLogoGlowWrap: {
    borderRadius: 4,
    backgroundColor: "rgba(255,255,255,0.03)",
    paddingHorizontal: 2,
    paddingVertical: 1
  },
  revolutLogoGlow: {
    shadowColor: "#FFFFFF",
    shadowOpacity: 0.38,
    shadowRadius: 2,
    shadowOffset: { width: 0, height: 0 },
    elevation: 1
  },
  monogram: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "600"
  },
  monogramCompact: {
    fontSize: 11
  }
}));
