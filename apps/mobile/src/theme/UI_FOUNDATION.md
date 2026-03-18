# UI Foundation Guide

## Change Sizes

- Control heights and shared dimensions live in `apps/mobile/src/theme/tokens/sizing.ts`.
- Shared spacing lives in `apps/mobile/src/theme/tokens/spacing.ts`.
- Shared radii live in `apps/mobile/src/theme/tokens/radius.ts`.

## Change Colors

- Raw app colors live in `apps/mobile/src/theme/tokens/colors.ts`.
- Semantic light and dark mappings live in `apps/mobile/src/theme/semantic/light.ts` and `apps/mobile/src/theme/semantic/dark.ts`.
- Existing legacy imports still flow through `apps/mobile/src/theme/tokens.ts`.

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
