import { computed, reactive } from 'vue';
import { api, type SettingsSection } from '../api';

export const settings = reactive({
  sections: [] as SettingsSection[],
  loaded: false,
  error: '',
});

/** Tabs: one per group, in order of first use; sections keep their order within a tab. */
export const settingsGroups = computed(() => {
  const groups: string[] = [];
  for (const s of settings.sections) if (!groups.includes(s.group)) groups.push(s.group);
  return groups;
});

export async function loadSettings(force = false) {
  if (settings.loaded && !force) return;
  try {
    settings.sections = await api<SettingsSection[]>('/api/settings');
    settings.loaded = true;
    settings.error = '';
  } catch (err) {
    settings.error = 'Could not load settings: ' + (err as Error).message;
  }
}

// The last opened tab is remembered, so "#settings" (sidebar, Camera page) returns to it
const LastTabKey = 'enclosurehub.settingsTab';

export const lastSettingsTab = {
  get: () => localStorage.getItem(LastTabKey) ?? '',
  set: (group: string) => localStorage.setItem(LastTabKey, group),
};
