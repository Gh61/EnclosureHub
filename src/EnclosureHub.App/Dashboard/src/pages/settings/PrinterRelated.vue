<script setup lang="ts">
import { computed } from 'vue';
import type { SettingField, SettingValue } from '../../api';
import { slug } from '../../router';
import { settings } from '../../stores/settings';

// Settings → Printer: what the link enables elsewhere (fields tagged "printer") with their current values
const related = computed(() =>
  settings.sections
    .filter(s => s.id !== 'printer')
    .flatMap(s => s.fields.filter(f => f.tag === 'printer').map(field => ({ section: s, field, value: s.values[field.key] ?? null }))),
);

function formatValue(field: SettingField, value: SettingValue) {
  if (field.type === 'toggle') return value ? 'On' : 'Off';
  if (field.type === 'choice') return field.choices?.find(c => c.value === String(value))?.label ?? String(value ?? '');
  return String(value ?? '—');
}
</script>

<template>
  <div v-if="related.length" class="related">
    <div class="subheading">
      <h3>Uses of the printer connection</h3>
      <div class="desc">Settings on other tabs that work with the linked printer, and their current state.</div>
    </div>
    <div class="rows">
      <div v-for="r in related" :key="r.section.id + '.' + r.field.key" class="row">
        <div>
          <div class="label">{{ r.field.label }}</div>
          <div class="sub">{{ r.section.group }} › {{ r.section.title }} · <a :href="'#settings/' + slug(r.section.group)" class="link">change</a></div>
        </div>
        <div class="control"><span class="value">{{ formatValue(r.field, r.value) }}</span></div>
      </div>
    </div>
  </div>
</template>
