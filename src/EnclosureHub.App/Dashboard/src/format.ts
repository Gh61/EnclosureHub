// Formatting helpers shared by the pages.

export const formatCount = (n: number) => Math.round(n).toLocaleString('en-US');

export const formatSize = (b: number | null | undefined) =>
  b == null ? '—'
    : b >= 1073741824 ? `${(b / 1073741824).toFixed(1)} GB`
    : b >= 1048576 ? `${Math.round(b / 1048576)} MB`
    : `${Math.max(1, Math.round(b / 1024))} kB`;

export const formatDuration = (seconds: number) => {
  const s = Math.round(seconds);
  if (s < 60) return `${s} s`;
  if (s < 3600) return s % 60 ? `${Math.floor(s / 60)} min ${s % 60} s` : `${s / 60} min`;
  if (s >= 172800) return `${formatCount(s / 86400)} days`;
  return `${Math.floor(s / 3600)} h ${String(Math.floor((s % 3600) / 60)).padStart(2, '0')} min`;
};

export const formatUptime = (seconds: number) => {
  const d = Math.floor(seconds / 86400);
  const h = Math.floor((seconds % 86400) / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  return d ? `${d} d ${h} h` : h ? `${h} h ${m} min` : `${m} min`;
};

export const formatMegabytes = (b: number) => `${(b / 1048576).toFixed(0)} MB`;

export const formatTemp = (value: number | null, target: number | null) =>
  value == null ? '—' : `${value.toFixed(1)} °C` + (target ? ` / ${Math.round(target)} °C` : '');

/** Local time with milliseconds, for the printer log. */
export const formatTime = (utc: string) => {
  const d = new Date(utc);
  return d.toLocaleTimeString([], { hour12: false }) + '.' + String(d.getMilliseconds()).padStart(3, '0');
};

export const formatDateTime = (utc: string) => new Date(utc).toLocaleString();

export const secondsSince = (utc: string) => (Date.now() - Date.parse(utc)) / 1000;
