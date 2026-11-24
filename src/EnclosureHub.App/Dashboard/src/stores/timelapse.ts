import { reactive } from 'vue';
import { api, ApiError, type TimelapseInfo, type TimelapseStatus, type TimelapseVideo } from '../api';
import { formatSize } from '../format';

export const timelapse = reactive({
  info: null as TimelapseInfo | null,
  /** False when /api/timelapse answers 404 (module turned off) – the page is then not listed. */
  available: true,
  videos: [] as TimelapseVideo[],
  videosKey: '',
});

export async function fetchTimelapse() {
  try {
    timelapse.info = await api<TimelapseInfo>('/api/timelapse');
    timelapse.available = true;
  } catch (err) {
    timelapse.info = null;
    timelapse.available = !(err instanceof ApiError && err.status === 404);
  }
  return timelapse.info;
}

/** Changes when a render finishes or fails – the video list is reloaded then. */
export const pendingKey = () => timelapse.info?.status.pending.map(p => p.name + ':' + p.state).join('|') ?? '';

export async function loadVideos() {
  const videos = await api<TimelapseVideo[]>('/api/timelapse/videos');
  const key = videos.map(v => v.name + ':' + v.sizeBytes).join('|');
  if (key === timelapse.videosKey) return;
  timelapse.videosKey = key;
  timelapse.videos = videos;
}

// ---------------------------------------------------------------- storage estimates

/** Estimate basis while the camera delivers no frame. */
const AssumedFrameBytes = 300 * 1024;

/** Bytes per saved frame and per frame of rendered video (same 15 % fallback as the server's disk guard). */
export function timelapseSizes(s: TimelapseStatus | null | undefined) {
  const frameBytes = s?.averageFrameBytes || AssumedFrameBytes;
  return { frameBytes, videoBytes: s?.videoBytesPerFrame || frameBytes * 0.15 };
}

export function sizeBasis(s: TimelapseStatus | null | undefined) {
  const { frameBytes } = timelapseSizes(s);
  return (
    (s?.averageFrameBytes
      ? `${formatSize(frameBytes)} per frame (${s.current?.frames ? 'average of this recording' : 'current camera picture'})`
      : `an assumed ${formatSize(frameBytes)} per frame – turn the camera on for a real figure`) +
    (s?.videoBytesPerFrame ? ', video size measured on your videos' : ', video assumed at 15 % of the frames')
  );
}

/** Hours of recording the free space allows at the given interval, frames + rendered video included. */
export function hoursOfSpace(s: TimelapseStatus | null | undefined, intervalSeconds: number): number | null {
  if (!s || s.freeBytes == null || !(intervalSeconds > 0)) return null;
  const { frameBytes, videoBytes } = timelapseSizes(s);
  return s.freeBytes / (((frameBytes + videoBytes) * 3600) / intervalSeconds);
}
