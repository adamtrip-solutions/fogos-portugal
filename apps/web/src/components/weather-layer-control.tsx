import { useState } from 'react'
import { Layers, Pause, Play, X } from 'lucide-react'

import {
  RADAR_CREDIT,
  WEATHER_CONTROL_TITLE,
  WEATHER_CREDIT,
  WEATHER_LAYER_LIST,
  WEATHER_LAYERS,
  WEATHER_OFF_LABEL,
  WIND_PARTICLES_CREDIT,
} from '#/lib/weather/catalog.ts'
import type { WeatherLayerKey } from '#/lib/weather/catalog.ts'
import { FWI_CREDIT, FWI_HELP_TEXT } from '#/lib/weather/effis.ts'
import type { WeatherAvailability } from '#/lib/weather/api.ts'
import type { RadarFrame } from '#/lib/weather/radar.ts'

const CARD_CLASS =
  'rounded-2xl border border-black/5 bg-white/75 shadow-lg backdrop-blur-xl dark:border-white/10 dark:bg-zinc-900/70'
const TITLE_CLASS =
  'text-xs font-semibold uppercase tracking-wider text-muted-foreground'

interface WeatherLayerControlProps {
  value: WeatherLayerKey | 'none'
  onChange: (value: WeatherLayerKey | 'none') => void
  availability: WeatherAvailability | undefined
  radarPlaying: boolean
  onToggleRadarPlaying: () => void
  radarActiveFrame: RadarFrame | undefined
  /**
   * Optional controlled open state so a parent can enforce "one panel open at a
   * time". When omitted the control manages its own open state (fallback).
   */
  open?: boolean
  onOpenChange?: (open: boolean) => void
}

/** Formats a Date as `dd/MM HH:mm` for Lisbon display. */
function formatLisbon(date: Date): string {
  const parts = new Intl.DateTimeFormat('pt-PT', {
    timeZone: 'Europe/Lisbon',
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).formatToParts(date)
  const get = (type: string) =>
    parts.find((part) => part.type === type)?.value ?? ''
  return `${get('day')}/${get('month')} ${get('hour')}:${get('minute')}`
}

/** Formats an ISO `YYYY-MM-DDTHH:MM` (UTC) forecast time for Lisbon display. */
function formatForecast(time: string): string {
  return formatLisbon(new Date(`${time}:00Z`))
}

export function WeatherLayerControl({
  value,
  onChange,
  availability,
  radarPlaying,
  onToggleRadarPlaying,
  radarActiveFrame,
  open: controlledOpen,
  onOpenChange,
}: WeatherLayerControlProps) {
  const [internalOpen, setInternalOpen] = useState(false)
  const open = controlledOpen ?? internalOpen
  const setOpen = onOpenChange ?? setInternalOpen

  const aromeDisabled = !availability?.referenceTime
  const activeDef = value === 'none' ? null : WEATHER_LAYERS[value]

  if (!open) {
    return (
      <button
        type="button"
        aria-label={WEATHER_CONTROL_TITLE}
        onClick={() => setOpen(true)}
        className={`${CARD_CLASS} relative flex size-10 items-center justify-center text-zinc-700 transition-colors hover:bg-white/90 dark:text-zinc-200 dark:hover:bg-zinc-900/90`}
      >
        <Layers className="size-[18px]" />
        {value !== 'none' && (
          <span
            aria-hidden
            className="absolute right-1.5 top-1.5 size-2 rounded-full bg-orange-500 ring-2 ring-white dark:ring-zinc-900"
          />
        )}
      </button>
    )
  }

  return (
    <div className={`${CARD_CLASS} w-60 p-3`}>
      <div className="flex items-center justify-between">
        <h2 className={TITLE_CLASS}>{WEATHER_CONTROL_TITLE}</h2>
        <button
          type="button"
          aria-label="Fechar"
          onClick={() => setOpen(false)}
          className="-m-1 flex size-6 items-center justify-center rounded-full text-muted-foreground transition-colors hover:bg-black/5 hover:text-foreground dark:hover:bg-white/10"
        >
          <X className="size-4" />
        </button>
      </div>

      <div role="radiogroup" aria-label={WEATHER_CONTROL_TITLE} className="mt-2 space-y-0.5">
        <WeatherOption
          label={WEATHER_OFF_LABEL}
          checked={value === 'none'}
          onSelect={() => onChange('none')}
        />
        {WEATHER_LAYER_LIST.map((def) => {
          // Radar is client-side and Vento now has particles, so both work with
          // no AROME run; the other AROME layers keep their disabled logic.
          const disabled =
            def.kind === 'wms' &&
            def.timeBased &&
            def.key !== 'wind' &&
            aromeDisabled
          return (
            <WeatherOption
              key={def.key}
              label={def.label}
              checked={value === def.key}
              disabled={disabled}
              onSelect={() => onChange(def.key)}
            />
          )
        })}
      </div>

      {activeDef?.kind === 'radar' && (
        <div className="mt-3 flex items-center gap-2 border-t border-black/5 pt-3 dark:border-white/10">
          <button
            type="button"
            aria-label={radarPlaying ? 'Pausar' : 'Reproduzir'}
            onClick={onToggleRadarPlaying}
            className="flex size-7 shrink-0 items-center justify-center rounded-full border border-black/10 text-zinc-700 transition-colors hover:bg-black/5 dark:border-white/15 dark:text-zinc-200 dark:hover:bg-white/10"
          >
            {radarPlaying ? (
              <Pause className="size-3.5" />
            ) : (
              <Play className="size-3.5 translate-x-px" />
            )}
          </button>
          <div className="min-w-0">
            {radarActiveFrame && (
              <p className="text-[11px] font-medium tabular-nums text-foreground">
                {formatLisbon(new Date(radarActiveFrame.time * 1000))}
                {radarActiveFrame.nowcast && ' · previsão'}
              </p>
            )}
            <p className="text-[11px] text-muted-foreground">{RADAR_CREDIT}</p>
          </div>
        </div>
      )}

      {activeDef?.kind === 'effis' && (
        <div className="mt-3 space-y-2 border-t border-black/5 pt-3 dark:border-white/10">
          <p className="text-[11px] leading-relaxed text-muted-foreground">
            {FWI_HELP_TEXT}
          </p>
          <p className="text-[11px] text-muted-foreground">{FWI_CREDIT}</p>
        </div>
      )}

      {activeDef?.kind === 'wms' && (
        <div className="mt-3 space-y-2 border-t border-black/5 pt-3 dark:border-white/10">
          <div className="overflow-y-auto rounded-lg bg-white p-1">
            <img
              src={activeDef.legendUrl}
              alt={`Legenda: ${activeDef.label}`}
              className="mx-auto max-h-40 w-auto object-contain"
            />
          </div>
          {activeDef.timeBased && availability?.time && (
            <p className="text-[11px] font-medium text-muted-foreground">
              Previsão AROME · {formatForecast(availability.time)}
            </p>
          )}
          <p className="text-[11px] text-muted-foreground">{WEATHER_CREDIT}</p>
          {activeDef.key === 'wind' && (
            <p className="text-[11px] text-muted-foreground">
              {WIND_PARTICLES_CREDIT}
            </p>
          )}
        </div>
      )}
    </div>
  )
}

interface WeatherOptionProps {
  label: string
  checked: boolean
  disabled?: boolean
  onSelect: () => void
}

function WeatherOption({
  label,
  checked,
  disabled = false,
  onSelect,
}: WeatherOptionProps) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={checked}
      disabled={disabled}
      onClick={onSelect}
      className={`flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-left text-xs transition-colors ${
        disabled
          ? 'cursor-not-allowed opacity-40'
          : 'hover:bg-black/5 dark:hover:bg-white/10'
      }`}
    >
      <span
        aria-hidden
        className={`flex size-3.5 shrink-0 items-center justify-center rounded-full border ${
          checked
            ? 'border-orange-500'
            : 'border-black/25 dark:border-white/30'
        }`}
      >
        {checked && <span className="size-1.5 rounded-full bg-orange-500" />}
      </span>
      <span className="text-foreground">{label}</span>
    </button>
  )
}
