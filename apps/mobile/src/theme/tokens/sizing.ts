export const sizing = {
  touchTarget: {
    minimum: 48
  },
  button: {
    heights: {
      compact: 36,
      standard: 44,
      large: 52,
      pillAction: 44,
      icon: 44
    },
    horizontalPadding: {
      compact: 14,
      standard: 16,
      large: 16
    }
  },
  chip: {
    heights: {
      compact: 28,
      standard: 32,
      large: 36
    },
    horizontalPadding: {
      compact: 10,
      standard: 12,
      large: 14
    }
  },
  field: {
    heights: {
      dense: 44,
      standard: 48,
      search: 48,
      select: 48,
      currency: 48
    },
    multilineMinHeight: 96
  },
  row: {
    heights: {
      compact: 48,
      standard: 56,
      large: 64
    }
  },
  card: {
    minHeights: {
      compact: 88,
      standard: 116,
      hero: 148,
      insight: 124
    },
    padding: {
      compact: 12,
      standard: 16,
      hero: 20
    }
  },
  iconButton: {
    compact: 36,
    standard: 44,
    large: 48
  },
  fab: {
    size: 56,
    extendedHeight: 56
  },
  tabBar: {
    height: 72
  },
  modalSheet: {
    handleWidth: 42,
    handleHeight: 4
  }
} as const;
