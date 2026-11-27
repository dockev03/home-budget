import { describe, expect, it } from 'vitest'
import {
  attachmentContentUrl,
  attachmentCountLabel,
  attachmentTooLarge,
  attachmentUploadUrl,
  formatFileSize,
  isPreviewable,
  MAX_ATTACHMENT_BYTES,
} from './attachments'

describe('pièces jointes', () => {
  it('affiche les images, pas le PDF ni le HEIC', () => {
    expect(isPreviewable('image/jpeg')).toBe(true)
    expect(isPreviewable('image/webp')).toBe(true)
    expect(isPreviewable('application/pdf')).toBe(false)
    expect(isPreviewable('image/heic')).toBe(false)
  })

  it('écrit les tailles à la française', () => {
    expect(formatFileSize(850)).toBe('850 o')
    expect(formatFileSize(12 * 1024)).toBe('12 Ko')
    expect(formatFileSize(2.4 * 1024 * 1024)).toBe('2,4 Mo')
  })

  it('refuse un fichier de plus de 10 Mo avant l’envoi', () => {
    expect(attachmentTooLarge({ size: MAX_ATTACHMENT_BYTES })).toBe(false)
    expect(attachmentTooLarge({ size: MAX_ATTACHMENT_BYTES + 1 })).toBe(true)
  })

  it('construit les adresses, nom du fichier encodé', () => {
    expect(attachmentUploadUrl('t1', 'fiche de paie & co.pdf')).toBe(
      '/api/transactions/t1/attachments?fileName=fiche%20de%20paie%20%26%20co.pdf',
    )
    expect(attachmentContentUrl('a1')).toBe('/api/attachments/a1/content')
    expect(attachmentContentUrl('a1', true)).toBe('/api/attachments/a1/content?download=true')
  })

  it('accorde le nombre de pièces jointes', () => {
    expect(attachmentCountLabel(1)).toBe('1 pièce jointe')
    expect(attachmentCountLabel(3)).toBe('3 pièces jointes')
  })
})
