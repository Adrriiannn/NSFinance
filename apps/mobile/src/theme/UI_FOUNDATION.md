# UI Foundation Guide

## Change Sizes

- Control heights and shared dimensions live in `apps/mobile/src/theme/tokens/sizing.ts`.
- Shared spacing lives in `apps/mobile/src/theme/tokens/spacing.ts`.
- Shared radii live in `apps/mobile/src/theme/tokens/radius.ts`.

## Change Colors

- Raw app colors live in `apps/mobile/src/theme/tokens/colors.ts`.
- Semantic light and dark mappings live in `apps/mobile/src/theme/semantic/light.ts` and `apps/mobile/src/theme/semantic/dark.ts`.
- The structural theme contract and semantic button role/state names live in `apps/mobile/src/theme/semantic/types.ts`.
- Existing token imports still flow through `apps/mobile/src/theme/tokens.ts`.

Theme implementations own palette values. Components consume semantic roles only, so future light, dark, seasonal, and event themes can change presentation without changing component behavior.

## Change Shadows

- Shared elevation and floating shadows live in `apps/mobile/src/theme/tokens/shadows.ts`.

## Change Presets

- Text presets: `apps/mobile/src/components/ui/text/text.presets.ts`
- Button presets: `apps/mobile/src/components/ui/buttons/button.presets.ts`
- Card presets: `apps/mobile/src/components/ui/cards/card.presets.ts`
- Chip presets: `apps/mobile/src/components/ui/chips/chip.presets.ts`
- Field presets: `apps/mobile/src/components/ui/fields/field.presets.ts`
- Form spacing presets: `apps/mobile/src/components/ui/forms/form.presets.ts`
- Row presets: `apps/mobile/src/components/ui/rows/row.presets.ts`
- Feedback presets: `apps/mobile/src/components/ui/feedback/feedback.presets.ts`
- Surface presets: `apps/mobile/src/components/ui/surfaces/surface.presets.ts`

## Button System

- `apps/mobile/src/components/ui/buttons/Button.tsx` is the canonical Button implementation.
- `apps/mobile/src/components/ui/buttons/IconButton.tsx` is the canonical icon-only adapter and requires an `accessibilityLabel`.
- Root-level button files remain compatibility adapters; they must delegate to the canonical nested implementation.
- Button colors come from the semantic role and state matrix in each theme. Presets must not contain palette literals.
- State precedence is `loading`, `disabled`, `active`, then `idle`. Focus and press both resolve to `active`; loading disables activation and remains exposed as busy and disabled to assistive technology.
- Keyboard focus adds a theme-owned 2dp border with at least 3:1 contrast against every supported surface; press retains the active-state scale response.
- Every canonical button has an effective minimum touch target of 48dp.
- Labels allow font scaling, wrap to at most two lines, and are never reduced with `adjustsFontSizeToFit`.
- Primary labels and loading indicators use the theme's `onAction.primary` role and must retain at least 4.5:1 contrast in idle, active, and loading states.

## Header System

- Global header sizes, slot widths, row spacing, sticky divider behavior, title sizing, subtitle sizing, and row-2 control sizing live in `apps/mobile/src/layout/header/header.constants.ts`.
- Header preset definitions and page-level header variant mapping live in `apps/mobile/src/layout/header/header.presets.ts`.
- Shared header building blocks live in `apps/mobile/src/layout/header/`.
- Menu and back button sizing are controlled through `HeaderActionButton.tsx` and `header.constants.ts`.
- Shared selector/search controls used in headers live in `HeaderDropdownSlot.tsx` and `HeaderSearchSlot.tsx`.

### Primary Page Mapping

- `Home` -> `primaryGreeting`
- `Accounts` -> `primaryTwoRowSelector`
- `Activity` -> `primaryTwoRowSelector`
- `Cashflow` -> `primaryDefault`
- `Calendar` -> `primaryDefault`
- `NS Companion` -> `primaryDefault`
- `Plans` -> `primaryDefault`
- `Analytics` -> `primaryTwoRowSelector`
- `Categories` -> `primaryTwoRowSearch`

### Secondary Pages

- All menu pages, legal/policy pages, transaction detail pages, account detail pages, and other routed non-primary pages should use `secondaryDetail`.
