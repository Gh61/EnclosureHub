import { defineConfig, loadEnv } from 'vite';
import vue from '@vitejs/plugin-vue';

// Builds into the App's wwwroot (served by Kestrel). `npm run dev` serves the sources with hot reload and
// proxies /api and /health to a running hub – set VITE_PROXY=http://<pi>:8080 in Dashboard/.env.local
// to develop against the real device.
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', 'VITE_');
  const proxyTarget = env.VITE_PROXY || 'http://localhost:8080';

  return {
    plugins: [vue()],
    build: {
      outDir: '../wwwroot',
      emptyOutDir: true,
      // One request each for the script and the style; the Pi serves it with Cache-Control: no-cache anyway
      rollupOptions: { output: { manualChunks: undefined } },
    },
    server: {
      proxy: {
        '/api': { target: proxyTarget, changeOrigin: true },
        '/health': { target: proxyTarget, changeOrigin: true },
      },
    },
  };
});
