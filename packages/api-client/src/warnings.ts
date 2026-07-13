// Pure data-shaping for the /avisos screen: group in-force IPMA warnings by
// district, severity-sorted. Ported verbatim from the web app's avisos route
// (apps/web/src/routes/avisos.tsx `groupByDistrict`). The district mapping and
// severity ranking are presentation tokens in @fogos/ui-tokens; this module only
// reshapes. No React / RN imports.

import { districtForArea, warningLevelRank } from '@fogos/ui-tokens'

import type { WeatherWarning } from './types'

/** One district's warnings, its worst severity first for section ordering. */
export interface WarningDistrictGroup {
  district: string
  /** Highest severity rank among the group's warnings (red 3 > orange 2 > yellow 1). */
  maxRank: number
  /** Warnings for the district, highest severity first. */
  warnings: WeatherWarning[]
}

/**
 * Groups warnings by district and severity-sorts: each group's warnings run
 * highest severity first, and the groups themselves run highest severity first,
 * then alphabetically by district (pt collation). Mirrors web's `groupByDistrict`.
 */
export function groupWarningsByDistrict(
  warnings: readonly WeatherWarning[],
): WarningDistrictGroup[] {
  const byDistrict = new Map<string, WeatherWarning[]>()
  for (const w of warnings) {
    const district = districtForArea(w.areaCode)
    const list = byDistrict.get(district)
    if (list) list.push(w)
    else byDistrict.set(district, [w])
  }

  return [...byDistrict.entries()]
    .map(([district, list]) => ({
      district,
      maxRank: Math.max(...list.map((w) => warningLevelRank(w.level))),
      warnings: [...list].sort(
        (a, b) => warningLevelRank(b.level) - warningLevelRank(a.level),
      ),
    }))
    // Highest severity first, then alphabetically by district.
    .sort(
      (a, b) =>
        b.maxRank - a.maxRank || a.district.localeCompare(b.district, 'pt'),
    )
}
