<script setup lang="ts">
import { computed, onMounted } from 'vue';
import { route, type Page } from './router';
import { connectEvents, connection, device, loadDevice, loadEntities } from './stores/entities';
import { fetchTimelapse, timelapse } from './stores/timelapse';
import { fetchPrinter, printer } from './stores/printer';
import CameraPage from './pages/CameraPage.vue';
import TimelapsePage from './pages/TimelapsePage.vue';
import PrinterPage from './pages/PrinterPage.vue';
import StatusPage from './pages/StatusPage.vue';
import SettingsPage from './pages/SettingsPage.vue';
import SystemPage from './pages/SystemPage.vue';

// Timelapse is listed while its module is on; Printer only while a printer is linked
const nav = computed<{ page: Page; label: string; show: boolean }[]>(() => [
  { page: 'camera', label: 'Camera', show: true },
  { page: 'timelapse', label: 'Timelapse', show: timelapse.available },
  { page: 'printer', label: 'Printer', show: printer.status?.linked === true },
  { page: 'status', label: 'Status', show: true },
  { page: 'settings', label: 'Settings', show: true },
  { page: 'system', label: 'System', show: true },
]);

onMounted(async () => {
  await loadEntities();
  await Promise.all([fetchTimelapse(), fetchPrinter(), loadDevice()]);
  connectEvents();
});
</script>

<template>
  <nav class="sidebar" aria-label="Sections">
    <div class="brand">
      <div class="brand-title">Enclosure Hub</div>
      <div class="brand-sub">{{ device.name || ' ' }}</div>
    </div>
    <template v-for="item in nav" :key="item.page">
      <a v-if="item.show" :href="'#' + item.page" :class="{ active: route.page === item.page }">{{ item.label }}</a>
    </template>
    <div class="conn"><span class="dot" :class="connection.state"></span><span>{{ connection.text }}</span></div>
  </nav>

  <main>
    <CameraPage v-if="route.page === 'camera'" />
    <TimelapsePage v-else-if="route.page === 'timelapse'" />
    <PrinterPage v-else-if="route.page === 'printer'" />
    <StatusPage v-else-if="route.page === 'status'" />
    <SettingsPage v-else-if="route.page === 'settings'" />
    <SystemPage v-else-if="route.page === 'system'" />
  </main>
</template>
