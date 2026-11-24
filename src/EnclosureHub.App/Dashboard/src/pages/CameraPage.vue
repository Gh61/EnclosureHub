<script setup lang="ts">
import { computed } from 'vue';
import type { CameraEntity } from '../api';
import { entityList } from '../stores/entities';
import CameraCard from '../components/CameraCard.vue';

const cameras = computed(() => entityList.value.filter((e): e is CameraEntity => e.kind === 'camera'));
</script>

<template>
  <section class="page">
    <h1>Camera</h1>
    <div v-if="cameras.length === 0" class="empty">
      No camera module is loaded. Turn it on under <a href="#settings/camera" class="link">Settings</a> and restart the service.
    </div>
    <CameraCard v-for="camera in cameras" :key="camera.id" :camera="camera" />
  </section>
</template>
