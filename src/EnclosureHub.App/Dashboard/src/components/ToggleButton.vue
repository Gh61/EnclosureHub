<script setup lang="ts">
// On/off switch in the outlined style: a button with role="switch"; the green dot is the only accent.
defineProps<{ modelValue: boolean; title?: string; disabled?: boolean }>();
const emit = defineEmits<{ 'update:modelValue': [value: boolean] }>();
</script>

<template>
  <button
    class="btn toggle"
    type="button"
    role="switch"
    :aria-checked="modelValue ? 'true' : 'false'"
    :title="title"
    :disabled="disabled"
    @click="emit('update:modelValue', !modelValue)"
  >
    {{ modelValue ? 'On' : 'Off' }}
  </button>
</template>
