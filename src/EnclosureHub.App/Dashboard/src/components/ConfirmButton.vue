<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue';

// Two-step button for destructive actions: the first click arms it ("Confirm"), the second click within
// the timeout runs the action; it disarms by itself otherwise.
const props = withDefaults(
  defineProps<{
    label: string;
    confirmLabel?: string;
    busyLabel?: string;
    timeout?: number;
    action: () => Promise<void>;
  }>(),
  { confirmLabel: 'Confirm', busyLabel: '', timeout: 5000 },
);

const emit = defineEmits<{ armed: [armed: boolean]; error: [message: string] }>();

const armed = ref(false);
const busy = ref(false);
let timer: ReturnType<typeof setTimeout> | undefined;

function disarm() {
  clearTimeout(timer);
  armed.value = false;
  emit('armed', false);
}

async function click() {
  if (!armed.value) {
    armed.value = true;
    emit('armed', true);
    timer = setTimeout(disarm, props.timeout);
    return;
  }
  disarm();
  busy.value = true;
  try {
    await props.action();
  } catch (err) {
    emit('error', (err as Error).message);
  } finally {
    busy.value = false;
  }
}

onBeforeUnmount(() => clearTimeout(timer));
</script>

<template>
  <button class="btn" :class="{ armed }" type="button" :disabled="busy" @click="click">
    {{ busy && busyLabel ? busyLabel : armed ? confirmLabel : label }}
  </button>
</template>
