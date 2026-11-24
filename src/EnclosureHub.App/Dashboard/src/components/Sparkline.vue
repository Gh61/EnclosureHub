<script setup lang="ts">
import { computed } from 'vue';
import { LiveSampleCount } from '../stores/live';

// Inline sparkline of live readings: one thin line, newest sample at the right edge, growing leftwards as
// samples arrive. The scale is min–max of the shown samples (not from zero) so small variations – 1.00 vs
// 0.97 fps – stay visible; two short ticks with the max (top) and min (bottom) labels say what the scale is.
const props = withDefaults(
  defineProps<{
    values: number[];
    unit?: string;
    decimals?: number;
    width?: number;
    height?: number;
    capacity?: number;
  }>(),
  { unit: '', decimals: 1, width: 164, height: 40, capacity: LiveSampleCount },
);

const labelWidth = 36; // right edge of the min/max labels
const tick = 4;
const pad = 2;

const plotLeft = labelWidth + 3 + tick + 2;
const plotWidth = computed(() => props.width - plotLeft - pad);

const stats = computed(() => {
  let min = Infinity;
  let max = -Infinity;
  for (const v of props.values) {
    if (v < min) min = v;
    if (v > max) max = v;
  }
  return { min, max, flat: max - min < 1e-9 };
});

const fmt = (v: number) => (Number.isFinite(v) ? v.toFixed(props.decimals) : '—');

const points = computed(() => {
  const { min, max, flat } = stats.value;
  const n = props.values.length;
  const innerHeight = props.height - 2 * pad;
  return props.values
    .map((v, i) => {
      // Right-aligned: sample i of n sits (n - 1 - i) steps left of the right edge
      const x = plotLeft + plotWidth.value * (1 - (n - 1 - i) / (props.capacity - 1));
      const y = flat ? props.height / 2 : props.height - pad - ((v - min) / (max - min)) * innerHeight;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    })
    .join(' ');
});

const title = computed(() => {
  const last = props.values[props.values.length - 1];
  return `Last ${props.values.length} s · now ${last == null ? '—' : fmt(last)} ${props.unit} · min ${fmt(stats.value.min)} · max ${fmt(stats.value.max)} ${props.unit}`;
});
</script>

<template>
  <svg class="spark" :viewBox="`0 0 ${width} ${height}`" :width="width" :height="height" role="img">
    <title>{{ title }}</title>
    <text class="spark-label" :x="labelWidth" :y="pad + 7" text-anchor="end">{{ fmt(stats.max) }}</text>
    <text class="spark-label" :x="labelWidth" :y="height - pad - 1" text-anchor="end">{{ fmt(stats.min) }}</text>
    <line class="spark-axis" :x1="labelWidth + 3" :x2="labelWidth + 3 + tick" :y1="pad + 0.5" :y2="pad + 0.5" />
    <line class="spark-axis" :x1="labelWidth + 3" :x2="labelWidth + 3 + tick" :y1="height - pad - 0.5" :y2="height - pad - 0.5" />
    <polyline v-if="values.length > 1" :points="points" />
  </svg>
</template>
