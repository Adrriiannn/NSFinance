import { useMemo, useState } from "react";
import { LayoutChangeEvent, StyleSheet, View } from "react-native";
import { palette } from "../../theme/tokens";

type SpendTrendGraphProps = {
  primarySeries: number[];
  secondarySeries: number[];
  height?: number;
};

type GraphPoint = {
  x: number;
  y: number;
};

const MIN_WIDTH = 220;

function toPoints(values: number[], width: number, height: number, max: number): GraphPoint[] {
  if (width <= 0 || height <= 0) {
    return [];
  }

  const horizontalPadding = 10;
  const verticalPadding = 8;
  const drawWidth = Math.max(width - horizontalPadding * 2, 1);
  const drawHeight = Math.max(height - verticalPadding * 2, 1);
  const normalizedValues = values.length <= 1 ? [values[0] ?? 0, values[0] ?? 0] : values;

  return normalizedValues.map((value, index) => {
    const ratio = normalizedValues.length <= 1 ? 0 : index / (normalizedValues.length - 1);
    return {
      x: horizontalPadding + ratio * drawWidth,
      y: verticalPadding + (1 - value / max) * drawHeight
    };
  });
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
      width: length,
      angle: `${Math.atan2(dy, dx)}rad`
    });
  }

  return segments;
}

function LineSeries({ points, color }: { points: GraphPoint[]; color: string }) {
  const segments = useMemo(() => buildSegments(points), [points]);

  return (
    <>
      {segments.map((segment) => (
        <View
          key={segment.key}
          style={[
            styles.segment,
            {
              backgroundColor: color,
              width: Math.max(segment.width, 1),
              left: segment.x,
              top: segment.y,
              transform: [{ rotateZ: segment.angle }]
            }
          ]}
        />
      ))}
      {points.map((point, index) => (
        <View
          key={`${point.x}-${point.y}-${index}`}
          style={[
            styles.dot,
            {
              backgroundColor: color,
              left: point.x - 3,
              top: point.y - 3
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
  height = 124
}: SpendTrendGraphProps) {
  const [width, setWidth] = useState(MIN_WIDTH);

  const onLayout = (event: LayoutChangeEvent) => {
    const nextWidth = Math.max(event.nativeEvent.layout.width, MIN_WIDTH);
    if (Math.abs(nextWidth - width) > 1) {
      setWidth(nextWidth);
    }
  };

  const maxValue = useMemo(() => {
    const allValues = [...primarySeries, ...secondarySeries];
    const found = Math.max(...allValues, 1);
    return Number.isFinite(found) ? found : 1;
  }, [primarySeries, secondarySeries]);

  const primaryPoints = useMemo(
    () => toPoints(primarySeries, width, height, maxValue),
    [height, maxValue, primarySeries, width]
  );
  const secondaryPoints = useMemo(
    () => toPoints(secondarySeries, width, height, maxValue),
    [height, maxValue, secondarySeries, width]
  );

  return (
    <View style={[styles.graph, { height }]} onLayout={onLayout}>
      <View style={styles.axisX} />
      <View style={styles.axisY} />
      <LineSeries points={secondaryPoints} color={palette.negative} />
      <LineSeries points={primaryPoints} color={palette.success} />
    </View>
  );
}

const styles = StyleSheet.create({
  graph: {
    position: "relative",
    width: "100%",
    overflow: "hidden"
  },
  axisX: {
    position: "absolute",
    left: 0,
    right: 0,
    bottom: 6,
    height: 1,
    backgroundColor: "rgba(220,232,255,0.12)"
  },
  axisY: {
    position: "absolute",
    top: 0,
    bottom: 0,
    left: 10,
    width: 1,
    backgroundColor: "rgba(220,232,255,0.12)"
  },
  segment: {
    position: "absolute",
    height: 2,
    borderRadius: 1
  },
  dot: {
    position: "absolute",
    width: 6,
    height: 6,
    borderRadius: 3
  }
});