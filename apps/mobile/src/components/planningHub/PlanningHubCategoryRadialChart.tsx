import { StyleSheet, Text, View } from "react-native";
import Svg, { Circle, G } from "react-native-svg";
import { getExpenseTrackerVisual } from "../../features/expenseTracker/expenseTrackerModels";
import { palette, typography } from "../../theme/tokens";

type ChartSlice = {
  domainId: number | null;
  categoryId: number | null;
  category: string;
  total: number;
  percentage: number;
};

type PlanningHubCategoryRadialChartProps = {
  data: ChartSlice[];
  totalLabel: string;
  centerLabel?: string;
};

const CHART_SIZE = 152;
const STROKE_WIDTH = 24;
const RADIUS = (CHART_SIZE - STROKE_WIDTH) / 2;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;
const CENTER = CHART_SIZE / 2;

export function PlanningHubCategoryRadialChart({ data, totalLabel, centerLabel = "Spent" }: PlanningHubCategoryRadialChartProps) {
  const slices = buildSlices(data);
  let accumulated = 0;

  return (
    <View style={styles.wrap}>
      <View style={styles.chartCircle}>
        <Svg width={CHART_SIZE} height={CHART_SIZE}>
          <G rotation={-90} originX={CENTER} originY={CENTER}>
            <Circle
              cx={CENTER}
              cy={CENTER}
              r={RADIUS}
              stroke="rgba(226,236,255,0.08)"
              strokeWidth={STROKE_WIDTH}
              fill="none"
            />
            {slices.map((slice) => {
              const dashLength = (slice.percentage / 100) * CIRCUMFERENCE;
              const dashGap = CIRCUMFERENCE - dashLength;
              const dashOffset = -accumulated;
              accumulated += dashLength;

              return (
                <Circle
                  key={`${slice.categoryId ?? slice.category}`}
                  cx={CENTER}
                  cy={CENTER}
                  r={RADIUS}
                  stroke={getExpenseTrackerVisual({ domainId: slice.domainId, categoryId: slice.categoryId }).color}
                  strokeWidth={STROKE_WIDTH}
                  strokeLinecap="butt"
                  strokeDasharray={`${dashLength} ${dashGap}`}
                  strokeDashoffset={dashOffset}
                  fill="none"
                />
              );
            })}
          </G>
        </Svg>

        <View style={styles.centerBubble}>
          <Text style={styles.centerLabel}>{centerLabel}</Text>
          <Text style={styles.centerValue}>{totalLabel}</Text>
        </View>
      </View>
    </View>
  );
}

function buildSlices(data: ChartSlice[]) {
  const nonZero = data.filter((item) => item.percentage > 0);
  if (!nonZero.length) {
    return [{ domainId: null, categoryId: null, category: "Other", total: 0, percentage: 100 }];
  }

  const normalizedTotal = nonZero.reduce((sum, item) => sum + item.percentage, 0);
  if (normalizedTotal === 100) {
    return nonZero;
  }

  return nonZero.map((item, index) => {
    if (index === nonZero.length - 1) {
      const used = nonZero.slice(0, -1).reduce((sum, current) => sum + current.percentage, 0);
      return {
        ...item,
        percentage: Number((100 - used).toFixed(2))
      };
    }

    return item;
  });
}

const styles = StyleSheet.create({
  wrap: {
    alignItems: "center",
    justifyContent: "center"
  },
  chartCircle: {
    width: CHART_SIZE,
    height: CHART_SIZE,
    alignItems: "center",
    justifyContent: "center",
    position: "relative"
  },
  centerBubble: {
    position: "absolute",
    width: 88,
    height: 88,
    borderRadius: 44,
    borderWidth: 1,
    borderColor: palette.border,
    backgroundColor: "rgba(12,25,43,0.98)",
    alignItems: "center",
    justifyContent: "center",
    paddingHorizontal: 8
  },
  centerLabel: {
    color: palette.textSecondary,
    ...typography.caption
  },
  centerValue: {
    marginTop: 4,
    color: palette.textPrimary,
    ...typography.bodyStrong,
    fontWeight: "700",
    textAlign: "center"
  }
});
