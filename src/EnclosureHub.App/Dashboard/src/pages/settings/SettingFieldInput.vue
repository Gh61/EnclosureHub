<script setup lang="ts">
import type { SettingField, SettingValue } from '../../api';
import ToggleButton from '../../components/ToggleButton.vue';

// One input for a SettingField, by type; values travel as bool / number / string (null = empty).
defineProps<{ field: SettingField; modelValue: SettingValue }>();
const emit = defineEmits<{ 'update:modelValue': [value: SettingValue] }>();

const text = (event: Event) => (event.target as HTMLInputElement | HTMLSelectElement).value;
const number = (event: Event) => {
  const v = text(event);
  return v === '' ? null : Number(v);
};
</script>

<template>
  <span v-if="field.type === 'info'" class="value info">{{ modelValue ?? '—' }}</span>
  <ToggleButton v-else-if="field.type === 'toggle'" :model-value="modelValue === true" @update:model-value="emit('update:modelValue', $event)" />
  <select v-else-if="field.type === 'choice'" :value="String(modelValue ?? '')" @change="emit('update:modelValue', text($event))">
    <option v-for="c in field.choices ?? []" :key="c.value" :value="c.value">{{ c.label }}</option>
  </select>
  <input
    v-else-if="field.type === 'number'"
    type="number"
    :min="field.min ?? undefined"
    :max="field.max ?? undefined"
    :step="field.step ?? 'any'"
    :value="modelValue ?? ''"
    @input="emit('update:modelValue', number($event))"
  />
  <input v-else type="text" :value="modelValue ?? ''" @input="emit('update:modelValue', text($event))" />
</template>
