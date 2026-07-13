import { ReactNode, useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  findNodeHandle, Keyboard, KeyboardAvoidingView, NativeSyntheticEvent, Platform, ScrollView, TextInput, UIManager, useWindowDimensions, type NativeScrollEvent } from "react-native";
import { SafeAreaView } from "react-native-safe-area-context";
import { layout, palette, spacing, createRuntimeStyleSheet } from "../../theme/tokens";
import { AppBackgroundLayer } from "../ui/surfaces/AppBackgroundLayer";

type AuthScreenProps = {
  children: ReactNode;
  focusedInputExtraClearance?: number;
  resetScrollOnKeyboardHide?: boolean;
};

export function AuthScreen({
  children,
  focusedInputExtraClearance = 0,
  resetScrollOnKeyboardHide = false
}: AuthScreenProps) {
  const normalizedFocusedExtraClearance = Math.max(focusedInputExtraClearance, 0);
  const { height: windowHeight } = useWindowDimensions();
  const [keyboardHeight, setKeyboardHeight] = useState(0);
  const [extraBottomSpacer, setExtraBottomSpacer] = useState(0);
  const scrollViewRef = useRef<ScrollView | null>(null);
  const keyboardTopRef = useRef<number | null>(null);
  const extraBottomSpacerRef = useRef(0);
  const clearSpacerTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const clearanceAdjustTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const scrollOffsetYRef = useRef(0);
  const keyboardSessionBaseOffsetRef = useRef<number>(0);
  const keyboardSessionActiveRef = useRef(false);
  const previousFocusedExtraClearanceRef = useRef(normalizedFocusedExtraClearance);
  const showEvent = Platform.OS === "ios" ? "keyboardWillShow" : "keyboardDidShow";
  const hideEvent = Platform.OS === "ios" ? "keyboardWillHide" : "keyboardDidHide";
  const baseBottomPadding = spacing[24];
  const fieldKeyboardClearance = spacing[32] + spacing[8];

  useEffect(() => {
    extraBottomSpacerRef.current = extraBottomSpacer;
  }, [extraBottomSpacer]);

  useEffect(() => {
    return () => {
      if (clearSpacerTimeoutRef.current) {
        clearTimeout(clearSpacerTimeoutRef.current);
        clearSpacerTimeoutRef.current = null;
      }
      if (clearanceAdjustTimeoutRef.current) {
        clearTimeout(clearanceAdjustTimeoutRef.current);
        clearanceAdjustTimeoutRef.current = null;
      }
    };
  }, []);

  const scrollFocusedInputIntoView = useCallback((
    animated = true,
    options?: {
      keyboardHeightOverride?: number;
      keyboardTopOverride?: number;
    }
  ) => {
    const effectiveKeyboardHeight = options?.keyboardHeightOverride ?? keyboardHeight;
    if (effectiveKeyboardHeight <= 0) {
      return;
    }

    const scrollView = scrollViewRef.current;
    if (!scrollView) {
      return;
    }

    const focusedInput = TextInput.State.currentlyFocusedInput?.();
    if (!focusedInput) {
      return;
    }

    const nodeHandle = findNodeHandle(focusedInput as unknown as Parameters<typeof findNodeHandle>[0]);
    if (!nodeHandle) {
      return;
    }

    UIManager.measureInWindow(nodeHandle, (_x, y, _width, height) => {
      const keyboardTop =
        options?.keyboardTopOverride ??
        keyboardTopRef.current ??
        (windowHeight - effectiveKeyboardHeight);
      const desiredFieldBottom =
        keyboardTop - fieldKeyboardClearance - normalizedFocusedExtraClearance;
      const overlap = Math.ceil(y + height - desiredFieldBottom);

      if (overlap <= 0) {
        return;
      }

      const requiredSpacer =
        effectiveKeyboardHeight + spacing[24] + normalizedFocusedExtraClearance;
      if (requiredSpacer > extraBottomSpacerRef.current) {
        setExtraBottomSpacer(requiredSpacer);
        requestAnimationFrame(() => {
          requestAnimationFrame(() => {
            scrollFocusedInputIntoView(animated, options);
          });
        });
        return;
      }

      const currentOffsetY = scrollOffsetYRef.current;
      const targetOffsetY = currentOffsetY + overlap;
      scrollView.scrollTo({ y: targetOffsetY, animated });
    });
  }, [fieldKeyboardClearance, keyboardHeight, normalizedFocusedExtraClearance, windowHeight]);

  useEffect(() => {
    const showSubscription = Keyboard.addListener(showEvent, (event) => {
      if (!keyboardSessionActiveRef.current) {
        keyboardSessionBaseOffsetRef.current = resetScrollOnKeyboardHide
          ? 0
          : scrollOffsetYRef.current;
        keyboardSessionActiveRef.current = true;
      }

      const nextKeyboardHeight = event.endCoordinates.height;
      const nextKeyboardTop = event.endCoordinates.screenY;
      setKeyboardHeight(nextKeyboardHeight);
      keyboardTopRef.current = nextKeyboardTop;
      requestAnimationFrame(() => {
        scrollFocusedInputIntoView(true, {
          keyboardHeightOverride: nextKeyboardHeight,
          keyboardTopOverride: nextKeyboardTop
        });
      });
      setTimeout(() => {
        scrollFocusedInputIntoView(true, {
          keyboardHeightOverride: nextKeyboardHeight,
          keyboardTopOverride: nextKeyboardTop
        });
      }, 70);
    });

    const hideSubscription = Keyboard.addListener(hideEvent, () => {
      const restoreOffsetY = Math.max(keyboardSessionBaseOffsetRef.current, 0);
      scrollViewRef.current?.scrollTo({ y: restoreOffsetY, animated: true });
      setKeyboardHeight(0);
      keyboardTopRef.current = null;
      keyboardSessionActiveRef.current = false;
      keyboardSessionBaseOffsetRef.current = restoreOffsetY;
      previousFocusedExtraClearanceRef.current = 0;
      if (clearanceAdjustTimeoutRef.current) {
        clearTimeout(clearanceAdjustTimeoutRef.current);
        clearanceAdjustTimeoutRef.current = null;
      }

      if (clearSpacerTimeoutRef.current) {
        clearTimeout(clearSpacerTimeoutRef.current);
      }

      clearSpacerTimeoutRef.current = setTimeout(() => {
        setExtraBottomSpacer(0);
        if (resetScrollOnKeyboardHide) {
          requestAnimationFrame(() => {
            scrollViewRef.current?.scrollTo({ y: 0, animated: false });
          });
        }
        clearSpacerTimeoutRef.current = null;
      }, 180);
    });

    return () => {
      showSubscription.remove();
      hideSubscription.remove();
    };
  }, [hideEvent, resetScrollOnKeyboardHide, scrollFocusedInputIntoView, showEvent]);

  useEffect(() => {
    if (keyboardHeight <= 0) {
      return;
    }

    requestAnimationFrame(() => {
      scrollFocusedInputIntoView();
    });
  }, [keyboardHeight, scrollFocusedInputIntoView]);

  useEffect(() => {
    if (keyboardHeight <= 0) {
      previousFocusedExtraClearanceRef.current = normalizedFocusedExtraClearance;
      return;
    }

    const previousClearance = previousFocusedExtraClearanceRef.current;
    const nextClearance = normalizedFocusedExtraClearance;

    if (nextClearance > previousClearance) {
      requestAnimationFrame(() => {
        scrollFocusedInputIntoView();
      });
    } else if (nextClearance < previousClearance) {
      const clearanceDelta = previousClearance - nextClearance;
      const targetOffsetY = Math.max(scrollOffsetYRef.current - clearanceDelta, 0);
      scrollViewRef.current?.scrollTo({ y: targetOffsetY, animated: true });

      if (clearanceAdjustTimeoutRef.current) {
        clearTimeout(clearanceAdjustTimeoutRef.current);
      }
      clearanceAdjustTimeoutRef.current = setTimeout(() => {
        scrollFocusedInputIntoView(true);
        clearanceAdjustTimeoutRef.current = null;
      }, 140);
    }

    previousFocusedExtraClearanceRef.current = nextClearance;
  }, [keyboardHeight, normalizedFocusedExtraClearance, scrollFocusedInputIntoView]);

  useEffect(() => {
    if (keyboardHeight <= 0) {
      return;
    }

    let previousFocusedInput = TextInput.State.currentlyFocusedInput?.() ?? null;
    const focusPoll = setInterval(() => {
      const focusedInput = TextInput.State.currentlyFocusedInput?.() ?? null;
      if (!focusedInput || focusedInput === previousFocusedInput) {
        return;
      }

      previousFocusedInput = focusedInput;
      scrollFocusedInputIntoView();
    }, 90);

    return () => {
      clearInterval(focusPoll);
    };
  }, [keyboardHeight, scrollFocusedInputIntoView]);

  const contentBottomPadding = useMemo(
    () => baseBottomPadding + extraBottomSpacer,
    [baseBottomPadding, extraBottomSpacer]
  );

  return (
    <SafeAreaView style={styles.safeArea} edges={["top", "left", "right", "bottom"]}>
      <AppBackgroundLayer />
      <KeyboardAvoidingView style={styles.keyboardWrap}>
        <ScrollView
          ref={scrollViewRef}
          onScroll={(event: NativeSyntheticEvent<NativeScrollEvent>) => {
            scrollOffsetYRef.current = event.nativeEvent.contentOffset.y;
          }}
          scrollEventThrottle={16}
          contentContainerStyle={[
            styles.content,
            { paddingBottom: contentBottomPadding }
          ]}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
        >
          {children}
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const styles = createRuntimeStyleSheet(() => ({
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
    paddingTop: spacing[24],
    paddingBottom: spacing[24]
  }
}));

