import { Ionicons } from "@expo/vector-icons";
import { LinearGradient } from "expo-linear-gradient";
import { usePathname, useRouter } from "expo-router";
import { useEffect, useMemo, useRef, useState } from "react";
import {
  AccessibilityInfo,
  Animated,
  Easing,
  Image,
  Linking,
  Pressable,
  StyleSheet,
  Text,
  View
} from "react-native";
import { SystemModal } from "../ui/surfaces/SystemModal";
import { useUserProfileQuery } from "../../features/users/useUserSettings";
import { externalLinks } from "../../lib/config/externalLinks";
import { useAuthSession } from "../../providers/AuthProvider";
import { useThemeRuntime } from "../../theme/runtime/ThemeRuntimeProvider";
import { layout, palette, spacing, surfaces, typography, createRuntimeStyleSheet } from "../../theme/tokens";

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
  { label: "Statements", path: "/(tabs)/accounts/statements", icon: "document-attach-outline" },
  { label: "Security", path: "/(tabs)/accounts/security", icon: "shield-checkmark-outline" },
  { label: "Legal & Privacy", path: "/(tabs)/accounts/legal-privacy", icon: "document-text-outline" },
  { label: "Support", path: "/(tabs)/accounts/support", icon: "help-circle-outline" },
  { label: "About", path: "/(tabs)/accounts/about", icon: "information-circle-outline" }
] as const;

const THEME_CONTROL_WIDTH = 60;
const THEME_TOGGLE_HEIGHT = 20;
const THEME_TOGGLE_THUMB_SIZE = 14;
const THEME_TOGGLE_SIDE_PADDING = 2;

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

export function GlobalAppMenu({ topOffset = 8, showTrigger = true }: GlobalAppMenuProps) {
  const router = useRouter();
  const pathname = usePathname();
  const { isAuthenticated, session, logout } = useAuthSession();
  const { mode, resolvedThemeName, setThemeMode, isTransitioning } = useThemeRuntime();
  const profileQuery = useUserProfileQuery();
  const [isOpen, setIsOpen] = useState(false);
  const [reducedMotionEnabled, setReducedMotionEnabled] = useState(false);
  const slideProgress = useRef(new Animated.Value(0)).current;
  const themeToggleProgress = useRef(new Animated.Value(resolvedThemeName === "dark" ? 1 : 0)).current;

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

  useEffect(() => {
    let isMounted = true;

    AccessibilityInfo.isReduceMotionEnabled()
      .then((enabled) => {
        if (isMounted) {
          setReducedMotionEnabled(enabled);
        }
      })
      .catch(() => {
        if (isMounted) {
          setReducedMotionEnabled(false);
        }
      });

    const subscription = AccessibilityInfo.addEventListener("reduceMotionChanged", (enabled) => {
      setReducedMotionEnabled(enabled);
    });

    return () => {
      isMounted = false;
      subscription.remove();
    };
  }, []);

  const activePath = useMemo(() => pathname || "", [pathname]);

  const profile = profileQuery.data;
  const fullName = profile?.fullName || session?.user.fullName || session?.user.displayName || "NSFinance user";
  const displayName = formatNsTag(profile?.displayName || session?.user.displayName || null);
  const subtitle =
    profile?.profileSubtitle?.trim() ||
    session?.user.profileSubtitle?.trim() ||
    formatMemberSince(profile?.createdUtc ?? session?.user.createdUtc);
  const profileImageUrl = profile?.profileImageUrl ?? session?.user.profileImageUrl ?? null;
  const initials = extractInitials(fullName);
  const effectiveThemeName = mode === "system" ? resolvedThemeName : mode;
  const systemModeEnabled = mode === "system";

  useEffect(() => {
    Animated.timing(themeToggleProgress, {
      toValue: effectiveThemeName === "dark" ? 1 : 0,
      duration: reducedMotionEnabled ? 120 : 230,
      easing: Easing.out(Easing.cubic),
      useNativeDriver: true
    }).start();
  }, [effectiveThemeName, reducedMotionEnabled, themeToggleProgress]);

  const setManualTheme = (nextTheme: "light" | "dark") => {
    if (isTransitioning) {
      return;
    }

    setThemeMode(nextTheme);
  };

  const toggleToOppositeTheme = () => {
    setManualTheme(effectiveThemeName === "dark" ? "light" : "dark");
  };

  const toggleSystemMode = () => {
    if (isTransitioning) {
      return;
    }

    if (systemModeEnabled) {
      setThemeMode(effectiveThemeName);
      return;
    }

    setThemeMode("system");
  };

  const thumbTranslateX = themeToggleProgress.interpolate({
    inputRange: [0, 1],
    outputRange: [
      THEME_TOGGLE_SIDE_PADDING,
      THEME_CONTROL_WIDTH - THEME_TOGGLE_THUMB_SIZE - THEME_TOGGLE_SIDE_PADDING
    ]
  });

  const sunOpacity = themeToggleProgress.interpolate({
    inputRange: [0, 0.35, 1],
    outputRange: [1, 0.2, 0]
  });

  const moonOpacity = themeToggleProgress.interpolate({
    inputRange: [0, 0.65, 1],
    outputRange: [0, 0.2, 1]
  });

  const sunScale = themeToggleProgress.interpolate({
    inputRange: [0, 1],
    outputRange: [1, 0.9]
  });

  const moonScale = themeToggleProgress.interpolate({
    inputRange: [0, 1],
    outputRange: [0.9, 1]
  });

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

      <SystemModal
        visible={isOpen}
        transparent
        animationType="none"
        statusBarTranslucent
        safeAreaEdges={["bottom", "left", "right"]}
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
                <View style={styles.profileIdentityCopy}>
                  <Text style={styles.fullName}>{fullName}</Text>
                  <Text style={styles.handle}>{displayName}</Text>
                </View>
                <Text style={styles.subtitle} numberOfLines={1}>
                  {subtitle}
                </Text>
              </View>
              <View style={styles.themeControlWrap}>
                <Pressable
                  accessibilityRole="switch"
                  accessibilityLabel="Theme toggle. Tap anywhere to switch between light and dark theme."
                  accessibilityState={{
                    checked: effectiveThemeName === "dark",
                    disabled: isTransitioning
                  }}
                  disabled={isTransitioning}
                  onPress={toggleToOppositeTheme}
                  style={({ pressed }) => [
                    styles.themeToggleTrack,
                    effectiveThemeName === "dark"
                      ? styles.themeToggleTrackDark
                      : styles.themeToggleTrackLight,
                    pressed && !isTransitioning ? styles.menuItemPressed : null,
                    isTransitioning ? styles.themeControlDisabled : null
                  ]}
                >
                  <LinearGradient
                    pointerEvents="none"
                    colors={
                      effectiveThemeName === "dark"
                        ? ["rgba(13,17,26,0.98)", "rgba(20,24,34,0.98)"]
                        : ["rgba(247,242,233,0.98)", "rgba(245,236,221,0.98)"]
                    }
                    start={{ x: 0, y: 0.5 }}
                    end={{ x: 1, y: 0.5 }}
                    style={StyleSheet.absoluteFillObject}
                  />

                  <Animated.View
                    pointerEvents="none"
                    style={[
                      styles.themeThumb,
                      {
                        transform: [{ translateX: thumbTranslateX }]
                      }
                    ]}
                  >
                    <View style={styles.thumbPressable}>
                      <Animated.View
                        style={[
                          styles.thumbFace,
                          {
                            opacity: sunOpacity,
                            transform: [{ scale: sunScale }]
                          }
                        ]}
                      >
                        <LinearGradient
                          colors={["#FFBE5D", "#F28C28", "#CD6B09"]}
                          start={{ x: 0.1, y: 0.1 }}
                          end={{ x: 0.9, y: 0.9 }}
                          style={styles.sunThumb}
                        />
                      </Animated.View>

                      <Animated.View
                        style={[
                          styles.thumbFace,
                          styles.moonThumb,
                          {
                            opacity: moonOpacity,
                            transform: [{ scale: moonScale }]
                          }
                        ]}
                      >
                        <View style={styles.moonCraterOne} />
                        <View style={styles.moonCraterTwo} />
                        <View style={styles.moonCraterThree} />
                      </Animated.View>
                    </View>
                  </Animated.View>
                </Pressable>

                <Pressable
                  accessibilityRole="checkbox"
                  accessibilityLabel="Follow system theme"
                  accessibilityState={{
                    checked: systemModeEnabled,
                    disabled: isTransitioning
                  }}
                  disabled={isTransitioning}
                  onPress={toggleSystemMode}
                  style={({ pressed }) => [
                    styles.systemRow,
                    pressed && !isTransitioning ? styles.menuItemPressed : null,
                    isTransitioning ? styles.themeControlDisabled : null
                  ]}
                >
                  <View
                    style={[
                      styles.systemCheckbox,
                      systemModeEnabled ? styles.systemCheckboxChecked : null
                    ]}
                  >
                    {systemModeEnabled ? <Ionicons name="checkmark" size={9} color="#FFFFFF" /> : null}
                  </View>
                  <Text style={styles.systemText} numberOfLines={1}>
                    System
                  </Text>
                </Pressable>
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
              <View style={styles.footer}>
                <Pressable
                  onPress={() => {
                    setIsOpen(false);
                    void (async () => {
                      await logout();
                      router.replace("/login" as never);
                    })();
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
      </SystemModal>
    </>
  );
}

const styles = createRuntimeStyleSheet(() => ({
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
    backgroundColor: surfaces.field,
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
    position: "relative",
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: surfaces.card,
    padding: spacing[12],
    flexDirection: "row",
    alignItems: "flex-start",
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
    minWidth: 0,
    gap: 2
  },
  profileIdentityCopy: {
    paddingRight: THEME_CONTROL_WIDTH + spacing[8]
  },
  themeControlWrap: {
    position: "absolute",
    top: spacing[10],
    right: spacing[10],
    width: THEME_CONTROL_WIDTH,
    gap: 2
  },
  themeToggleTrack: {
    width: THEME_CONTROL_WIDTH,
    height: THEME_TOGGLE_HEIGHT,
    borderRadius: 6,
    borderWidth: 1,
    borderColor: palette.border,
    overflow: "hidden",
    justifyContent: "center"
  },
  themeToggleTrackLight: {
    backgroundColor: "rgba(247, 242, 233, 0.9)"
  },
  themeToggleTrackDark: {
    backgroundColor: "rgba(12, 16, 24, 0.95)"
  },
  themeThumb: {
    position: "absolute",
    top: THEME_TOGGLE_SIDE_PADDING,
    left: 0,
    width: THEME_TOGGLE_THUMB_SIZE,
    height: THEME_TOGGLE_THUMB_SIZE
  },
  thumbPressable: {
    width: "100%",
    height: "100%",
    borderRadius: THEME_TOGGLE_THUMB_SIZE / 2,
    overflow: "hidden"
  },
  thumbFace: {
    ...StyleSheet.absoluteFillObject,
    alignItems: "center",
    justifyContent: "center",
    borderRadius: THEME_TOGGLE_THUMB_SIZE / 2
  },
  sunThumb: {
    ...StyleSheet.absoluteFillObject
  },
  moonThumb: {
    backgroundColor: "#F2F2EE",
    borderWidth: 1,
    borderColor: "rgba(157, 157, 157, 0.48)"
  },
  moonCraterOne: {
    position: "absolute",
    width: 4,
    height: 4,
    borderRadius: 2,
    top: 4,
    left: 4,
    backgroundColor: "rgba(188, 188, 188, 0.55)"
  },
  moonCraterTwo: {
    position: "absolute",
    width: 3,
    height: 3,
    borderRadius: 2,
    top: 9,
    right: 4,
    backgroundColor: "rgba(176, 176, 176, 0.45)"
  },
  moonCraterThree: {
    position: "absolute",
    width: 2,
    height: 2,
    borderRadius: 2,
    bottom: 4,
    left: 7,
    backgroundColor: "rgba(173, 173, 173, 0.42)"
  },
  systemRow: {
    width: THEME_CONTROL_WIDTH,
    minHeight: 14,
    flexDirection: "row",
    alignItems: "center",
    gap: 3
  },
  systemCheckbox: {
    width: 12,
    height: 12,
    borderRadius: 3,
    borderWidth: 1,
    borderColor: palette.borderStrong,
    backgroundColor: surfaces.field,
    alignItems: "center",
    justifyContent: "center"
  },
  systemCheckboxChecked: {
    borderColor: palette.accent,
    backgroundColor: palette.accent
  },
  systemText: {
    color: palette.textSecondary,
    ...typography.caption
  },
  themeControlDisabled: {
    opacity: 0.56
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
    ...typography.caption,
    flexShrink: 1
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
}));


