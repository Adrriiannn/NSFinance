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
  const [remoteImageFailed, setRemoteImageFailed] = useState(false);
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

  useEffect(() => {
    setRemoteImageFailed(false);
  }, [resolved.remoteIconUrl]);

  const remoteIconUrl = remoteImageFailed ? null : resolved.remoteIconUrl;
  const isSvgLogo = Boolean(remoteIconUrl && /\.svg(?:$|[?#])/i.test(remoteIconUrl));
  const accessibilityLabel = resolved.displayName
    ? `${resolved.displayName} logo`
    : "Connected bank logo";

  return (
    <View
      accessibilityRole="image"
      accessibilityLabel={accessibilityLabel}
      style={[styles.badge, compact ? styles.badgeCompact : null]}
    >
      {remoteIconUrl ? (
        isSvgLogo ? (
          <SvgUri
            uri={remoteIconUrl}
            width={compact ? 24 : 30}
            height={compact ? 18 : 22}
            onError={() => setRemoteImageFailed(true)}
          />
        ) : (
          <Image
            source={{ uri: remoteIconUrl }}
            style={[styles.logo, compact ? styles.logoCompact : null]}
            resizeMode="contain"
            onError={() => setRemoteImageFailed(true)}
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

