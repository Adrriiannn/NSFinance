import { StyleSheet } from "react-native";
import type { ImageStyle, RegisteredStyle, TextStyle, ViewStyle } from "react-native";
import { getRuntimeThemeSnapshot } from "./themeSnapshot";

type NamedStyles<T> = { [P in keyof T]: ViewStyle | TextStyle | ImageStyle };
type RuntimeStyles<T extends NamedStyles<T>> = { [P in keyof T]: RegisteredStyle<T[P]> };

export function createRuntimeStyleSheet<T extends NamedStyles<T>>(factory: () => T): RuntimeStyles<T> {
  let cachedThemeName: string | null = null;
  let cachedStyles: RuntimeStyles<T> | null = null;

  const ensureStyles = () => {
    const themeName = getRuntimeThemeSnapshot().name;

    if (!cachedStyles || cachedThemeName !== themeName) {
      cachedThemeName = themeName;
      cachedStyles = StyleSheet.create(factory()) as unknown as RuntimeStyles<T>;
    }

    return cachedStyles;
  };

  return new Proxy({} as RuntimeStyles<T>, {
    get(_target, property) {
      return ensureStyles()[property as keyof T];
    },
    ownKeys() {
      return Reflect.ownKeys(ensureStyles() as object);
    },
    getOwnPropertyDescriptor() {
      return {
        enumerable: true,
        configurable: true
      };
    }
  });
}
