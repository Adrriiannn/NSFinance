import { useMemo, useState } from "react";
import { LayoutChangeEvent, Text, View } from "react-native";
import { palette, typography, createRuntimeStyleSheet } from "../../theme/tokens";

type GraphPoint = {
  x: number;
  y: number;
};

type SpendTrendGraphProps = {
  primarySeries: number[];
  secondarySeries: number[];
  xCheckpoints?: number[];
  yCheckpoints?: number[];
  currency?: string;
  monthDate?: Date;
  maxValue?: number;
  primaryColor?: string;
  secondaryColor?: string;
  height?: number;
};

const MIN_WIDTH = 220;
const LEFT_GUTTER = 56;
const RIGHT_GUTTER = 12;
const TOP_GUTTER = 12;
const BOTTOM_GUTTER = 30;
const X_LABEL_WIDTH = 48;

function toPoints(values: number[], width: number, height: number, max: number): GraphPoint[] {
  if (values.length === 0 || width <= 0 || height <= 0) {
    return [];
  }

  const drawWidth = Math.max(width - LEFT_GUTTER - RIGHT_GUTTER, 1);
  const drawHeight = Math.max(height - TOP_GUTTER - BOTTOM_GUTTER, 1);
  const normalizedValues = values.length === 1 ? [values[0], values[0]] : values;

  return normalizedValues.map((value, index) => {
    const ratio = normalizedValues.length <= 1 ? 0 : index / (normalizedValues.length - 1);
    return {
      x: LEFT_GUTTER + ratio * drawWidth,
      y: TOP_GUTTER + (1 - value / max) * drawHeight
    };
  });
}

function smoothPolyline(points: GraphPoint[]) {
  if (points.length <= 2) {
    return points;
  }

  const smoothed: GraphPoint[] = [];
  for (let index = 0; index < points.length - 1; index += 1) {
    const current = points[index];
    const next = points[index + 1];
    smoothed.push(current);
    smoothed.push({
      x: (current.x + next.x) / 2,
      y: (current.y + next.y) / 2
    });
  }
  smoothed.push(points[points.length - 1]);
  return smoothed;
}

function buildSegments(points: GraphPoint[]) {
  const segments: { key: string; x: number; y: number; width: number; angle: string }[] = [];

  for (let index = 0; index < points.length - 1; index += 1) {
    const current = points[index];
    const next = points[index + 1];
    const dx = next.x - current.x;
    const dy = next.y - current.y;
    const length = Math.sqrt(dx * dx + dy * dy);
    const centerX = (current.x + next.x) / 2;
    const centerY = (current.y + next.y) / 2;

    segments.push({
      key: `${index}-${next.x.toFixed(2)}-${next.y.toFixed(2)}`,
      x: centerX - length / 2,
      y: centerY,
      width: Math.max(length, 1),
      angle: `${Math.atan2(dy, dx)}rad`
    });
  }

  return segments;
}

function LineSeries({ points, color }: { points: GraphPoint[]; color: string }) {
  const segments = useMemo(() => buildSegments(smoothPolyline(points)), [points]);

  return (
    <>
      {segments.map((segment) => (
        <View
          key={segment.key}
          style={[
            styles.segment,
            {
              backgroundColor: color,
              width: segment.width,
              left: segment.x,
              top: segment.y,
              transform: [{ rotateZ: segment.angle }]
            }
          ]}
        />
      ))}
    </>
  );
}

export function SpendTrendGraph({
  primarySeries,
  secondarySeries,
  xCheckpoints = [],
  yCheckpoints = [],
  currency = "GBP",
  monthDate = new Date(),
  maxValue,
  primaryColor = palette.success,
  secondaryColor = palette.negative,
  height = 164
}: SpendTrendGraphProps) {
  const [width, setWidth] = useState(MIN_WIDTH);

  const onLayout = (event: LayoutChangeEvent) => {
    const nextWidth = Math.max(event.nativeEvent.layout.width, MIN_WIDTH);
    if (Math.abs(nextWidth - width) > 1) {
      setWidth(nextWidth);
    }
  };

  const safeMax = useMemo(() => {
    if (maxValue && maxValue > 0) {
      return maxValue;
    }

    const found = Math.max(...primarySeries, ...secondarySeries, 1);
    return Number.isFinite(found) ? found : 1;
  }, [maxValue, primarySeries, secondarySeries]);

  const primaryPoints = useMemo(
    () => toPoints(primarySeries, width, height, safeMax),
    [height, primarySeries, safeMax, width]
  );
  const secondaryPoints = useMemo(
    () => toPoints(secondarySeries, width, height, safeMax),
    [height, secondarySeries, safeMax, width]
  );

  const drawHeight = Math.max(height - TOP_GUTTER - BOTTOM_GUTTER, 1);
  const drawWidth = Math.max(width - LEFT_GUTTER - RIGHT_GUTTER, 1);
  const elapsedDays = Math.max(primarySeries.length, secondarySeries.length, 1);

  const yFormatter = useMemo(
    () =>
      new Intl.NumberFormat("en-GB", {
        style: "currency",
        currency,
        maximumFractionDigits: 0
      }),
    [currency]
  );

  return (
    <View style={[styles.graph, { height }]} onLayout={onLayout}>
      {yCheckpoints.map((checkpoint) => {
        const ratio = checkpoint / safeMax;
        const y = TOP_GUTTER + (1 - ratio) * drawHeight;
        return (
          <View key={`y-${checkpoint}`} style={[styles.yGridRow, { top: y }]}>
            <Text style={styles.yLabel}>{yFormatter.format(Math.round(checkpoint))}</Text>
            <View style={styles.gridLine} />
          </View>
        );
      })}

      {xCheckpoints.map((day) => {
        const ratio = elapsedDays <= 1 ? 0 : (day - 1) / (elapsedDays - 1);
        const x = LEFT_GUTTER + ratio * drawWidth;
        return (
          <View
            key={`x-guide-${day}`}
            style={[
              styles.xGuideLine,
              {
                left: x,
                top: TOP_GUTTER,
                height: drawHeight
              }
            ]}
          />
        );
      })}

      <LineSeries points={secondaryPoints} color={secondaryColor} />
      <LineSeries points={primaryPoints} color={primaryColor} />

      <View style={styles.xAxisBase} />
      {Array.from({ length: elapsedDays }, (_, dayIndex) => dayIndex + 1).map((day) => {
        const ratio = elapsedDays <= 1 ? 0 : (day - 1) / (elapsedDays - 1);
        const x = LEFT_GUTTER + ratio * drawWidth;
        return (
          <View
            key={`x-tick-${day}`}
            style={[
              styles.xMinorTick,
              {
                left: x,
                bottom: BOTTOM_GUTTER,
                height: xCheckpoints.includes(day) ? 7 : 4
              }
            ]}
          />
        );
      })}
      {xCheckpoints.map((day) => {
        const ratio = elapsedDays <= 1 ? 0 : (day - 1) / (elapsedDays - 1);
        const x = LEFT_GUTTER + ratio * drawWidth;
        const monthLabel = String(monthDate.getMonth() + 1).padStart(2, "0");
        const dayLabel = String(day).padStart(2, "0");
        const lastCheckpointDay = xCheckpoints[xCheckpoints.length - 1] ?? day;
        const isLastCheckpoint = day === lastCheckpointDay;
        const minLabelLeft = LEFT_GUTTER - X_LABEL_WIDTH / 2;
        const labelLeft = isLastCheckpoint
          ? x - X_LABEL_WIDTH / 2
          : Math.max(x - X_LABEL_WIDTH / 2, minLabelLeft);
        return (
          <View key={`x-${day}`} style={[styles.xCheckpointWrap, { left: labelLeft }]}>
            <Text style={styles.xLabel}>{`${dayLabel}.${monthLabel}`}</Text>
          </View>
        );
      })}
    </View>
  );
}

const styles = createRuntimeStyleSheet(() => ({
  graph: {
    position: "relative",
    width: "100%",
    overflow: "visible"
  },
  segment: {
    position: "absolute",
    height: 2.4,
    borderRadius: 6
  },
  yGridRow: {
    position: "absolute",
    left: 0,
    right: 0,
    flexDirection: "row",
    alignItems: "center",
    gap: 6
  },
  yLabel: {
    width: LEFT_GUTTER - 8,
    textAlign: "right",
    color: palette.textSecondary,
    ...typography.caption
  },
  gridLine: {
    flex: 1,
    height: 1,
    backgroundColor: "rgba(242,140,40,0.1)"
  },
  xAxisBase: {
    position: "absolute",
    left: LEFT_GUTTER,
    right: RIGHT_GUTTER,
    bottom: BOTTOM_GUTTER,
    height: 1,
    backgroundColor: "rgba(242,140,40,0.16)"
  },
  xGuideLine: {
    position: "absolute",
    width: 1,
    backgroundColor: "rgba(242,140,40,0.1)"
  },
  xMinorTick: {
    position: "absolute",
    width: 1,
    backgroundColor: "rgba(242,140,40,0.16)"
  },
  xCheckpointWrap: {
    position: "absolute",
    bottom: 2,
    width: X_LABEL_WIDTH,
    alignItems: "center"
  },
  xLabel: {
    color: palette.textSecondary,
    ...typography.caption
  }
}));

