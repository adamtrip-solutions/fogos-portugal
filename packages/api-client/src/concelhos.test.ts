import { describe, expect, it } from 'vitest'

import {
  CONCELHOS,
  concelhoByDico,
  concelhoByName,
  foldText,
  searchConcelhos,
} from './concelhos'

describe('foldText', () => {
  it('strips accents, lowercases and trims (unaccented input matches accented)', () => {
    expect(foldText('Évora')).toBe('evora')
    expect(foldText('  Bragança ')).toBe('braganca')
    expect(foldText('Santarém')).toBe('santarem')
    expect(foldText('Setúbal')).toBe('setubal')
  })
})

describe('searchConcelhos', () => {
  it('matches accent-insensitively (unaccented query finds accented name)', () => {
    const byName = searchConcelhos('agueda')
    expect(byName.map((c) => c.dico)).toContain('0101') // Águeda
    const byDistrict = searchConcelhos('evora')
    expect(byDistrict.length).toBeGreaterThan(0)
    expect(byDistrict.every((c) => c.district === 'Évora')).toBe(true)
  })

  it('requires every whitespace term to match (partial, multi-term)', () => {
    const results = searchConcelhos('vila nova')
    expect(results.length).toBeGreaterThan(1)
    expect(
      results.every((c) => {
        const hay = `${c.name} ${c.district}`.toLowerCase()
        return hay.includes('vila') && hay.includes('nova')
      }),
    ).toBe(true)
  })

  it('does a partial substring match on a single term', () => {
    const results = searchConcelhos('coimb')
    expect(results.some((c) => c.name === 'Coimbra')).toBe(true)
  })

  it('caps results at the limit', () => {
    expect(searchConcelhos('a', 5)).toHaveLength(5)
  })

  it('returns the first N concelhos for an empty query', () => {
    expect(searchConcelhos('   ', 3)).toEqual(CONCELHOS.slice(0, 3))
  })

  it('returns nothing for a non-matching query', () => {
    expect(searchConcelhos('zzzzz')).toEqual([])
  })
})

describe('concelhoByDico', () => {
  it('resolves an exact zero-padded DICO', () => {
    expect(concelhoByDico('1106')?.name).toBe('Lisboa')
  })

  it('returns null for an unknown DICO', () => {
    expect(concelhoByDico('9999')).toBeNull()
  })
})

describe('concelhoByName', () => {
  it('resolves accent- and case-insensitively', () => {
    expect(concelhoByName('agueda')?.dico).toBe('0101')
    expect(concelhoByName('ÉVORA')?.dico).toBe('0705')
  })

  it('returns null for a name outside the mainland set', () => {
    expect(concelhoByName('Funchal')).toBeNull()
  })
})
