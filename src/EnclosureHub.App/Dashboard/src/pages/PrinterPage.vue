<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue';
import { post, type PrinterInfo, type PrinterLogDirection } from '../api';
import { formatDateTime, formatDuration, formatTemp, formatTime, secondsSince } from '../format';
import { fetchPrinter, isRoutineLine, loadPrinterLog, printer } from '../stores/printer';

const status = computed(() => printer.status);
const showRoutine = ref(false);
const logBox = ref<HTMLElement>();
const infoNote = ref({ cls: 'note', text: '' });
const reading = ref(false);

const stateText = computed(() => {
  const p = status.value;
  if (!p) return '';
  return !p.connected ? 'Not connected' : p.printing ? 'Printing' + (p.progress != null ? ` · ${p.progress} %` : '') : 'Idle';
});

const positionText = computed(() => {
  const p = status.value;
  if (!p) return '';
  return [p.layer != null ? `layer ${p.layer}` : null, p.z != null ? `Z ${p.z} mm` : null].filter(Boolean).join(' · ');
});

const printerSub = (info: PrinterInfo | null) =>
  [info?.firmware, info?.uuid ? 'UUID ' + info.uuid : null].filter(Boolean).join(' · ');

const visibleLog = computed(() => printer.log.filter(e => showRoutine.value || !isRoutineLine(e)));
const marks: Record<PrinterLogDirection, string> = { in: '<', out: '>', info: '·' };

async function refresh() {
  const box = logBox.value;
  const atBottom = !box || box.scrollHeight - box.scrollTop - box.clientHeight < 40;
  await fetchPrinter();
  await loadPrinterLog();
  await nextTick();
  if (atBottom && logBox.value) logBox.value.scrollTop = logBox.value.scrollHeight;
}

async function readInfo() {
  reading.value = true;
  infoNote.value = { cls: 'note', text: 'Asking the printer…' };
  try {
    const info = await post<PrinterInfo>('/api/printer/info');
    infoNote.value = { cls: 'note ok', text: [info.machineType, info.firmware].filter(Boolean).join(' · ') || 'The printer answered.' };
  } catch (err) {
    infoNote.value = { cls: 'note err', text: (err as Error).message };
  } finally {
    reading.value = false;
    void refresh();
  }
}

let timer: ReturnType<typeof setInterval> | undefined;
onMounted(async () => {
  await refresh();
  timer = setInterval(refresh, 2000);
});
onBeforeUnmount(() => clearInterval(timer));
</script>

<template>
  <section class="page">
    <h1>Printer</h1>

    <div v-if="!status" class="empty">The printer module is not available.</div>
    <div v-else-if="!status.linked" class="empty">
      The printer is not linked. Switch on the printer connection under <a href="#settings/printer" class="link">Settings → Printer</a>.
    </div>

    <template v-else>
      <div class="rows">
        <div class="row">
          <div>
            <div class="label">Connection</div>
            <div class="sub">{{ status.connected ? status.port : status.error || 'Waiting for the printer (switched off?)' }}</div>
          </div>
          <div class="control">
            <span class="dot" :class="status.connected ? 'on' : 'off'"></span>
            <span class="value">{{ status.connected ? 'Connected' : 'Offline' }}</span>
          </div>
        </div>
        <div class="row">
          <div>
            <div class="label">Printer</div>
            <div v-if="printerSub(status.info)" class="sub">{{ printerSub(status.info) }}</div>
          </div>
          <div class="control"><span class="value">{{ status.info?.machineType || 'Unknown' }}</span></div>
        </div>
        <div class="row">
          <div>
            <div class="label">State</div>
            <div v-if="status.printing || positionText" class="sub">
              {{ status.printing ? [status.file, positionText].filter(Boolean).join(' · ') : positionText }}
            </div>
          </div>
          <div class="control">
            <span v-if="status.printing" class="dot on"></span>
            <span class="value">{{ stateText }}</span>
          </div>
        </div>
        <div class="row">
          <div><div class="label">Nozzle</div></div>
          <div class="control"><span class="value">{{ formatTemp(status.nozzleTemperature, status.nozzleTarget) }}</span></div>
        </div>
        <div class="row">
          <div><div class="label">Bed</div></div>
          <div class="control"><span class="value">{{ formatTemp(status.bedTemperature, status.bedTarget) }}</span></div>
        </div>
        <div class="row">
          <div>
            <div class="label">Last message</div>
            <div class="sub">{{ status.lastReceivedUtc ? formatDateTime(status.lastReceivedUtc) : 'Nothing received yet' }}</div>
          </div>
          <div class="control">
            <span class="value">{{ status.lastReceivedUtc ? formatDuration(secondsSince(status.lastReceivedUtc)) + ' ago' : '—' }}</span>
          </div>
        </div>
      </div>

      <div class="row">
        <div>
          <div class="label">Read printer information</div>
          <div class="desc">
            Asks the printer who it is (M115) and for its temperatures and print state – a quick check that the USB connection works
            while the printer is idle.
          </div>
        </div>
        <div class="control">
          <span :class="infoNote.cls">{{ infoNote.text }}</span>
          <button class="btn" type="button" :disabled="reading" @click="readInfo">Read</button>
        </div>
      </div>

      <div class="section">
        <div class="log-head">
          <h2>Communication</h2>
          <label class="check"><input v-model="showRoutine" type="checkbox" /> Show temperature and busy reports</label>
        </div>
        <p class="section-desc">
          Last 500 lines: <span class="dir-out">&gt;</span> sent by the hub, <span class="dir-in">&lt;</span> received from the printer,
          <span class="dir-info">·</span> connection notes.
        </p>
        <div ref="logBox" class="log">
          <div v-if="visibleLog.length === 0" class="empty">No communication yet.</div>
          <div v-for="e in visibleLog" :key="e.sequence" class="log-line" :class="'dir-' + e.direction">
            <span class="log-time">{{ formatTime(e.utc) }}</span>
            <span class="log-dir">{{ marks[e.direction] }}</span>
            <span>{{ e.text }}</span>
          </div>
        </div>
      </div>
    </template>
  </section>
</template>
