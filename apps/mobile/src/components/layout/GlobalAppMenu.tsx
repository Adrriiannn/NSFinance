import { Ionicons } from "@expo/vector-icons";
import { usePathname, useRouter } from "expo-router";
import { useEffect, useMemo, useState } from "react";
import { Linking, Modal, Pressable, StyleSheet, Text, View } from "react-native";
import { useAuthSession } from "../../providers/AuthProvider";
import { externalLinks } from "../../lib/config/externalLinks";
import { layout, palette, spacing, typography } from "../../theme/tokens";

type GlobalAppMenuProps = {
  topOffset?: number;
};

const menuItems = [
  { label: "Profile", path: "/(tabs)/accounts/profile" },
  { label: "Security", path: "/(tabs)/accounts/security" },
  { label: "Sessions", path: "/(tabs)/accounts/sessions" },
  { label: "Legal", path: "/(tabs)/accounts/legal" },
  { label: "Privacy", path: "/(tabs)/accounts/privacy" },
  { label: "Support", path: "/(tabs)/accounts/support" }
] as const;

export function GlobalAppMenu({ topOffset = 8 }: GlobalAppMenuProps) {
  const router = useRouter();
  const pathname = usePathname();
  const { isAuthenticated, logout } = useAuthSession();
  const [isOpen, setIsOpen] = useState(false);

  useEffect(() => {
    setIsOpen(false);
  }, [pathname]);

  const activePath = useMemo(() => pathname || "", [pathname]);

  if (!isAuthenticated) {
    return null;
  }

  return (
    <>
      <View style={[styles.triggerWrap, { top: topOffset }]}>
        <Pressable
          accessibilityRole="button"
          accessibilityLabel="Open app menu"
          onPress={() => setIsOpen(true)}
          style={({ pressed }) => [styles.trigger, pressed ? styles.triggerPressed : null]}
        >
          <Ionicons name="menu-outline" size={20} color={palette.textPrimary} />
        </Pressable>
      </View>

      <Modal
        visible={isOpen}
        transparent
        animationType="fade"
        onRequestClose={() => setIsOpen(false)}
      >
        <Pressable style={styles.overlay} onPress={() => setIsOpen(false)}>
          <Pressable style={styles.menuCard} onPress={() => undefined}>
            <Text style={styles.menuTitle}>Menu</Text>
            <View style={styles.menuItems}>
              {menuItems.map((item) => {
                const isActive = activePath.includes(item.path.replace("/(tabs)", ""));
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
                <Text style={styles.logoutText}>Log Out</Text>
              </Pressable>
              <View style={styles.linkIconRow}>
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel="Instagram"
                  onPress={() => {
                    void Linking.openURL(externalLinks.instagram);
                  }}
                  style={({ pressed }) => [styles.linkIconButton, pressed ? styles.menuItemPressed : null]}
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
          </Pressable>
        </Pressable>
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
  overlay: {
    flex: 1,
    backgroundColor: "rgba(3,9,18,0.62)",
    alignItems: "flex-end",
    paddingTop: 72,
    paddingRight: layout.screenHorizontalPadding,
    paddingBottom: spacing[24],
    paddingLeft: spacing[24]
  },
  menuCard: {
    width: "82%",
    maxWidth: 310,
    borderRadius: 18,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(12,25,43,0.98)",
    padding: spacing[16],
    gap: spacing[12]
  },
  menuTitle: {
    color: palette.textPrimary,
    ...typography.title2
  },
  menuItems: {
    gap: spacing[8]
  },
  menuItem: {
    minHeight: 42,
    borderRadius: 12,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.76)",
    justifyContent: "center",
    paddingHorizontal: spacing[12]
  },
  menuItemActive: {
    borderColor: palette.primaryGlow,
    backgroundColor: "rgba(47,107,255,0.2)"
  },
  menuItemPressed: {
    opacity: 0.86
  },
  menuItemText: {
    color: palette.textPrimary,
    ...typography.body1
  },
  menuItemTextActive: {
    fontWeight: "700"
  },
  footer: {
    marginTop: spacing[8],
    paddingTop: spacing[12],
    borderTopWidth: 1,
    borderTopColor: "rgba(220,232,255,0.12)",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
    gap: spacing[12]
  },
  logoutButton: {
    minHeight: 38,
    borderRadius: 10,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(18,36,58,0.8)",
    alignItems: "center",
    justifyContent: "center",
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
  }
});
