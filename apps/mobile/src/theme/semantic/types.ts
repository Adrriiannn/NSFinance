export const semanticButtonVariants = [
  "primary",
  "secondary",
  "ghost",
  "destructive",
  "icon",
  "compact",
  "pillAction"
] as const;

export const semanticButtonStates = ["idle", "active", "disabled", "loading"] as const;

export type SemanticButtonVariant = (typeof semanticButtonVariants)[number];
export type SemanticButtonState = (typeof semanticButtonStates)[number];

export type SemanticButtonStateColors = Readonly<{
  background: string;
  border: string;
  foreground: string;
}>;

export type SemanticButtonStates = Readonly<{
  [State in SemanticButtonState]: SemanticButtonStateColors;
}>;

export type SemanticButtonRoles = Readonly<{
  [Variant in SemanticButtonVariant]: SemanticButtonStates;
}>;

export type SemanticTheme = Readonly<{
  name: string;
  isDark: boolean;
  colors: Readonly<{
    canvas: string;
    elevatedCanvas: string;
    surface: Readonly<{
      level0: string;
      level1: string;
      level2: string;
      field: string;
      fieldStrong: string;
      tabBar: string;
      floating: string;
      muted: string;
    }>;
    text: Readonly<{
      primary: string;
      secondary: string;
      muted: string;
      inverse: string;
    }>;
    border: Readonly<{
      subtle: string;
      strong: string;
      focus: string;
      divider: string;
    }>;
    action: Readonly<{
      primary: string;
      primaryStrong: string;
      primaryGlow: string;
      secondary: string;
      secondaryStrong: string;
      ghost: string;
      destructive: string;
      button: SemanticButtonRoles;
    }>;
    onAction: Readonly<{
      primary: string;
      secondary: string;
      ghost: string;
      destructive: string;
      disabled: string;
    }>;
    status: Readonly<{
      success: string;
      successSurface: string;
      warning: string;
      warningSurface: string;
      danger: string;
      dangerSurface: string;
      info: string;
      infoSurface: string;
    }>;
    accent: Readonly<{
      primary: string;
      primaryStrong: string;
      cyan: string;
      amber: string;
    }>;
    overlay: Readonly<{
      strong: string;
      soft: string;
    }>;
    money: Readonly<{
      positive: string;
      negative: string;
    }>;
  }>;
}>;
