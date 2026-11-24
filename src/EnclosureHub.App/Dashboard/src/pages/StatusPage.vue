<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted } from 'vue';
import type { Entity } from '../api';
import { entityList, refreshEntity, entities, sendOnOff } from '../stores/entities';
import { hasLive, liveDecimals, liveSamples, liveUnit, recordLive } from '../stores/live';
import Sparkline from '../components/Sparkline.vue';

// CPU load first (it has the graph), the rest in registry order
const ordered = computed(() => {
  const list = entityList.value;
  return [...list.filter(e => e.id === 'cpu_load'), ...list.filter(e => e.id !== 'cpu_load')];
});

const kindLabel: Record<Entity['kind'], string> = {
  sensor: 'Sensor', binarySensor: 'Binary sensor', switch: 'Switch', camera: 'Camera', light: 'Light', fan: 'Fan',
};

function formatState(e: Entity): string {
  switch (e.kind) {
    case 'sensor': {
      // Live reading (1 s sample) when the sensor has one; the entity value is the slower average sent to HA
      const v = e.state.live ?? e.state.value;
      return v == null ? '—' : `${v}${e.state.unit ? ' ' + e.state.unit : ''}`;
    }
    case 'binarySensor':
    case 'switch':
      return e.state.isOn == null ? 'Unknown' : e.state.isOn ? 'On' : 'Off';
    case 'camera':
      return e.state.hasFrame ? `Live · ${e.state.capturedFps ?? '?'} fps · ${e.state.frameIntervalMs ?? '?'} ms` : 'No image';
    case 'light':
      return e.state.isOn ? `On · ${Math.round((e.state.brightness ?? 255) / 2.55)} %` : 'Off';
    case 'fan':
      return e.state.isOn ? `On · ${e.state.percentage ?? '?'} %` : 'Off';
  }
}

function dotClass(e: Entity): string {
  switch (e.kind) {
    case 'binarySensor':
    case 'switch':
      return e.state.isOn == null ? '' : e.state.isOn ? 'on' : 'off';
    case 'camera':
      return e.state.hasFrame ? 'on' : 'off';
    case 'light':
    case 'fan':
      return e.state.isOn ? 'on' : 'off';
    case 'sensor':
      return e.state.value == null ? 'err' : 'on';
  }
}

/** Secondary line: kind, id, and what the live reading relates to. */
function subText(e: Entity): string {
  let text = `${kindLabel[e.kind]} · ${e.id}${e.isDiagnostic ? ' · diagnostic' : ''}`;
  if (e.kind === 'sensor' && e.state.live !== undefined && e.state.value != null)
    text += ` · ${e.state.value}${e.state.unit ? ' ' + e.state.unit : ''} average sent to Home Assistant`;
  if (e.kind === 'camera' && e.state.hasFrame && e.state.frameAgeSeconds != null) text += ` · frame ${e.state.frameAgeSeconds} s ago`;
  return text;
}

const switchable = (e: Entity) => e.kind === 'switch' || e.kind === 'light' || e.kind === 'fan';
const isOn = (e: Entity) => e.kind !== 'camera' && e.kind !== 'sensor' && e.state.isOn === true;

// Live readings raise no change events: poll them every second while the page is open and graph them
let timer: ReturnType<typeof setInterval> | undefined;
let polling = false;
async function poll() {
  if (polling) return;
  polling = true;
  try {
    for (const id of entities.order) {
      const e = entities.byId[id];
      if (!e || !hasLive(e)) continue;
      await refreshEntity(id);
      const fresh = entities.byId[id];
      if (fresh) recordLive(fresh);
    }
  } finally {
    polling = false;
  }
}
onMounted(() => {
  void poll();
  timer = setInterval(poll, 1000);
});
onBeforeUnmount(() => clearInterval(timer));
</script>

<template>
  <section class="page">
    <h1>Status</h1>
    <p class="lead">All entities of this controller, updated live. Graphs show the last two minutes while this page is open.</p>
    <div class="rows">
      <div v-if="ordered.length === 0" class="empty">No entities registered.</div>
      <div v-for="e in ordered" :key="e.id" class="row">
        <div>
          <div class="label">{{ e.name }}</div>
          <div class="sub">{{ subText(e) }}</div>
        </div>
        <div class="control">
          <Sparkline v-if="liveSamples[e.id]?.length" :values="liveSamples[e.id]!" :unit="liveUnit(e)" :decimals="liveDecimals(e)" />
          <span class="dot" :class="dotClass(e)"></span>
          <span class="value">{{ formatState(e) }}</span>
          <button v-if="switchable(e)" class="btn" type="button" @click="sendOnOff(e.id, !isOn(e))">
            {{ isOn(e) ? 'Turn off' : 'Turn on' }}
          </button>
        </div>
      </div>
    </div>
  </section>
</template>
