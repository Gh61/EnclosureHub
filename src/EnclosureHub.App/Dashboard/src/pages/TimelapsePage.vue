<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { del, enc, post, type TimelapsePending } from '../api';
import { formatCount, formatDateTime, formatDuration, formatSize, secondsSince } from '../format';
import { entities, sendOnOff } from '../stores/entities';
import { fetchTimelapse, hoursOfSpace, loadVideos, pendingKey, timelapse } from '../stores/timelapse';
import ConfirmButton from '../components/ConfirmButton.vue';
import ToggleButton from '../components/ToggleButton.vue';

const error = ref('');
const status = computed(() => timelapse.info?.status ?? null);
const current = computed(() => status.value?.current ?? null);

const recordingText = computed(() => {
  const s = status.value;
  const cur = current.value;
  if (!s || !cur) return '';
  let text = `Since ${formatDateTime(cur.startedUtc)} · ${formatDuration(secondsSince(cur.startedUtc))} · ${formatCount(cur.frames)} frames · ${formatSize(cur.bytes)}`;
  const trigger =
    s.trigger === 'layers' ? 'Frames follow the printer layers'
      : s.trigger === 'layersAndInterval' ? `Frames follow the printer layers, and every ${s.intervalSeconds} s during long layers`
      : null;
  if (trigger) text += ` · ${trigger}` + (s.lastLayer != null ? ` (last: layer ${s.lastLayer})` : '');
  return text;
});

const storageText = computed(() => {
  const hours = hoursOfSpace(status.value, status.value?.intervalSeconds ?? 0);
  return hours == null ? 'Free space unknown' : `Room for about ${formatDuration(hours * 3600)} of recording at the current interval`;
});

function pendingText(p: TimelapsePending) {
  const state =
    p.state === 'rendering' ? `Rendering · ${Math.round((p.progress || 0) * 100)} %`
      : p.state === 'queued' ? 'Waiting to be rendered'
      : 'Rendering failed' + (p.error ? ': ' + p.error : '');
  return `${state} · ${formatCount(p.frames)} frames · ${formatSize(p.bytes)}`;
}

function videoDetails(v: (typeof timelapse.videos)[number]) {
  const recorded = v.startedUtc && v.stoppedUtc ? (Date.parse(v.stoppedUtc) - Date.parse(v.startedUtc)) / 1000 : null;
  return [
    v.durationSeconds != null ? `${formatDuration(v.durationSeconds)} video` : null,
    v.frames != null ? `${formatCount(v.frames)} frames at ${v.fps} fps` : null,
    recorded != null ? `recorded ${formatDuration(recorded)}` : null,
    formatSize(v.sizeBytes),
  ]
    .filter(Boolean)
    .join(' · ');
}

const videoUrl = (name: string) => `/api/timelapse/videos/${enc(name)}`;

async function refresh() {
  const before = pendingKey();
  await fetchTimelapse();
  if (pendingKey() !== before) await refreshVideos(); // a render finished or failed
}

async function refreshVideos() {
  if (!timelapse.info) return;
  try {
    await loadVideos();
  } catch (err) {
    error.value = 'Could not load the videos: ' + (err as Error).message;
  }
}

async function toggleRecording(on: boolean) {
  if (!timelapse.info) return;
  await sendOnOff(timelapse.info.switchId, on);
  await refresh();
}

async function retry(name: string) {
  try {
    await post(`/api/timelapse/recordings/${enc(name)}/render`);
    await refresh();
  } catch (err) {
    error.value = (err as Error).message;
  }
}

const discard = (name: string) => async () => {
  await del(`/api/timelapse/recordings/${enc(name)}`);
  await refresh();
};

const deleteVideo = (name: string) => async () => {
  await del(videoUrl(name));
  timelapse.videosKey = '';
  await refreshVideos();
};

// Progress (frames, render percentage) is polled while the page is on screen; switch changes arrive over SSE
let timer: ReturnType<typeof setInterval> | undefined;
onMounted(async () => {
  await fetchTimelapse();
  await refreshVideos();
  timer = setInterval(refresh, 3000);
});
onBeforeUnmount(() => clearInterval(timer));

const switchId = computed(() => timelapse.info?.switchId ?? 'timelapse');
watch(
  () => [entities.byId[switchId.value]?.lastChangedUtc, entities.byId[switchId.value + '_rendering']?.lastChangedUtc],
  () => void refresh(),
);
</script>

<template>
  <section class="page">
    <h1>Timelapse</h1>

    <div v-if="!status" class="section empty">
      {{ timelapse.available ? 'Could not load the timelapse status.' : 'The timelapse module is turned off (Modules:Timelapse:Enabled in appsettings.Local.json).' }}
    </div>

    <div v-else class="section rows">
      <div class="row">
        <div>
          <div class="label">{{ status.recording ? 'Recording' : 'Not recording' }}</div>
          <div v-if="current" class="sub">{{ recordingText }}</div>
          <div v-else class="sub">
            A frame every {{ status.intervalSeconds }} s; stops automatically after {{ status.maxDurationHours }} h.
            <a href="#settings/timelapse" class="link">Settings</a>
          </div>
        </div>
        <div class="control">
          <span class="dot" :class="status.recording ? 'on' : 'off'"></span>
          <ToggleButton
            :model-value="status.recording"
            :title="status.recording ? 'Stop the recording and render the video' : 'Start a new recording'"
            @update:model-value="toggleRecording"
          />
        </div>
      </div>

      <div v-if="!status.ffmpeg.available" class="row">
        <div class="note err">
          ffmpeg is not installed, so videos cannot be rendered (frames are still saved).
          <a href="#settings/timelapse" class="link">Install it in Settings</a>.
        </div>
        <div></div>
      </div>
      <div v-if="status.recording && !status.cameraAvailable" class="row">
        <div class="note err">The camera is off or not delivering frames – nothing is being saved until it runs again.</div>
        <div></div>
      </div>

      <div class="row">
        <div>
          <div class="label">Storage</div>
          <div class="sub">{{ storageText }}</div>
        </div>
        <div class="control"><span class="value">{{ formatSize(status.freeBytes) }} free</span></div>
      </div>

      <div v-for="p in status.pending" :key="p.name" class="row">
        <div>
          <div class="label">{{ p.name }}</div>
          <div class="sub" :class="{ err: p.state === 'failed' }">{{ pendingText(p) }}</div>
        </div>
        <div class="control">
          <template v-if="p.state === 'failed'">
            <button class="btn" type="button" @click="retry(p.name)">Retry</button>
            <ConfirmButton label="Discard" :action="discard(p.name)" @error="error = $event" />
          </template>
          <span v-else class="dot" :class="p.state === 'rendering' ? 'on' : 'off'"></span>
        </div>
      </div>
    </div>

    <div class="section">
      <h2>Videos</h2>
      <div v-if="error" class="note err">{{ error }}</div>
      <div class="rows">
        <div v-if="status && timelapse.videos.length === 0" class="empty">
          No videos yet. Start a recording above; the video appears here once it is rendered.
        </div>
        <div v-for="v in timelapse.videos" :key="v.name" class="row video">
          <div class="video-info">
            <a class="thumb" :href="videoUrl(v.name) + '.mp4'" target="_blank" rel="noopener" title="Play">
              <img v-if="v.hasThumbnail" :src="videoUrl(v.name) + '.jpg'" alt="" loading="lazy" />
            </a>
            <div>
              <div class="label">{{ formatDateTime(v.startedUtc || v.createdUtc) }}</div>
              <div class="sub">{{ v.name }}</div>
              <div class="sub">{{ videoDetails(v) }}</div>
            </div>
          </div>
          <div class="control">
            <a class="btn" :href="videoUrl(v.name) + '.mp4'" target="_blank" rel="noopener">Play</a>
            <a class="btn" :href="videoUrl(v.name) + '.mp4?download=true'" :download="v.name + '.mp4'">Download</a>
            <ConfirmButton label="Delete" :action="deleteVideo(v.name)" @error="error = $event" />
          </div>
        </div>
      </div>
    </div>
  </section>
</template>
