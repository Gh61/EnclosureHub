<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { api, post, type SystemInfo } from '../api';
import { formatDateTime, formatMegabytes, formatUptime } from '../format';
import ConfirmButton from '../components/ConfirmButton.vue';

const info = ref<SystemInfo>();
const error = ref('');
const note = ref({ cls: 'note', text: '' });

const rows = (s: SystemInfo): [string, string, string | null][] => [
  ['Device', s.device, s.deviceId],
  ['Version', s.version, null],
  ['Host', s.hostname, `${s.os} · ${s.architecture}`],
  ['Runtime', s.runtime, s.underSystemd ? 'running under systemd' : 'not running under systemd'],
  ['Uptime', formatUptime(s.uptimeSeconds), 'since ' + formatDateTime(s.startedUtc)],
  ['Memory', formatMegabytes(s.workingSetBytes), `${formatMegabytes(s.managedMemoryBytes)} managed heap`],
];

onMounted(async () => {
  try {
    info.value = await api<SystemInfo>('/api/system');
  } catch (err) {
    error.value = 'Could not load system information: ' + (err as Error).message;
  }
});

// Graceful shutdown; systemd starts the service again, the page reloads once /health answers
async function restart() {
  await post('/api/system/restart');
  note.value = { cls: 'note', text: 'Restarting… this page reloads when the service is back.' };
  await waitForHealth();
  location.reload();
}

async function waitForHealth() {
  await new Promise(r => setTimeout(r, 3000));
  for (let i = 0; i < 60; i++) {
    try {
      const r = await fetch('/health', { cache: 'no-store' });
      if (r.ok) return;
    } catch {
      // not up yet
    }
    await new Promise(r => setTimeout(r, 2000));
  }
  throw new Error('The service did not come back within two minutes.');
}

function onArmed(armed: boolean) {
  note.value = { cls: 'note', text: armed ? 'Click again to restart the service.' : '' };
}
</script>

<template>
  <section class="page">
    <h1>System</h1>
    <div class="rows">
      <div v-if="error" class="note err">{{ error }}</div>
      <template v-if="info">
        <div v-for="[label, value, sub] in rows(info)" :key="label" class="row">
          <div>
            <div class="label">{{ label }}</div>
            <div v-if="sub" class="sub">{{ sub }}</div>
          </div>
          <div class="control"><span class="value">{{ value }}</span></div>
        </div>
      </template>
    </div>
    <div class="row">
      <div>
        <div class="label">Restart service</div>
        <div class="desc">
          Stops the application cleanly; systemd starts it again within a few seconds. Needed after enabling a module that was off
          at startup.
        </div>
      </div>
      <div class="control">
        <span :class="note.cls">{{ note.text }}</span>
        <ConfirmButton label="Restart" busy-label="Restarting" :timeout="6000" :action="restart" @armed="onArmed" @error="note = { cls: 'note err', text: $event }" />
      </div>
    </div>
  </section>
</template>
