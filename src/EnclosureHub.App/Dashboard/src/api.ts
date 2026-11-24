// Typed client for the hub's HTTP API. JSON is camelCase, enums are camelCase strings (ApiJson.Options).

export class ApiError extends Error {
  constructor(public readonly status: number, message: string) {
    super(message);
    this.name = 'ApiError';
  }
}

export async function api<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, { headers: { 'Content-Type': 'application/json' }, ...init });
  const text = await response.text();
  let data: unknown = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = { error: text };
  }
  if (!response.ok) {
    const message = (data as { error?: string } | null)?.error || `${response.status} ${response.statusText}`;
    throw new ApiError(response.status, message);
  }
  return data as T;
}

export const post = <T>(url: string, body?: unknown) =>
  api<T>(url, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) });
export const put = <T>(url: string, body: unknown) => api<T>(url, { method: 'PUT', body: JSON.stringify(body) });
export const del = <T>(url: string) => api<T>(url, { method: 'DELETE' });

export const enc = encodeURIComponent;

// ---------------------------------------------------------------- entities

export type EntityKind = 'camera' | 'sensor' | 'binarySensor' | 'switch' | 'light' | 'fan';

export interface CameraState {
  available: boolean;
  hasFrame: boolean;
  lastFrameUtc: string | null;
  frameAgeSeconds: number | null;
  /** Time between the last two frames the camera captured (ms). */
  frameIntervalMs: number | null;
  /** Frames per second the camera really captures, measured over the last few frames. */
  capturedFps: number | null;
}

export interface SensorState {
  value: number | null;
  unit: string | null;
  /** Instantaneous 1 s reading behind the (slower) value; only sensors that have one. */
  live?: number | null;
}

export interface OnOffState {
  isOn: boolean | null;
}

export interface LightState extends OnOffState {
  brightness?: number | null;
}

export interface FanState extends OnOffState {
  percentage?: number | null;
}

interface EntityBase {
  id: string;
  name: string;
  icon: string | null;
  isDiagnostic: boolean;
  lastChangedUtc: string;
}

export type Entity = EntityBase &
  (
    | { kind: 'camera'; state: CameraState }
    | { kind: 'sensor'; state: SensorState }
    | { kind: 'binarySensor' | 'switch'; state: OnOffState }
    | { kind: 'light'; state: LightState }
    | { kind: 'fan'; state: FanState }
  );

export type CameraEntity = Extract<Entity, { kind: 'camera' }>;
export type SensorEntity = Extract<Entity, { kind: 'sensor' }>;

// ---------------------------------------------------------------- timelapse

export type TimelapseTrigger = 'interval' | 'layers' | 'layersAndInterval';
export type FfmpegInstallState = 'idle' | 'running' | 'succeeded' | 'failed';

export interface FfmpegStatus {
  available: boolean;
  version: string | null;
  canInstall: boolean;
  installState: FfmpegInstallState;
  installMessage: string | null;
}

export interface TimelapseRecording {
  name: string;
  startedUtc: string;
  frames: number;
  bytes: number;
  lastFrameUtc: string | null;
}

export interface TimelapsePending {
  name: string;
  state: 'queued' | 'rendering' | 'failed';
  frames: number;
  bytes: number;
  progress: number | null;
  error: string | null;
}

export interface TimelapseStatus {
  recording: boolean;
  current: TimelapseRecording | null;
  pending: TimelapsePending[];
  cameraAvailable: boolean;
  intervalSeconds: number;
  maxDurationHours: number;
  videoFps: number;
  averageFrameBytes: number | null;
  videoBytesPerFrame: number | null;
  freeBytes: number | null;
  ffmpeg: FfmpegStatus;
  trigger: TimelapseTrigger;
  lastLayer: number | null;
}

export interface TimelapseInfo {
  switchId: string;
  status: TimelapseStatus;
}

export interface TimelapseVideo {
  name: string;
  sizeBytes: number;
  createdUtc: string;
  startedUtc: string | null;
  stoppedUtc: string | null;
  frames: number | null;
  fps: number | null;
  durationSeconds: number | null;
  hasThumbnail: boolean;
}

// ---------------------------------------------------------------- printer

export interface PrinterInfo {
  machineType: string | null;
  firmware: string | null;
  uuid: string | null;
  capabilities: string[];
}

export interface PrinterStatus {
  linked: boolean;
  connected: boolean;
  port: string | null;
  error: string | null;
  info: PrinterInfo | null;
  printing: boolean;
  file: string | null;
  progress: number | null;
  layer: number | null;
  z: number | null;
  nozzleTemperature: number | null;
  nozzleTarget: number | null;
  bedTemperature: number | null;
  bedTarget: number | null;
  lastReceivedUtc: string | null;
}

export type PrinterLogDirection = 'in' | 'out' | 'info';

export interface PrinterLogEntry {
  sequence: number;
  utc: string;
  direction: PrinterLogDirection;
  text: string;
}

// ---------------------------------------------------------------- settings

export type SettingFieldType = 'toggle' | 'number' | 'text' | 'choice' | 'heading' | 'info';
export type SettingValue = string | number | boolean | null;

export interface SettingChoice {
  value: string;
  label: string;
}

export interface SettingField {
  key: string;
  label: string;
  type: SettingFieldType;
  description: string | null;
  choices: SettingChoice[] | null;
  min: number | null;
  max: number | null;
  step: number | null;
  /** Groups related fields across sections, e.g. "printer" for everything the printer link enables. */
  tag: string | null;
}

export interface SettingsSection {
  id: string;
  title: string;
  description: string | null;
  /** Tab on the Settings page. */
  group: string;
  fields: SettingField[];
  values: Record<string, SettingValue>;
}

export interface SettingsApplyResult {
  restartRequired: boolean;
  message: string | null;
  values: Record<string, SettingValue>;
}

/** Editable fields only (headings and read-only info are never sent back). */
export const isEditable = (field: SettingField) => field.type !== 'heading' && field.type !== 'info';

// ---------------------------------------------------------------- system

export interface SystemInfo {
  device: string;
  deviceId: string;
  version: string;
  hostname: string;
  os: string;
  architecture: string;
  runtime: string;
  startedUtc: string;
  uptimeSeconds: number;
  workingSetBytes: number;
  managedMemoryBytes: number;
  processorCount: number;
  underSystemd: boolean;
}
