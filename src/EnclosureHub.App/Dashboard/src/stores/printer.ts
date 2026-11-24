import { reactive } from 'vue';
import { api, type PrinterLogEntry, type PrinterStatus } from '../api';

export const printer = reactive({
  /** Null = module missing or not loaded yet. */
  status: null as PrinterStatus | null,
  log: [] as PrinterLogEntry[],
  logAfter: 0, // highest sequence received
});

export async function fetchPrinter() {
  try {
    printer.status = await api<PrinterStatus>('/api/printer');
  } catch {
    printer.status = null;
  }
  return printer.status;
}

/** Appends new log entries (the server keeps the last 500; so do we). */
export async function loadPrinterLog() {
  let entries: PrinterLogEntry[];
  try {
    entries = await api<PrinterLogEntry[]>('/api/printer/log?after=' + printer.logAfter);
  } catch {
    return;
  }
  if (entries.length === 0) return;
  printer.logAfter = entries[entries.length - 1]!.sequence;
  printer.log.push(...entries);
  if (printer.log.length > 500) printer.log.splice(0, printer.log.length - 500);
}

/** Temperature reports and "busy" keepalives (every 2 s during heating, homing, probing) would bury everything else. */
export const isRoutineLine = (e: PrinterLogEntry) => e.direction === 'in' && /^((ok\s+)?T:|echo:busy:)/.test(e.text.trim());
