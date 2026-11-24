<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { post, type SettingValue } from '../../api';
import { formatSize } from '../../format';
import { fetchTimelapse, timelapse } from '../../stores/timelapse';
import { diskSpaceNote } from './timelapseEstimates';

// Rows above the Timelapse fields: ffmpeg (required, installable from here) and the current free disk space.
const props = defineProps<{ values: Record<string, SettingValue>; active: boolean }>();

const status = computed(() => timelapse.info?.status ?? null);
const ffmpeg = computed(() => status.value?.ffmpeg ?? null);
const installing = computed(() => ffmpeg.value?.installState === 'running');
const installError = ref('');

const ffmpegText = computed(() => {
  const f = ffmpeg.value;
  if (!f) return '—';
  return f.available ? 'Installed' + (f.version ? ' · ' + f.version : '') : installing.value ? 'Installing…' : 'Not installed';
});

const ffmpegNote = computed(() => {
  const f = ffmpeg.value;
  if (installError.value) return { cls: 'note err', text: installError.value };
  if (!f) return { cls: 'note', text: '' };
  if (f.installState !== 'idle') return { cls: 'note' + (f.installState === 'failed' ? ' err' : f.installState === 'succeeded' ? ' ok' : ''), text: f.installMessage || '' };
  return { cls: 'note', text: !f.available && !f.canInstall ? 'Install it on the device: sudo apt install ffmpeg' : '' };
});

const diskText = computed(() => diskSpaceNote(status.value, Number(props.values.interval ?? 0)));

async function install() {
  installError.value = '';
  try {
    await post('/api/timelapse/ffmpeg/install');
  } catch (err) {
    installError.value = (err as Error).message;
  }
  await fetchTimelapse();
}

// ffmpeg installation progress and disk space are polled while this tab is open
let timer: ReturnType<typeof setInterval> | undefined;
watch(
  () => props.active,
  active => {
    clearInterval(timer);
    if (active) {
      void fetchTimelapse();
      timer = setInterval(fetchTimelapse, 3000);
    }
  },
  { immediate: true },
);
onBeforeUnmount(() => clearInterval(timer));
</script>

<template>
  <div class="row">
    <div>
      <div class="label">ffmpeg</div>
      <div class="desc">Required: renders the saved frames into an MP4 video. Installed from the Raspberry Pi OS packages (apt install ffmpeg, about 100 MB).</div>
      <div v-if="ffmpegNote.text" :class="ffmpegNote.cls">{{ ffmpegNote.text }}</div>
    </div>
    <div class="control">
      <span class="dot" :class="ffmpeg?.available ? 'on' : installing ? '' : 'err'"></span>
      <span class="value">{{ ffmpegText }}</span>
      <button v-if="ffmpeg && !ffmpeg.available && ffmpeg.canInstall" class="btn" type="button" :disabled="installing" @click="install">Install</button>
    </div>
  </div>
  <div class="row">
    <div>
      <div class="label">Free disk space</div>
      <div class="desc">{{ diskText }}</div>
    </div>
    <div class="control"><span class="value">{{ status?.freeBytes == null ? 'Unknown' : formatSize(status.freeBytes) + ' free' }}</span></div>
  </div>
</template>
