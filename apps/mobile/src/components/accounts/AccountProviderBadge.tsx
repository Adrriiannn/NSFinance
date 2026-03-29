import { Ionicons } from "@expo/vector-icons";
import { useEffect, useMemo, useState } from "react";
import { Image, Text, View } from "react-native";
import { SvgUri } from "react-native-svg";
import type { AccountDto } from "../../types/api";
import { palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../theme/tokens";
import { resolveProviderBadge } from "../../features/accounts/providerBranding";

type AccountProviderBadgeProps = {
  account: Pick<
    AccountDto,
    "providerId" | "providerDisplayName" | "providerIconUrl" | "providerLogoUrl"
  >;
  compact?: boolean;
};

export function AccountProviderBadge({ account, compact = false }: AccountProviderBadgeProps) {
  const [remoteImageIndex, setRemoteImageIndex] = useState(0);
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
  const activeRemoteIconUrl = resolved.remoteIconUrls[remoteImageIndex] ?? null;

  useEffect(() => {
    setRemoteImageIndex(0);
  }, [resolved.remoteIconUrls]);

  const isSvgLogo = Boolean(activeRemoteIconUrl && /\.svg(?:$|[?#])/i.test(activeRemoteIconUrl));
  const accessibilityLabel = resolved.displayName
    ? `${resolved.displayName} logo`
    : "Connected bank logo";

  return (
    <View
      accessibilityRole="image"
      accessibilityLabel={accessibilityLabel}
      style={[styles.badge, compact ? styles.badgeCompact : null]}
    >
      {activeRemoteIconUrl ? (
        isSvgLogo ? (
          <SvgUri
            uri={activeRemoteIconUrl}
            width={compact ? 24 : 30}
            height={compact ? 18 : 22}
            onError={() => setRemoteImageIndex((current) => current + 1)}
          />
        ) : (
          <Image
            source={{ uri: activeRemoteIconUrl }}
            style={[styles.logo, compact ? styles.logoCompact : null]}
            resizeMode="contain"
            onError={() => setRemoteImageIndex((current) => current + 1)}
          />
        )
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
    backgroundColor: surfaces.fieldStrong,
    paddingHorizontal: spacing[10],
    alignItems: "center",
    justifyContent: "center",
    overflow: "hidden"
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
  monogram: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "600"
  },
  monogramCompact: {
    fontSize: 11
  }
}));

