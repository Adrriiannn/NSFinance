import { LinearGradient } from "expo-linear-gradient";
import { ReactNode, useEffect, useMemo, useState } from "react";
import {
  Keyboard,
  KeyboardAvoidingView,
  Platform,
  ScrollView,
  StyleSheet,
  Text,
  TextInputProps,
  View
} from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import { PasswordField } from "../ui/PasswordField";
import { TextField } from "../ui/TextField";
import { layout, palette, radius, spacing, typography } from "../../theme/tokens";

export type AuthKeyboardMirrorField = {
  key: string;
  label: string;
  value: string;
  onChangeText: (value: string) => void;
  secureTextEntry?: boolean;
  passwordVisible?: boolean;
  onPasswordVisibilityChange?: (isVisible: boolean) => void;
  placeholder?: string;
  keyboardType?: TextInputProps["keyboardType"];
  autoCapitalize?: TextInputProps["autoCapitalize"];
};

export type AuthKeyboardMirrorRequirement = {
  key: string;
  label: string;
  isMet: boolean;
};

export type AuthKeyboardMirrorRequirements = {
  items: AuthKeyboardMirrorRequirement[];
  showSuccessWhenAllMet?: boolean;
  successText?: string;
};

type AuthScreenProps = {
  children: ReactNode;
  keyboardMirrorField?: AuthKeyboardMirrorField | null;
  keyboardMirrorRequirements?: AuthKeyboardMirrorRequirements | null;
};

export function AuthScreen({
  children,
  keyboardMirrorField = null,
  keyboardMirrorRequirements = null
}: AuthScreenProps) {
  const [keyboardHeight, setKeyboardHeight] = useState(0);
  const showEvent = Platform.OS === "ios" ? "keyboardWillShow" : "keyboardDidShow";
  const hideEvent = Platform.OS === "ios" ? "keyboardWillHide" : "keyboardDidHide";

  useEffect(() => {
    const showSubscription = Keyboard.addListener(showEvent, (event) => {
      setKeyboardHeight(event.endCoordinates.height);
    });

    const hideSubscription = Keyboard.addListener(hideEvent, () => {
      setKeyboardHeight(0);
    });

    return () => {
      showSubscription.remove();
      hideSubscription.remove();
    };
  }, [hideEvent, showEvent]);

  const shouldShowMirror = useMemo(
    () => keyboardHeight > 0 && keyboardMirrorField !== null,
    [keyboardHeight, keyboardMirrorField]
  );
  const shouldShowMirrorRequirements = Boolean(
    shouldShowMirror &&
      keyboardMirrorField?.secureTextEntry &&
      keyboardMirrorRequirements &&
      keyboardMirrorRequirements.items.length > 0
  );
  const allMirrorRequirementsMet = useMemo(
    () =>
      Boolean(
        keyboardMirrorRequirements &&
          keyboardMirrorRequirements.items.length > 0 &&
          keyboardMirrorRequirements.items.every((item) => item.isMet)
      ),
    [keyboardMirrorRequirements]
  );
  const nextMirrorRequirement = useMemo(() => {
    if (!shouldShowMirrorRequirements || !keyboardMirrorRequirements) {
      return null;
    }

    return keyboardMirrorRequirements.items.find((item) => !item.isMet) ?? null;
  }, [keyboardMirrorRequirements, shouldShowMirrorRequirements]);

  return (
    <SafeAreaView style={styles.safeArea} edges={["top", "left", "right", "bottom"]}>
      <LinearGradient colors={["#061020", "#050D19", "#040B16"]} style={StyleSheet.absoluteFill} />
      <View style={styles.glowTop} />
      <View style={styles.glowBottom} />
      <KeyboardAvoidingView
        style={styles.keyboardWrap}
        behavior={Platform.OS === "ios" ? "padding" : undefined}
      >
        <ScrollView
          contentContainerStyle={styles.content}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
        >
          {children}
        </ScrollView>

        {shouldShowMirror && keyboardMirrorField ? (
          <View style={[styles.keyboardMirrorWrap, { bottom: keyboardHeight + spacing[8] }]}>
            {shouldShowMirrorRequirements && keyboardMirrorRequirements ? (
              <View style={styles.mirrorRulesWrap}>
                <View
                  style={[
                    styles.mirrorRuleSingleChip,
                    allMirrorRequirementsMet ? styles.mirrorRuleSingleChipMet : styles.mirrorRuleSingleChipMissing
                  ]}
                >
                  <Text
                    numberOfLines={1}
                    adjustsFontSizeToFit
                    minimumFontScale={0.82}
                    style={[
                      styles.mirrorRuleSingleText,
                      allMirrorRequirementsMet ? styles.mirrorRuleSingleTextMet : styles.mirrorRuleSingleTextMissing
                    ]}
                  >
                    {allMirrorRequirementsMet
                      ? (keyboardMirrorRequirements.successText ?? "Your password meets our requirements.")
                      : (nextMirrorRequirement?.label ?? "")}
                  </Text>
                </View>
              </View>
            ) : null}

            {keyboardMirrorField.secureTextEntry ? (
              <PasswordField
                key={keyboardMirrorField.key}
                label={keyboardMirrorField.label}
                value={keyboardMirrorField.value}
                onChangeText={keyboardMirrorField.onChangeText}
                placeholder={keyboardMirrorField.placeholder}
                showLabel={false}
                forceFocused
                surfaceMode="solid"
                isPasswordVisible={keyboardMirrorField.passwordVisible}
                onPasswordVisibilityChange={keyboardMirrorField.onPasswordVisibilityChange}
                autoHideOnBlur={false}
                autoFocus
              />
            ) : (
              <TextField
                key={keyboardMirrorField.key}
                label={keyboardMirrorField.label}
                value={keyboardMirrorField.value}
                onChangeText={keyboardMirrorField.onChangeText}
                placeholder={keyboardMirrorField.placeholder}
                keyboardType={keyboardMirrorField.keyboardType}
                autoCapitalize={keyboardMirrorField.autoCapitalize}
                showLabel={false}
                forceFocused
                surfaceMode="solid"
                autoFocus
              />
            )}
          </View>
        ) : null}
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  safeArea: {
    flex: 1,
    backgroundColor: palette.appBackground
  },
  keyboardWrap: {
    flex: 1
  },
  content: {
    flexGrow: 1,
    paddingHorizontal: layout.screenHorizontalPadding,
    paddingVertical: spacing[24]
  },
  keyboardMirrorWrap: {
    position: "absolute",
    left: layout.screenHorizontalPadding,
    right: layout.screenHorizontalPadding,
    zIndex: 50
  },
  mirrorRulesWrap: {
    marginBottom: spacing[16],
    alignItems: "center"
  },
  mirrorRuleSingleChip: {
    width: "88%",
    minHeight: 30,
    borderRadius: radius.small,
    borderWidth: 1,
    paddingHorizontal: spacing[12],
    justifyContent: "center",
    alignItems: "center",
    overflow: "hidden"
  },
  mirrorRuleSingleChipMissing: {
    backgroundColor: palette.elevatedBackground,
    borderColor: "rgba(244,104,119,0.52)",
    shadowColor: palette.negative,
    shadowOpacity: 0.55,
    shadowRadius: 14,
    shadowOffset: { width: 0, height: 0 },
    elevation: 8
  },
  mirrorRuleSingleChipMet: {
    backgroundColor: palette.elevatedBackground,
    borderColor: "rgba(28,197,131,0.56)",
    shadowColor: palette.success,
    shadowOpacity: 0.55,
    shadowRadius: 14,
    shadowOffset: { width: 0, height: 0 },
    elevation: 8
  },
  mirrorRuleSingleText: {
    width: "100%",
    textAlign: "center"
  },
  mirrorRuleSingleTextMissing: {
    color: palette.negative,
    textShadowColor: "rgba(244,104,119,0.62)",
    textShadowOffset: { width: 0, height: 0 },
    textShadowRadius: 10,
    ...typography.caption
  },
  mirrorRuleSingleTextMet: {
    color: palette.success,
    textShadowColor: "rgba(28,197,131,0.62)",
    textShadowOffset: { width: 0, height: 0 },
    textShadowRadius: 10,
    ...typography.caption
  },
  glowTop: {
    position: "absolute",
    top: -90,
    right: -30,
    width: 220,
    height: 220,
    borderRadius: 110,
    backgroundColor: "rgba(47,107,255,0.1)"
  },
  glowBottom: {
    position: "absolute",
    bottom: -120,
    left: -80,
    width: 260,
    height: 260,
    borderRadius: 130,
    backgroundColor: "rgba(111,215,255,0.05)"
  }
});
