import { Ionicons } from "@expo/vector-icons";
import { usePathname, useRouter } from "expo-router";
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
import { layout, palette, spacing, typography } from "../../theme/tokens";

type GlobalAppMenuProps = {
  topOffset?: number;
};

const menuItems = [
  { label: "Profile", path: "/(tabs)/accounts/profile", icon: "person-outline" },
  { label: "Security", path: "/(tabs)/accounts/security", icon: "shield-checkmark-outline" },
  { label: "Privacy", path: "/(tabs)/accounts/privacy", icon: "lock-closed-outline" },
  { label: "Legal", path: "/(tabs)/accounts/legal", icon: "document-text-outline" },
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

export function GlobalAppMenu({ topOffset = 8 }: GlobalAppMenuProps) {
  const router = useRouter();
  const pathname = usePathname();
  const { isAuthenticated, session, logout } = useAuthSession();
  const profileQuery = useUserProfileQuery();
  const [isOpen, setIsOpen] = useState(false);
  const slideProgress = useRef(new Animated.Value(0)).current;

  useEffect(() => {
    setIsOpen(false);
  }, [pathname]);

  useEffect(() => {
    Animated.timing(slideProgress, {
      toValue: isOpen ? 1 : 0,
      duration: 220,
      easing: Easing.out(Easing.cubic),
      useNativeDriver: true
    }).start();
  }, [isOpen, slideProgress]);

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
        <Pressable
          accessibilityRole="button"
          accessibilityLabel="Open settings menu"
          onPress={() => setIsOpen(true)}
          style={({ pressed }) => [styles.trigger, pressed ? styles.triggerPressed : null]}
        >
          <Ionicons name="menu-outline" size={20} color={palette.textPrimary} />
        </Pressable>
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
                      color={isActive ? palette.primaryGlow : palette.textSecondary}
                    />
                    <Text style={[styles.menuItemText, isActive ? styles.menuItemTextActive : null]}>
                      {item.label}
                    </Text>
                  </Pressable>
                );
              })}
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
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(12,25,43,0.92)",
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
    backgroundColor: "rgba(3,9,18,0.66)"
  },
  drawer: {
    width: "84%",
    maxWidth: 340,
    minHeight: "100%",
    backgroundColor: "rgba(9,20,35,0.98)",
    borderLeftWidth: 1,
    borderLeftColor: palette.border,
    paddingTop: 76,
    paddingHorizontal: spacing[16],
    paddingBottom: spacing[24],
    gap: spacing[16]
  },
  profileHeader: {
    borderRadius: 16,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.78)",
    padding: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[12]
  },
  avatarWrap: {
    width: 54,
    height: 54,
    borderRadius: 27,
    borderWidth: 1,
    borderColor: "rgba(127,174,255,0.8)",
    backgroundColor: "rgba(47,107,255,0.22)",
    alignItems: "center",
    justifyContent: "center"
  },
  avatarText: {
    color: palette.textPrimary,
    ...typography.body1,
    fontWeight: "700"
  },
  avatarImage: {
    width: "100%",
    height: "100%",
    borderRadius: 27
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
    color: palette.primaryGlow,
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
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.74)",
    paddingHorizontal: spacing[12],
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  menuItemActive: {
    borderColor: palette.primaryGlow,
    backgroundColor: "rgba(47,107,255,0.2)"
  },
  menuItemText: {
    color: palette.textPrimary,
    ...typography.body1
  },
  menuItemTextActive: {
    fontWeight: "700"
  },
  menuItemPressed: {
    opacity: 0.86
  },
  footer: {
    marginTop: "auto",
    paddingTop: spacing[12],
    borderTopWidth: 1,
    borderTopColor: "rgba(220,232,255,0.12)",
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
    borderRadius: 10,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.8)",
    paddingHorizontal: spacing[12]
  },
  logoutText: {
    color: palette.textPrimary,
    ...typography.body2,
    fontWeight: "600"
  },
  linkIconRow: {
    flexDirection: "row",
    alignItems: "center",
    gap: spacing[8]
  },
  linkIconButton: {
    width: 36,
    height: 36,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.8)",
    alignItems: "center",
    justifyContent: "center"
  },
  linkIconDisabled: {
    opacity: 0.45
  }
});
