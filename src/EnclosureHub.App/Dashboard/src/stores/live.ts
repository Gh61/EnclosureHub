import { reactive } from 'vue';
import type { Entity } from '../api';

// 1 s samples collected while the Status page is open (nothing is stored on the server): sensors with a
// "live" value (CPU load) and cameras (frame rate from the time between the last two captured frames).
// Newest last; 120 samples = the last two minutes.
export const LiveSampleCount = 120;

export const liveSamples = reactive<Record<string, number[]>>({});

export const hasLive = (e: Entity) => e.kind === 'camera' || (e.kind === 'sensor' && e.state.live !== undefined);

export function liveValue(e: Entity): number | null {
  if (e.kind === 'camera') return e.state.frameIntervalMs ? Math.round((1000 / e.state.frameIntervalMs) * 1000) / 1000 : null;
  if (e.kind === 'sensor') return e.state.live ?? null;
  return null;
}

export const liveUnit = (e: Entity) => (e.kind === 'camera' ? 'fps' : e.kind === 'sensor' ? e.state.unit || '' : '');

/** Decimals for the graph's min/max labels: frame rates need to show 0.97 vs 1.00. */
export const liveDecimals = (e: Entity) => (e.kind === 'camera' ? 2 : 0);

export function recordLive(e: Entity) {
  const v = liveValue(e);
  if (v == null) return;
  const list = liveSamples[e.id] ?? (liveSamples[e.id] = []);
  list.push(v);
  if (list.length > LiveSampleCount) list.shift();
}
