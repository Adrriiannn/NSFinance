import { Ionicons } from "@expo/vector-icons";
import { useEffect, useMemo, useRef, useState } from "react";
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

type LogoRenderMode = "svg" | "image";

type ProviderLogoRenderAttempt = {
  uri: string;
  mode: LogoRenderMode;
};

export function AccountProviderBadge({ account, compact = false }: AccountProviderBadgeProps) {
  const [attemptIndex, setAttemptIndex] = useState(0);
  const lastDebugSignatureRef = useRef<string | null>(null);
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

  const renderAttempts = useMemo(
    () => buildProviderLogoRenderAttempts(resolved.remoteIconUrls),
    [resolved.remoteIconUrls]
  );
  const activeAttempt = renderAttempts[attemptIndex] ?? null;

  useEffect(() => {
    setAttemptIndex(0);
  }, [renderAttempts]);

  useEffect(() => {
    if (!__DEV__) {
      return;
    }

    const signature = [
      account.providerId ?? "",
      account.providerDisplayName ?? "",
      resolved.remoteIconUrls.join("|"),
      String(attemptIndex)
    ].join("::");

    if (lastDebugSignatureRef.current === signature) {
      return;
    }

    if (renderAttempts.length === 0 && (account.providerId || account.providerDisplayName)) {
      console.info("[ProviderBadge]", {
        event: "logo_missing",
        providerId: account.providerId ?? null,
        providerDisplayName: account.providerDisplayName ?? null,
        canonicalProviderKey: resolved.canonicalProviderKey
      });
      lastDebugSignatureRef.current = signature;
      return;
    }

    if (attemptIndex >= renderAttempts.length && renderAttempts.length > 0) {
      console.info("[ProviderBadge]", {
        event: "logo_attempts_exhausted",
        providerId: account.providerId ?? null,
        providerDisplayName: account.providerDisplayName ?? null,
        canonicalProviderKey: resolved.canonicalProviderKey,
        attemptedUris: resolved.remoteIconUrls
      });
      lastDebugSignatureRef.current = signature;
    }
  }, [
    account.providerDisplayName,
    account.providerId,
    attemptIndex,
    renderAttempts.length,
    resolved.canonicalProviderKey,
    resolved.remoteIconUrls
  ]);

  const accessibilityLabel = resolved.displayName
    ? `${resolved.displayName} logo`
    : "Connected bank logo";
  const hasRealArtwork = Boolean(activeAttempt);
  const goToNextAttempt = () => setAttemptIndex((current) => current + 1);

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
      {activeAttempt ? (
        activeAttempt.mode === "svg" ? (
          <SvgUri
            uri={activeAttempt.uri}
            width={compact ? 24 : 30}
            height={compact ? 18 : 22}
            onError={goToNextAttempt}
          />
        ) : (
          <Image
            source={{ uri: activeAttempt.uri }}
            style={[styles.logo, compact ? styles.logoCompact : null]}
            resizeMode="contain"
            onError={goToNextAttempt}
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

function buildProviderLogoRenderAttempts(uris: string[]): ProviderLogoRenderAttempt[] {
  const attempts: ProviderLogoRenderAttempt[] = [];
  for (const uri of uris) {
    const renderMode = inferProviderLogoRenderMode(uri);
    if (renderMode === "svg") {
      attempts.push({ uri, mode: "svg" });
      continue;
    }

    if (renderMode === "image") {
      attempts.push({ uri, mode: "image" });
      continue;
    }

    attempts.push({ uri, mode: "svg" });
    attempts.push({ uri, mode: "image" });
  }

  return attempts;
}

function inferProviderLogoRenderMode(uri: string): LogoRenderMode | "unknown" {
  if (/^data:image\/svg\+xml/i.test(uri)) {
    return "svg";
  }

  if (/^data:image\/(?:png|jpe?g|webp|gif|bmp|avif|heic|heif)/i.test(uri)) {
    return "image";
  }

  const extensionMatch = uri.match(/\.([a-z0-9]+)(?:$|[?#])/i);
  const extension = extensionMatch?.[1]?.toLowerCase();
  if (!extension) {
    return "unknown";
  }

  if (extension === "svg" || extension === "svgz") {
    return "svg";
  }

  if (["png", "jpg", "jpeg", "webp", "gif", "bmp", "avif", "heic", "heif"].includes(extension)) {
    return "image";
  }

  return "unknown";
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
  monogram: {
    color: palette.textPrimary,
    ...typography.caption,
    fontWeight: "600"
  },
  monogramCompact: {
    fontSize: 11
  }
}));
