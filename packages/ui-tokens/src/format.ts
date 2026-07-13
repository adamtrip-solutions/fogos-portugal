// Display formatters (pt-PT). No React / RN imports.

// Hermes ships without Intl.RelativeTimeFormat (unlike the browsers running the
// web app), so hand-rolled pt-PT strings instead of the Intl formatter.
const DIVISIONS: Array<{ amount: number; singular: string; plural: string }> = [
  { amount: 60, singular: 'minuto', plural: 'minutos' },
  { amount: 24, singular: 'hora', plural: 'horas' },
  { amount: 30, singular: 'dia', plural: 'dias' },
  { amount: 12, singular: 'mês', plural: 'meses' },
  { amount: Number.POSITIVE_INFINITY, singular: 'ano', plural: 'anos' },
]

/** e.g. "há 2 horas". */
export function formatRelative(iso: string): string {
  const deltaSeconds = (Date.parse(iso) - Date.now()) / 1000
  if (Math.abs(deltaSeconds) < 60) return 'agora mesmo'

  let duration = Math.abs(deltaSeconds) / 60
  for (const division of DIVISIONS) {
    if (duration < division.amount || division.amount === Number.POSITIVE_INFINITY) {
      const count = Math.round(duration)
      const noun = count === 1 ? division.singular : division.plural
      return deltaSeconds < 0 ? `há ${count} ${noun}` : `dentro de ${count} ${noun}`
    }
    duration /= division.amount
  }
  return 'agora mesmo' // unreachable — the last division catches everything
}

/** Join freguesia · concelho · district, skipping empties and repeats. */
export function locationParts(
  freguesia: string | null,
  concelho: string | null,
  district: string | null,
): string {
  const parts: string[] = []
  for (const raw of [freguesia, concelho, district]) {
    const value = raw?.trim()
    if (!value) continue
    if (parts.some((p) => p.toLowerCase() === value.toLowerCase())) continue
    parts.push(value)
  }
  return parts.join(' · ')
}

/** Resource counts use -1 as an "unknown" sentinel; treat <= 0 as absent. */
export function hasResource(value: number): boolean {
  return value > 0
}

export function incidentTitle(item: { natureza: string | null }): string {
  return item.natureza?.trim() || 'Incêndio'
}

// ── Detail-panel formatters (ported verbatim from apps/web/src/lib/fogos/format.ts) ──
// Intl.DateTimeFormat / NumberFormat are Hermes-safe (only RelativeTimeFormat is
// missing, which formatRelative above already hand-rolls).

const timeFmt = new Intl.DateTimeFormat('pt-PT', {
  hour: '2-digit',
  minute: '2-digit',
  timeZone: 'Europe/Lisbon',
})
const dayMonthFmt = new Intl.DateTimeFormat('pt-PT', {
  day: 'numeric',
  month: 'short',
  timeZone: 'Europe/Lisbon',
})

/** e.g. "03:50 · 4 jul.". */
export function formatTimelineStamp(iso: string): string {
  const d = new Date(iso)
  return `${timeFmt.format(d)} · ${dayMonthFmt.format(d)}`
}

/** Clock time, e.g. "09:12" — the situation-report "Atualizado às …" stamp. */
export function formatClock(iso: string): string {
  return timeFmt.format(new Date(iso))
}

/** Day + short month, e.g. "4 jul." — the situation-report archive stamp. */
export function formatDayMonth(iso: string): string {
  return dayMonthFmt.format(new Date(iso))
}

/** `morning` → "Manhã", anything else → "Noite" (mirrors the worker's slot). */
export function situationSlotLabel(slot: string): string {
  return slot === 'morning' ? 'Manhã' : 'Noite'
}

const hectaresFmt = new Intl.NumberFormat('pt-PT', {
  maximumFractionDigits: 1,
})

/** e.g. "11 985,7 ha". */
export function formatHectares(value: number): string {
  return `${hectaresFmt.format(value)} ha`
}

/**
 * Human duration in European Portuguese, e.g. "1 h 23 min", "45 min", "2 h".
 * Sub-minute durations collapse to "< 1 min". Negative inputs clamp to 0.
 */
export function formatDuration(seconds: number): string {
  const total = Math.max(0, Math.round(seconds))
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  if (h > 0 && m > 0) return `${h} h ${m} min`
  if (h > 0) return `${h} h`
  if (m > 0) return `${m} min`
  return '< 1 min'
}

// ── Season statistics display formatters ─────────────────────────────────────
// Ported verbatim from apps/web/src/lib/fogos/stats.ts (the display half — the
// data-shaping transforms live in @fogos/api-client). Intl.NumberFormat /
// DateTimeFormat are Hermes-safe.

const integerFmt = new Intl.NumberFormat('pt-PT', { maximumFractionDigits: 0 })

/** e.g. "1 234". */
export function formatInteger(value: number): string {
  return integerFmt.format(value)
}

const percentFmt = new Intl.NumberFormat('pt-PT', {
  style: 'percent',
  maximumFractionDigits: 1,
})

/** e.g. "12,5 %". */
export function formatPercent(fraction: number): string {
  return percentFmt.format(fraction)
}

/** e.g. "+25 %" / "−12 %" / "—" for a signed YoY ratio (rounded to whole %). */
export function formatSignedPercent(ratio: number | null): string {
  if (ratio == null || !Number.isFinite(ratio)) return '—'
  const pct = Math.round(ratio * 100)
  const sign = pct > 0 ? '+' : pct < 0 ? '−' : ''
  return `${sign}${Math.abs(pct)} %`
}

/** e.g. "+3" / "−2" / "0" — a signed integer with a real minus sign (delta chips). */
export function formatSignedInteger(value: number): string {
  const sign = value > 0 ? '+' : value < 0 ? '−' : ''
  return `${sign}${integerFmt.format(Math.abs(value))}`
}

const monthDayFmt = new Intl.DateTimeFormat('pt-PT', {
  day: 'numeric',
  month: 'short',
})

/**
 * Formats a day-of-year index (1-based) back into a `d mmm` tick label. Uses a
 * fixed non-leap reference year so ticks read the same across the YoY overlay.
 */
export function dayOfYearLabel(day: number): string {
  const ms = Date.UTC(2025, 0, 1) + (day - 1) * 86_400_000
  return monthDayFmt.format(new Date(ms))
}

/** 24-hour label like "09h" for the hourly histogram. */
export function hourLabel(hour: number): string {
  return `${String(hour).padStart(2, '0')}h`
}

/** PT labels for the critical-conditions machine keys (WP1 signals). */
export const CRITICAL_REASON_LABELS: Record<string, string> = {
  TEMP_ABOVE_30: 'Temperatura > 30 °C',
  HUMIDITY_BELOW_30: 'Humidade < 30%',
  WIND_ABOVE_30: 'Vento > 30 km/h',
  RISK_MAXIMUM: 'Risco máximo',
  HEAT_WAVE: 'Onda de calor',
}

/** Falls back to the raw key when a reason is not in the catalog. */
export function criticalReasonLabel(key: string): string {
  return CRITICAL_REASON_LABELS[key] ?? key
}
