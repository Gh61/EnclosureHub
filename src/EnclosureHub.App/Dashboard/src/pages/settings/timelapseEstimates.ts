import type { SettingValue, TimelapseStatus } from '../../api';
import { formatCount, formatDuration, formatSize } from '../../format';
import { sizeBasis, timelapse, timelapseSizes } from '../../stores/timelapse';
import type { FieldNote } from './SettingsSection.vue';

// Live storage estimates under the Timelapse settings, computed from the values being edited.
// While a recording is rendered, its frames and the new video are on disk together (the peak); afterwards only the video stays.

const num = (values: Record<string, SettingValue>, key: string) => Number(values[key] ?? 0);

export function timelapseFieldNote(key: string, values: Record<string, SettingValue>): FieldNote {
  const s = timelapse.info?.status ?? null;
  const interval = num(values, 'interval');
  const fps = num(values, 'videoFps') || 30;
  if (!(interval > 0)) return null;

  const { frameBytes, videoBytes } = timelapseSizes(s);
  const framesPerHour = 3600 / interval;

  if (key === 'interval') {
    return {
      text:
        `1 hour of recording ≈ ${formatCount(framesPerHour)} frames: ${formatSize(framesPerHour * frameBytes)} of frames ` +
        `+ about ${formatSize(framesPerHour * videoBytes)} for ${formatDuration(framesPerHour / fps)} of video. Based on ${sizeBasis(s)}.`,
    };
  }

  if (key === 'maxHours') {
    const hours = num(values, 'maxHours');
    if (!(hours > 0)) return null;
    const frames = framesPerHour * hours;
    const peak = frames * (frameBytes + videoBytes);
    const free = s?.freeBytes ?? null;
    const tooBig = free != null && peak > free;
    return {
      text:
        `${hours} h ≈ ${formatCount(frames)} frames: ${formatSize(frames * frameBytes)} of frames + about ` +
        `${formatSize(frames * videoBytes)} video (${formatDuration(frames / fps)}) = ${formatSize(peak)} needed while rendering. ` +
        'Afterwards the frames are deleted and only the video stays.' +
        (tooBig ? ` Only ${formatSize(free)} is free – a recording stops early when the disk is nearly full.` : ''),
      error: tooBig,
    };
  }

  return null;
}

/** Text for the "Free disk space" row: how long the free space lasts at the interval being edited. */
export function diskSpaceNote(s: TimelapseStatus | null, interval: number): string {
  if (!s || s.freeBytes == null || !(interval > 0)) return 'Currently, on the disk holding the timelapse data.';
  const { frameBytes, videoBytes } = timelapseSizes(s);
  const hours = s.freeBytes / (((frameBytes + videoBytes) * 3600) / interval);
  return `Currently, on the disk holding the timelapse data. Enough for about ${formatDuration(hours * 3600)} of recording at this interval, including rendering the video.`;
}
