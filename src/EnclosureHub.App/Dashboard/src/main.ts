// Enclosure Hub dashboard – a client of the hub's local HTTP API (/api/*) only; it is also the reference
// implementation for other display clients.
import { createApp } from 'vue';
import App from './App.vue';
import './styles/dashboard.scss';

createApp(App).mount('#app');
