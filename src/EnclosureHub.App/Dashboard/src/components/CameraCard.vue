<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { enc, type CameraEntity } from '../api';
import { entities, refreshEntity, sendOnOff } from '../stores/entities';
import ToggleButton from './ToggleButton.vue';

const props = defineProps<{ camera: CameraEntity }>();

// The "{id}_active" switch is ON only while the camera is switched on and producing frames
const activeSwitch = computed(() => {
  const e = entities.byId[props.camera.id + '_active'];
  return e?.kind === 'switch' ? e : undefined;
});
const isOn = computed(() => (activeSwitch.value ? activeSwitch.value.state.isOn === true : props.camera.state.hasFrame));

const snapshotUrl = computed(() => `/api/cameras/${enc(props.camera.id)}/snapshot.jpg`);
const streamUrl = computed(() => `/api/cameras/${enc(props.camera.id)}/stream.mjpeg`);

// Stream only while this card is on screen (the page unmounts it) and the camera is not switched off – saves CPU on the Pi
const shouldStream = computed(() => !activeSwitch.value || activeSwitch.value.state.isOn !== false);
const src = ref<string>();
const loaded = ref(false);
watch(
  shouldStream,
  on => {
    src.value = on ? `${streamUrl.value}?t=${Date.now()}` : undefined;
    loaded.value = false;
  },
  { immediate: true },
);

const placeholder = computed(() =>
  !isOn.value ? 'Camera is off or not producing frames.' : !loaded.value || !props.camera.state.hasFrame ? 'Waiting for the first frame…' : '',
);

// Camera state (frame age) raises no change events; refresh it while the card is shown
let timer: ReturnType<typeof setInterval> | undefined;
onMounted(() => {
  timer = setInterval(() => refreshEntity(props.camera.id), 5000);
});
onBeforeUnmount(() => clearInterval(timer));

function toggle(on: boolean) {
  if (activeSwitch.value) void sendOnOff(activeSwitch.value.id, on);
}
</script>

<template>
  <div class="camera">
    <div class="camera-view">
      <img v-if="src" :src="src" :alt="camera.name" @load="loaded = true" />
      <div v-if="placeholder" class="placeholder">{{ placeholder }}</div>
    </div>
    <div class="camera-bar">
      <span class="name"><span class="dot" :class="isOn ? 'on' : 'off'"></span>{{ camera.name }}</span>
      <span>{{ isOn ? 'Running' : 'Off' }}</span>
      <span class="spacer"></span>
      <ToggleButton
        v-if="activeSwitch"
        :model-value="isOn"
        :title="isOn ? 'Turn the camera off' : 'Turn the camera on (restarts the capture if it is stuck)'"
        @update:model-value="toggle"
      />
      <a :href="snapshotUrl" target="_blank" rel="noopener">Snapshot URL</a>
      <a :href="streamUrl" target="_blank" rel="noopener">Stream URL</a>
      <a href="#settings">Settings</a>
    </div>
  </div>
</template>
