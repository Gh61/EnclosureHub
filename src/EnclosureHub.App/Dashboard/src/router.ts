import { reactive } from 'vue';

// Hash routing: "#settings/timelapse" = page + optional tab. No router library – six pages are not worth one.
export const pages = ['camera', 'timelapse', 'printer', 'status', 'settings', 'system'] as const;
export type Page = (typeof pages)[number];

export const route = reactive({ page: 'camera' as Page, tab: '' });

function parse() {
  const [requested = '', tab = ''] = (location.hash || '#camera').slice(1).split('/');
  route.page = (pages as readonly string[]).includes(requested) ? (requested as Page) : 'camera';
  route.tab = tab;
}

window.addEventListener('hashchange', parse);
parse();

export const slug = (text: string) => text.toLowerCase().replace(/[^a-z0-9]+/g, '-');
