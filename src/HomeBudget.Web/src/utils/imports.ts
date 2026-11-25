import type { Schemas } from '@/api/client'

export type ImportLine = Schemas['ImportPreviewLineDto']
export type ImportLineStatus = Schemas['ImportLineStatus']

/** Taille maximale d'un relevé, identique à celle de l'API (5 Mio). */
export const maxImportBytes = 5 * 1024 * 1024

export const importStatuses: Record<ImportLineStatus, { title: string; color: string }> = {
  New: { title: 'Nouvelle', color: 'primary' },
  Match: { title: 'Échéance', color: 'success' },
  ProbableDuplicate: { title: 'Doublon probable', color: 'warning' },
  Duplicate: { title: 'Déjà importée', color: 'grey' },
}

/** Les doublons certains ne sont jamais importés. */
export const isSelectable = (line: ImportLine) => line.status !== 'Duplicate'

/** Cochées par défaut : les opérations nouvelles et celles qui remplacent une échéance. */
export function defaultSelection(lines: ImportLine[]): number[] {
  return lines.filter((l) => l.status === 'New' || l.status === 'Match').map((l) => l.line)
}

/** Un CSV ou un TSV se lit avec un profil ; l'OFX et le QIF se lisent seuls. */
export function needsProfile(fileName: string): boolean {
  return !/\.(ofx|qif)$/i.test(fileName)
}

/** Contenu binaire → Base64, par blocs pour ne pas dépasser la pile d'appels. */
export function toBase64(bytes: Uint8Array): string {
  let binary = ''
  const chunk = 0x8000
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunk))
  }
  return btoa(binary)
}

export const delimiters = [
  { value: ';', title: 'Point-virgule (;)' },
  { value: ',', title: 'Virgule (,)' },
  { value: '\t', title: 'Tabulation (TSV)' },
  { value: '|', title: 'Barre verticale (|)' },
]

export const dateFormats = [
  { value: 'dd/MM/yyyy', title: 'JJ/MM/AAAA' },
  { value: 'dd/MM/yy', title: 'JJ/MM/AA' },
  { value: 'yyyy-MM-dd', title: 'AAAA-MM-JJ' },
  { value: 'dd-MM-yyyy', title: 'JJ-MM-AAAA' },
  { value: 'dd.MM.yyyy', title: 'JJ.MM.AAAA' },
  { value: 'MM/dd/yyyy', title: 'MM/JJ/AAAA (américain)' },
]

export const decimalSeparators = [
  { value: ',', title: 'Virgule (1 234,56)' },
  { value: '.', title: 'Point (1,234.56)' },
]

/** Résumé des lignes cochées : nombre, dont échéances remplacées, et somme. */
export function selectionSummary(lines: ImportLine[], selected: number[], acceptMatch: Set<number>) {
  const chosen = new Set(selected)
  const kept = lines.filter((l) => chosen.has(l.line) && isSelectable(l))
  return {
    count: kept.length,
    matched: kept.filter((l) => l.status === 'Match' && acceptMatch.has(l.line)).length,
    total: Math.round(kept.reduce((sum, l) => sum + l.amount * 100, 0)) / 100,
  }
}
