import { useMemo } from "react";
import { StyleSheet, View, useWindowDimensions } from "react-native";
import Svg, { Circle, Defs, Ellipse, LinearGradient, Path, Stop } from "react-native-svg";
import { mulberry32, pickSeeded, seededInRange } from "./decorativeRandom";

// Easter decorative layer (THEME-001): softly scattered eggs behind content.
// Each egg draws a seeded-random style (stripes, dots, chevron, or gradient)
// and palette so no two neighbours repeat one look. The layer is non-
// interactive and low-opacity by contract - it may never obscure financial
// content, authentication, or confirmations.

const EGG_STYLES = ["stripes", "dots", "chevron", "gradient"] as const;
type EggStyle = (typeof EGG_STYLES)[number];

const EGG_PALETTES = [
  { shell: "#B9A7E0", detail: "#8A6FC4" },
  { shell: "#A7CFA0", detail: "#6E9E66" },
  { shell: "#F2C6A0", detail: "#D99A5B" },
  { shell: "#A9C7E8", detail: "#6E9CCB" },
  { shell: "#EFB8C8", detail: "#D387A4" }
] as const;

type EggSpec = {
  key: string;
  x: number;
  y: number;
  size: number;
  rotation: number;
  style: EggStyle;
  shell: string;
  detail: string;
};

// Eggs keep to the edges and corners so the content column stays calm.
const EGG_ANCHOR_ZONES = [
  { xMin: 0.02, xMax: 0.16, yMin: 0.06, yMax: 0.2 },
  { xMin: 0.82, xMax: 0.96, yMin: 0.1, yMax: 0.24 },
  { xMin: 0.04, xMax: 0.18, yMin: 0.4, yMax: 0.54 },
  { xMin: 0.8, xMax: 0.94, yMin: 0.46, yMax: 0.6 },
  { xMin: 0.06, xMax: 0.2, yMin: 0.74, yMax: 0.86 },
  { xMin: 0.78, xMax: 0.92, yMin: 0.78, yMax: 0.9 },
  { xMin: 0.4, xMax: 0.56, yMin: 0.9, yMax: 0.96 },
  { xMin: 0.44, xMax: 0.6, yMin: 0.02, yMax: 0.07 }
] as const;

function buildEggSpecs(width: number, height: number): EggSpec[] {
  return EGG_ANCHOR_ZONES.map((zone, index) => {
    const random = mulberry32(0x5ea50000 + index * 97);
    const style = pickSeeded(random, EGG_STYLES);
    const palette = pickSeeded(random, EGG_PALETTES);

    return {
      key: `egg-${index}`,
      x: seededInRange(random, zone.xMin, zone.xMax) * width,
      y: seededInRange(random, zone.yMin, zone.yMax) * height,
      size: seededInRange(random, 26, 44),
      rotation: seededInRange(random, -24, 24),
      style,
      shell: palette.shell,
      detail: palette.detail
    };
  });
}

function EggMotif({ spec }: { spec: EggSpec }) {
  const width = spec.size;
  const height = spec.size * 1.32;
  const centerX = width / 2;
  const centerY = height / 2;
  const gradientId = `${spec.key}-gradient`;

  return (
    <Svg
      pointerEvents="none"
      width={width}
      height={height}
      viewBox={`0 0 ${width} ${height}`}
      style={{
        position: "absolute",
        left: spec.x,
        top: spec.y,
        transform: [{ rotate: `${spec.rotation}deg` }]
      }}
    >
      <Defs>
        <LinearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
          <Stop offset="0" stopColor={spec.shell} />
          <Stop offset="1" stopColor={spec.detail} />
        </LinearGradient>
      </Defs>

      <Ellipse
        cx={centerX}
        cy={centerY}
        rx={width * 0.42}
        ry={height * 0.46}
        fill={spec.style === "gradient" ? `url(#${gradientId})` : spec.shell}
      />

      {spec.style === "stripes" ? (
        <>
          <Path
            d={`M ${width * 0.1} ${height * 0.36} Q ${centerX} ${height * 0.28} ${width * 0.9} ${height * 0.36}`}
            stroke={spec.detail}
            strokeWidth={width * 0.07}
            fill="none"
          />
          <Path
            d={`M ${width * 0.07} ${height * 0.58} Q ${centerX} ${height * 0.5} ${width * 0.93} ${height * 0.58}`}
            stroke={spec.detail}
            strokeWidth={width * 0.07}
            fill="none"
          />
        </>
      ) : null}

      {spec.style === "dots" ? (
        <>
          <Circle cx={centerX - width * 0.16} cy={centerY - height * 0.1} r={width * 0.06} fill={spec.detail} />
          <Circle cx={centerX + width * 0.14} cy={centerY + height * 0.02} r={width * 0.055} fill={spec.detail} />
          <Circle cx={centerX - width * 0.04} cy={centerY + height * 0.16} r={width * 0.05} fill={spec.detail} />
          <Circle cx={centerX + width * 0.1} cy={centerY - height * 0.18} r={width * 0.045} fill={spec.detail} />
        </>
      ) : null}

      {spec.style === "chevron" ? (
        <>
          <Path
            d={`M ${width * 0.14} ${height * 0.5} L ${width * 0.3} ${height * 0.42} L ${width * 0.46} ${height * 0.5} L ${width * 0.62} ${height * 0.42} L ${width * 0.78} ${height * 0.5}`}
            stroke={spec.detail}
            strokeWidth={width * 0.06}
            fill="none"
          />
          <Path
            d={`M ${width * 0.16} ${height * 0.66} L ${width * 0.32} ${height * 0.58} L ${width * 0.48} ${height * 0.66} L ${width * 0.64} ${height * 0.58} L ${width * 0.8} ${height * 0.66}`}
            stroke={spec.detail}
            strokeWidth={width * 0.06}
            fill="none"
          />
        </>
      ) : null}
    </Svg>
  );
}

export const EASTER_DECORATION_OPACITY = 0.1;

export function EasterDecorativeLayer() {
  const { width, height } = useWindowDimensions();
  const eggs = useMemo(() => buildEggSpecs(width, height), [width, height]);

  return (
    <View pointerEvents="none" style={[StyleSheet.absoluteFillObject, { opacity: EASTER_DECORATION_OPACITY }]}>
      {eggs.map((spec) => (
        <EggMotif key={spec.key} spec={spec} />
      ))}
    </View>
  );
}
