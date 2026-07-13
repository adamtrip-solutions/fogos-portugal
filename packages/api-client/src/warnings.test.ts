import { describe, expect, it } from 'vitest'

import { groupWarningsByDistrict } from './warnings'
import type { WeatherWarning } from './types'

function warning(over: Partial<WeatherWarning>): WeatherWarning {
  return {
    id: Math.random().toString(36),
    areaCode: 'LSB',
    awarenessType: 'Tempo quente',
    level: 'yellow',
    levelPt: 'Amarelo',
    startsAt: '2025-07-04T00:00:00Z',
    endsAt: '2025-07-04T18:00:00Z',
    text: null,
    ...over,
  }
}

describe('groupWarningsByDistrict', () => {
  it('maps area codes to district names, incl. Madeira and Açores', () => {
    const groups = groupWarningsByDistrict([
      warning({ areaCode: 'FAR' }),
      warning({ areaCode: 'PSA' }), // Madeira
      warning({ areaCode: 'ACE' }), // Açores
    ])
    expect(groups.map((g) => g.district).sort()).toEqual([
      'Açores',
      'Faro',
      'Madeira',
    ])
  })

  it('collapses multiple area codes into one district group', () => {
    const groups = groupWarningsByDistrict([
      warning({ areaCode: 'MCN' }),
      warning({ areaCode: 'MMT' }),
    ])
    expect(groups).toHaveLength(1)
    expect(groups[0].district).toBe('Madeira')
    expect(groups[0].warnings).toHaveLength(2)
  })

  it('sorts groups by worst severity, then alphabetically', () => {
    const groups = groupWarningsByDistrict([
      warning({ areaCode: 'FAR', level: 'yellow' }), // Faro, rank 1
      warning({ areaCode: 'PTO', level: 'red' }), // Porto, rank 3
      warning({ areaCode: 'AVR', level: 'orange' }), // Aveiro, rank 2
      warning({ areaCode: 'BJA', level: 'orange' }), // Beja, rank 2
    ])
    expect(groups.map((g) => g.district)).toEqual([
      'Porto', // red
      'Aveiro', // orange, before Beja alphabetically
      'Beja', // orange
      'Faro', // yellow
    ])
    expect(groups[0].maxRank).toBe(3)
  })

  it('sorts warnings within a district by severity, highest first', () => {
    const [group] = groupWarningsByDistrict([
      warning({ areaCode: 'LSB', level: 'yellow' }),
      warning({ areaCode: 'LSB', level: 'red' }),
      warning({ areaCode: 'LSB', level: 'orange' }),
    ])
    expect(group.warnings.map((w) => w.level)).toEqual(['red', 'orange', 'yellow'])
    expect(group.maxRank).toBe(3)
  })

  it('is empty for no warnings', () => {
    expect(groupWarningsByDistrict([])).toEqual([])
  })
})
