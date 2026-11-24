<script setup lang="ts">
import { computed, onMounted, watch } from 'vue';
import type { SettingsSection as Section } from '../api';
import { route, slug } from '../router';
import { lastSettingsTab, loadSettings, settings, settingsGroups } from '../stores/settings';
import { fetchPrinter } from '../stores/printer';
import SettingsSection from './settings/SettingsSection.vue';
import TimelapseSettingsExtras from './settings/TimelapseSettingsExtras.vue';
import PrinterRelated from './settings/PrinterRelated.vue';
import { timelapseFieldNote } from './settings/timelapseEstimates';

// "#settings/<tab>" opens that tab; plain "#settings" returns to the last opened one
const activeTab = computed(() => {
  const groups = settingsGroups.value;
  const requested = groups.find(g => slug(g) === route.tab);
  if (requested) return requested;
  const last = lastSettingsTab.get();
  return groups.includes(last) ? last : (groups[0] ?? '');
});
watch(activeTab, tab => tab && lastSettingsTab.set(tab), { immediate: true });

const sectionsOf = (group: string) => settings.sections.filter(s => s.group === group);

// A section alone on its tab with the tab's name needs no second heading
const hideTitle = (section: Section) => section.title === section.group && sectionsOf(section.group).length === 1;

async function onSaved(section: Section) {
  // The printer link adds or removes fields on other tabs and the Printer page: load everything again
  if (section.id === 'printer') {
    await loadSettings(true);
    await fetchPrinter();
  }
}

onMounted(() => loadSettings());
</script>

<template>
  <section class="page">
    <h1>Settings</h1>
    <p class="lead">Changes are saved to appsettings.Local.json on the device.</p>
    <nav class="tabs" aria-label="Settings groups">
      <a v-for="g in settingsGroups" :key="g" :href="'#settings/' + slug(g)" :class="{ active: g === activeTab }">{{ g }}</a>
    </nav>
    <div v-if="settings.error" class="note err">{{ settings.error }}</div>
    <div v-for="g in settingsGroups" v-show="g === activeTab" :key="g" class="tab-panel">
      <SettingsSection
        v-for="section in sectionsOf(g)"
        :key="section.id"
        :section="section"
        :hide-title="hideTitle(section)"
        :field-note="section.id === 'timelapse' ? timelapseFieldNote : undefined"
        @saved="onSaved"
      >
        <template v-if="section.id === 'timelapse'" #before="{ values }">
          <TimelapseSettingsExtras :values="values" :active="g === activeTab" />
        </template>
        <template v-if="section.id === 'printer'" #after>
          <PrinterRelated v-if="section.values.linked === true" />
        </template>
      </SettingsSection>
    </div>
  </section>
</template>
