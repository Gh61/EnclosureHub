import { computed, reactive } from 'vue';
import { api, enc, post, type Entity, type SystemInfo } from '../api';

// Entities from /api/entities, kept current by the SSE stream (/api/events).
export const entities = reactive({
  byId: {} as Record<string, Entity>,
  order: [] as string[], // ids in registry order
});

export const connection = reactive({ state: '' as '' | 'on' | 'err', text: 'Connecting…' });

export const device = reactive({ name: '' });

export const entityList = computed(() => entities.order.map(id => entities.byId[id]).filter((e): e is Entity => !!e));

export function setEntity(e: Entity) {
  if (!entities.byId[e.id]) entities.order.push(e.id);
  entities.byId[e.id] = e;
}

export async function loadEntities() {
  try {
    const all = await api<Entity[]>('/api/entities');
    entities.order = all.map(e => e.id);
    for (const e of all) entities.byId[e.id] = e;
  } catch {
    connection.state = 'err';
    connection.text = 'API unavailable';
  }
}

/** Re-reads one entity (for state that does not raise change events: camera frame age, live sensor samples). */
export async function refreshEntity(id: string) {
  try {
    setEntity(await api<Entity>(`/api/entities/${enc(id)}`));
  } catch {
    // transient; the next poll tries again
  }
}

export const sendOnOff = (id: string, on: boolean) =>
  post<Entity>(`/api/entities/${enc(id)}/command`, { on }).catch((err: unknown) => console.error(err));

export function connectEvents() {
  const events = new EventSource('/api/events');
  events.onopen = () => {
    connection.state = 'on';
    connection.text = 'Connected';
  };
  events.onerror = () => {
    connection.state = 'err';
    connection.text = 'Reconnecting…';
  };
  events.addEventListener('state', ev => setEntity(JSON.parse((ev as MessageEvent<string>).data) as Entity));
}

export async function loadDevice() {
  try {
    const info = await api<SystemInfo>('/api/system');
    device.name = info.device;
    document.title = `${info.device} · Enclosure Hub`;
  } catch {
    // shown as empty brand line
  }
}
