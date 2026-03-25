import { Ionicons } from "@expo/vector-icons";
import { useGlobalSearchParams, usePathname, useRouter } from "expo-router";
import { useEffect, useMemo, useRef, useState } from "react";
import {
  Animated,
  Easing,
  Image,
  Linking,
  Modal,
  Pressable,
  StyleSheet,
  Text,
  View
} from "react-native";
import { useUserProfileQuery } from "../../features/users/useUserSettings";
import { externalLinks } from "../../lib/config/externalLinks";
import { useAuthSession } from "../../providers/AuthProvider";
import { layout, palette, spacing, surfaces, typography } from "../../theme/tokens";

type GlobalAppMenuProps = {
  topOffset?: number;
  showTrigger?: boolean;
};

type GlobalMenuListener = () => void;

const globalMenuListeners = new Set<GlobalMenuListener>();

export function requestOpenGlobalAppMenu() {
  globalMenuListeners.forEach((listener) => listener());
}

const menuItems = [
  { label: "Profile", path: "/(tabs)/accounts/profile", icon: "person-outline" },
  { label: "Security", path: "/(tabs)/accounts/security", icon: "shield-checkmark-outline" },
  { label: "Legal & Privacy", path: "/(tabs)/accounts/legal-privacy", icon: "document-text-outline" },
  { label: "Support", path: "/(tabs)/accounts/support", icon: "help-circle-outline" },
  { label: "About", path: "/(tabs)/accounts/about", icon: "information-circle-outline" }
] as const;

function extractInitials(fullName: string) {
  const parts = fullName
    .split(" ")
    .map((part) => part.trim())
    .filter(Boolean)
    .slice(0, 2);

  if (parts.length === 0) {
    return "NS";
  }

  return parts.map((part) => part.charAt(0).toUpperCase()).join("");
}

function formatMemberSince(isoDate?: string | null) {
  if (!isoDate) {
    return "Member since recently";
  }

  const parsed = new Date(isoDate);
  if (Number.isNaN(parsed.getTime())) {
    return "Member since recently";
  }

  return `Member since ${new Intl.DateTimeFormat("en-GB", {
    month: "short",
    year: "numeric"
  }).format(parsed)}`;
}

function formatNsTag(rawValue?: string | null) {
  if (!rawValue) {
    return "@member";
  }

  const normalized = rawValue.trim().replace(/^@+/, "");
  return normalized ? `@${normalized}` : "@member";
}

function resolveHubContext(pathname: string, source?: string | null): "finance" | "planning" {
  if (pathname.startsWith("/planning")) {
    return "planning";
  }

  if (pathname.startsWith("/calendar") || pathname.startsWith("/companion")) {
    if (source === "planningHub" || source === "expense") {
      return "planning";
    }
  }

  return "finance";
}

export function GlobalAppMenu({ topOffset = 8, showTrigger = true }: GlobalAppMenuProps) {
  const router = useRouter();
  const pathname = usePathname();
  const params = useGlobalSearchParams<{ source?: string }>();
  const { isAuthenticated, session, logout } = useAuthSession();
  const profileQuery = useUserProfileQuery();
  const [isOpen, setIsOpen] = useState(false);
  const slideProgress = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    setIsOpen(false);
  }, [pathname]);

  useEffect(() => {
    const listener = () => setIsOpen(true);
    globalMenuListeners.add(listener);

    return () => {
      globalMenuListeners.delete(listener);
    };
  }, []);

  useEffect(() => {
    Animated.timing(slideProgress, {
      toValue: isOpen ? 1 : 0,
      duration: 220,
      easing: Easing.out(Easing.cubic),
      useNativeDriver: true
    }).start();
  }, [isOpen, slideProgress]);

  const activePath = useMemo(() => pathname || "", [pathname]);
  const sourceContext = typeof params.source === "string" ? params.source : null;
  const hubContext = useMemo(
    () => resolveHubContext(activePath, sourceContext),
    [activePath, sourceContext]
  );
  const highlightFinanceHub = hubContext === "planning";
  const highlightPlanningHub = hubContext === "finance";
  const canSwitchToFinanceHub = highlightFinanceHub;
  const canSwitchToPlanningHub = highlightPlanningHub;

  const profile = profileQuery.data;
  const fullName = profile?.fullName || session?.user.fullName || session?.user.displayName || "NSFinance user";
  const displayName = formatNsTag(profile?.displayName || session?.user.displayName || null);
  const subtitle =
    profile?.profileSubtitle?.trim() ||
    session?.user.profileSubtitle?.trim() ||
    formatMemberSince(profile?.createdUtc ?? session?.user.createdUtc);
  const profileImageUrl = profile?.profileImageUrl ?? session?.user.profileImageUrl ?? null;
  const initials = extractInitials(fullName);

  if (!isAuthenticated) {
    return null;
  }

  const panelTranslateX = slideProgress.interpolate({
    inputRange: [0, 1],
    outputRange: [360, 0]
  });

  const overlayOpacity = slideProgress.interpolate({
    inputRange: [0, 1],
    outputRange: [0, 1]
  });

  return (
    <>
      <View style={[styles.triggerWrap, { top: topOffset }]}> 
        {showTrigger ? (
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Open settings menu"
            onPress={() => setIsOpen(true)}
            style={({ pressed }) => [styles.trigger, pressed ? styles.triggerPressed : null]}
          >
            <Ionicons name="menu-outline" size={20} color={palette.textPrimary} />
          </Pressable>
        ) : null}
      </View>

      <Modal
        visible={isOpen}
        transparent
        animationType="none"
        statusBarTranslucent
        onRequestClose={() => setIsOpen(false)}
      >
        <View style={styles.modalRoot}>
          <Animated.View style={[styles.overlay, { opacity: overlayOpacity }]}>
            <Pressable style={StyleSheet.absoluteFill} onPress={() => setIsOpen(false)} />
          </Animated.View>

          <Animated.View
            style={[
              styles.drawer,
              {
                transform: [{ translateX: panelTranslateX }]
              }
            ]}
          >
            <View style={styles.profileHeader}>
              <View style={styles.avatarWrap}>
                {profileImageUrl ? (
                  <Image source={{ uri: profileImageUrl }} style={styles.avatarImage} />
                ) : (
                  <Text style={styles.avatarText}>{initials}</Text>
                )}
              </View>
              <View style={styles.profileMeta}>
                <Text style={styles.fullName}>{fullName}</Text>
                <Text style={styles.handle}>{displayName}</Text>
                <Text style={styles.subtitle} numberOfLines={2}>
                  {subtitle}
                </Text>
              </View>
            </View>

            <View style={styles.menuItems}>
              {menuItems.map((item) => {
                const normalizedPath = item.path.replace("/(tabs)", "");
                const isActive = activePath.includes(normalizedPath);
                return (
                  <Pressable
                    key={item.path}
                    onPress={() => {
                      setIsOpen(false);
                      router.push(item.path as never);
                    }}
                    style={({ pressed }) => [
                      styles.menuItem,
                      isActive ? styles.menuItemActive : null,
                      pressed ? styles.menuItemPressed : null
                    ]}
                  >
                    <Ionicons
                      name={item.icon}
                      size={16}
                      color={isActive ? palette.accent : palette.textSecondary}
                    />
                    <Text style={[styles.menuItemText, isActive ? styles.menuItemTextActive : null]}>
                      {item.label}
                    </Text>
                  </Pressable>
                );
              })}
            </View>

            <View style={styles.bottomCluster}>
              <View style={styles.hubSwitcherRow}>
                <Pressable
                  disabled={!canSwitchToFinanceHub}
                  onPress={() => {
                    setIsOpen(false);
                    router.push("/(tabs)" as never);
                  }}
                  style={({ pressed }) => [
                    styles.hubButton,
                    styles.financeHubButton,
                    highlightFinanceHub ? styles.hubButtonHighlighted : styles.hubButtonDimmed,
                    pressed && canSwitchToFinanceHub ? styles.menuItemPressed : null
                  ]}
                >
                  <Text style={styles.hubButtonText}>Finance Hub</Text>
                </Pressable>

                <Pressable
                  disabled={!canSwitchToPlanningHub}
                  onPress={() => {
                    setIsOpen(false);
                    router.push("/(tabs)/planning" as never);
                  }}
                  style={({ pressed }) => [
                    styles.hubButton,
                    styles.planningHubButton,
                    highlightPlanningHub ? styles.hubButtonHighlighted : styles.hubButtonDimmed,
                    pressed && canSwitchToPlanningHub ? styles.menuItemPressed : null
                  ]}
                >
                  <Text style={styles.hubButtonText}>Planning Hub</Text>
                </Pressable>
              </View>

              <View style={styles.footer}>
                <Pressable
                  onPress={() => {
                    setIsOpen(false);
                    void logout();
                  }}
                  style={({ pressed }) => [styles.logoutButton, pressed ? styles.menuItemPressed : null]}
                >
                  <Ionicons name="log-out-outline" size={16} color={palette.textPrimary} />
                  <Text style={styles.logoutText}>Log Out</Text>
                </Pressable>

                <View style={styles.linkIconRow}>
                  <Pressable
                    accessibilityRole="button"
                    accessibilityLabel="Instagram"
                    disabled={!externalLinks.instagram}
                    onPress={() => {
                      if (externalLinks.instagram) {
                        void Linking.openURL(externalLinks.instagram);
                      }
                    }}
                    style={({ pressed }) => [
                      styles.linkIconButton,
                      !externalLinks.instagram ? styles.linkIconDisabled : null,
                      pressed ? styles.menuItemPressed : null
                    ]}
                  >
                    <Ionicons name="logo-instagram" size={18} color={palette.textPrimary} />
                  </Pressable>
                  <Pressable
                    accessibilityRole="button"
                    accessibilityLabel="Website"
                    onPress={() => {
                      void Linking.openURL(externalLinks.website);
                    }}
                    style={({ pressed }) => [styles.linkIconButton, pressed ? styles.menuItemPressed : null]}
                  >
                    <Ionicons name="globe-outline" size={18} color={palette.textPrimary} />
                  </Pressable>
                </View>
              </View>
            </View>
          </Animated.View>
        </View>
      </Modal>
    </>
  );
}

const styles = StyleSheet.create({
  triggerWrap: {
    position: "absolute",
    right: layout.screenHorizontalPadding,
    zIndex: 30
  },
  trigger: {
    width: 42,
    height: 42,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(17,17,17,0.92)",
    alignItems: "center",
    justifyContent: "center"
  },
  triggerPressed: {
    opacity: 0.86,
    transform: [{ scale: 0.96 }]
  },
  modalRoot: {
    flex: 1,
    justifyContent: "flex-start",
    alignItems: "flex-end"
  },
  overlay: {
    ...StyleSheet.absoluteFillObject,
    backgroundColor: palette.overlay
  },
  drawer: {
    width: "84%",
    maxWidth: 340,
    minHeight: "100%",
    backgroundColor: surfaces.app,
    borderLeftWidth: 1,
    borderLeftColor: palette.border,
    paddingTop: 76,
    paddingHorizontal: spacing[16],
    paddingBottom: spacing[24],
    gap: spacing[16]
  },
  profileHeader: {
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.card,
    padding: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[12]
  },
  avatarWrap: {
    width: 54,
    height: 54,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: "rgba(242,140,40,0.8)",
    backgroundColor: "rgba(242,140,40,0.12)",
    alignItems: "center",
    justifyContent: "center"
  },
  avatarText: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "600"
  },
  avatarImage: {
    width: "100%",
    height: "100%",
    borderRadius: 6
  },
  profileMeta: {
    flex: 1,
    gap: 2
  },
  fullName: {
    color: palette.textPrimary,
    ...typography.bodyStrong
  },
  handle: {
    color: palette.accent,
    ...typography.caption
  },
  subtitle: {
    color: palette.textSecondary,
    ...typography.caption
  },
  menuItems: {
    gap: spacing[8]
  },
  menuItem: {
    minHeight: 44,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  menuItemActive: {
    borderColor: palette.borderStrong,
    backgroundColor: "rgba(242,140,40,0.12)"
  },
  menuItemText: {
    color: palette.textPrimary,
    ...typography.body1
  },
  menuItemTextActive: {
    fontWeight: "600"
  },
  menuItemPressed: {
    opacity: 0.86
  },
  bottomCluster: {
    marginTop: "auto",
    gap: spacing[8]
  },
  hubSwitcherRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  hubButton: {
    flex: 1,
    minHeight: 40,
    borderRadius: 6,
    borderWidth: 1,
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: spacing[10]
  },
  financeHubButton: {
    borderColor: palette.border,
    backgroundColor: surfaces.field
  },
  planningHubButton: {
    borderColor: palette.border,
    backgroundColor: surfaces.field
  },
  hubButtonHighlighted: {
    opacity: 1,
    borderColor: palette.borderStrong,
    backgroundColor: "rgba(242,140,40,0.12)"
  },
  hubButtonDimmed: {
    opacity: 0.38
  },
  hubButtonText: {
    color: palette.textPrimary,
    ...typography.body2
  },
  footer: {
    paddingTop: spacing[12],
    borderTopWidth: 1,
    borderTopColor: palette.border,
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  logoutButton: {
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "center",
    gap: spacing[8],
    minHeight: 38,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    paddingHorizontal: spacing[12]
  },
  logoutText: {
    color: palette.textPrimary,
    ...typography.body2
  },
  linkIconRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  linkIconButton: {
    width: 36,
    height: 36,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.field,
    alignItems: "center",
    justifyContent: "center"
  },
  linkIconDisabled: {
    opacity: 0.45
  }
});

