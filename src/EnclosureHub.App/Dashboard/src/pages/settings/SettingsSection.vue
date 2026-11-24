<script setup lang="ts">
import { reactive, ref, watch } from 'vue';
import { isEditable, put, type SettingField, type SettingsApplyResult, type SettingsSection, type SettingValue } from '../../api';
import SettingFieldInput from './SettingFieldInput.vue';

export type FieldNote = { text: string; error?: boolean } | null;

// One settings section (ISettingsSection on the server) rendered generically from its field list.
const props = defineProps<{
  section: SettingsSection;
  hideTitle?: boolean;
  /** Extra text under a field's description, computed from the current (unsaved) values – e.g. storage estimates. */
  fieldNote?: (key: string, values: Record<string, SettingValue>) => FieldNote;
}>();

const emit = defineEmits<{ saved: [section: SettingsSection, result: SettingsApplyResult] }>();

// Edited copy of the values; reset when the section is loaded again
const values = reactive<Record<string, SettingValue>>({ ...props.section.values });
watch(
  () => props.section,
  s => Object.assign(values, s.values),
);

const saving = ref(false);
const note = reactive({ cls: 'note', text: '', restart: false });

const noteFor = (field: SettingField) => (props.fieldNote ? props.fieldNote(field.key, values) : null);

async function save() {
  const body: Record<string, SettingValue> = {};
  for (const field of props.section.fields) if (isEditable(field)) body[field.key] = values[field.key] ?? null;

  saving.value = true;
  Object.assign(note, { cls: 'note', text: 'Saving…', restart: false });
  try {
    const result = await put<SettingsApplyResult>(`/api/settings/${encodeURIComponent(props.section.id)}`, body);
    Object.assign(note, { cls: 'note ok', text: result.message || 'Saved.', restart: result.restartRequired });
    Object.assign(values, result.values);
    emit('saved', props.section, result);
  } catch (err) {
    Object.assign(note, { cls: 'note err', text: (err as Error).message, restart: false });
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <div class="section">
    <h2 v-if="!hideTitle">{{ section.title }}</h2>
    <p v-if="section.description" class="section-desc">{{ section.description }}</p>

    <slot name="before" :values="values" />

    <div class="grid-2">
      <template v-for="field in section.fields" :key="field.key">
        <!-- Sub-heading: spans both columns, the fields below belong to it -->
        <div v-if="field.type === 'heading'" class="subheading">
          <h3>{{ field.label }}</h3>
          <div v-if="field.description" class="desc">{{ field.description }}</div>
        </div>
        <div v-else class="row">
          <div>
            <div class="label">{{ field.label }}</div>
            <div v-if="field.description" class="desc">{{ field.description }}</div>
            <div v-if="noteFor(field)" class="estimate" :class="{ err: noteFor(field)?.error }">{{ noteFor(field)?.text }}</div>
          </div>
          <div class="control">
            <SettingFieldInput :field="field" :model-value="values[field.key] ?? null" @update:model-value="values[field.key] = $event" />
          </div>
        </div>
      </template>
    </div>

    <slot name="after" :values="values" />

    <div class="section-footer">
      <button class="btn" type="button" :disabled="saving" @click="save">Save</button>
      <span :class="note.cls">
        {{ note.text }}
        <a v-if="note.restart" href="#system" class="link">Restart now</a>
      </span>
    </div>
  </div>
</template>
