const WIDTH = 640;
const HEIGHT = 240;
const MARGIN = { top: 18, right: 18, bottom: 36, left: 58 };

export function buildTrendChartModel(data, valueKey = "count", money = false) {
  const values = data.map(item => finiteValue(item?.[valueKey]));
  const present = values.filter(value => value !== null);
  const scale = niceScale(Math.max(0, ...present), money);
  const plotWidth = WIDTH - MARGIN.left - MARGIN.right;
  const plotHeight = HEIGHT - MARGIN.top - MARGIN.bottom;
  const baseline = MARGIN.top + plotHeight;
  const points = values.map((value, index) => {
    const x = data.length <= 1 ? MARGIN.left + plotWidth / 2 : MARGIN.left + index * plotWidth / (data.length - 1);
    return { index, x, y: value === null ? null : MARGIN.top + (scale.max - value) * plotHeight / scale.max, value };
  });
  const segments = splitSegments(points);
  return {
    width: WIDTH,
    height: HEIGHT,
    margin: MARGIN,
    baseline,
    hasData: present.length > 0,
    values,
    points,
    linePath: segments.map(segment => `M ${segment.map(point => `${point.x} ${point.y}`).join(" L ")}`).join(" "),
    areaPath: segments.map(segment => `M ${segment[0].x} ${baseline} L ${segment.map(point => `${point.x} ${point.y}`).join(" L ")} L ${segment.at(-1).x} ${baseline} Z`).join(" "),
    yTicks: scale.ticks.map(value => ({ value, y: MARGIN.top + (scale.max - value) * plotHeight / scale.max })),
    xLabels: labelIndexes(data.length).map(index => ({ index, x: points[index].x, value: data[index]?.date })),
    total: present.reduce((sum, value) => sum + value, 0),
    last: present.at(-1) ?? 0,
  };
}

function finiteValue(raw) {
  if (raw === null || raw === undefined) return null;
  const value = Number(raw);
  return Number.isFinite(value) ? value : null;
}

function splitSegments(points) {
  const segments = []; let current = [];
  for (const point of points) {
    if (point.value === null) {
      if (current.length) segments.push(current);
      current = [];
    } else current.push(point);
  }
  if (current.length) segments.push(current);
  return segments;
}

function niceScale(maxValue, money) {
  if (maxValue <= 0) return { max: 1, ticks: [0, 1] };
  const rough = maxValue / 4;
  const power = 10 ** Math.floor(Math.log10(rough));
  const normalized = rough / power;
  const factor = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
  const step = money ? factor * power : Math.max(1, factor * power);
  const max = Math.ceil(maxValue / step) * step;
  const ticks = [];
  for (let value = 0; value <= max + step / 2; value += step) ticks.push(value);
  return { max, ticks };
}

function labelIndexes(length) {
  if (!length) return [];
  const step = Math.max(1, Math.ceil(length / 5));
  const indexes = [];
  for (let index = 0; index < length; index += step) indexes.push(index);
  if (indexes.at(-1) !== length - 1) indexes.push(length - 1);
  return indexes;
}
