/** Pièces jointes des opérations (B7) : formats acceptés, taille et affichage. */

/** Taille maximale d'un fichier (10 Mio), vérifiée aussi par le serveur. */
export const MAX_ATTACHMENT_BYTES = 10 * 1024 * 1024

/** Types proposés par le sélecteur de fichiers ; le serveur reconnaît le type sur les premiers octets. */
export const ATTACHMENT_ACCEPT =
  'image/jpeg,image/png,image/webp,image/heic,image/heif,application/pdf,.heic,.heif'

const imageTypes = new Set(['image/jpeg', 'image/png', 'image/webp'])

/** Images affichées en aperçu ; le PDF et le HEIC (non lu par la plupart des navigateurs) se téléchargent. */
export function isPreviewable(contentType: string): boolean {
  return imageTypes.has(contentType)
}

/** Taille lisible : « 850 o », « 12 Ko », « 2,4 Mo ». */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} o`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} Ko`
  const mb = bytes / (1024 * 1024)
  return `${mb.toLocaleString('fr-FR', { maximumFractionDigits: 1 })} Mo`
}

/** Refus immédiat d'un fichier trop gros, sans l'envoyer. */
export function attachmentTooLarge(file: { size: number }): boolean {
  return file.size > MAX_ATTACHMENT_BYTES
}

export function attachmentContentUrl(id: string, download = false): string {
  return `/api/attachments/${encodeURIComponent(id)}/content${download ? '?download=true' : ''}`
}

export function attachmentUploadUrl(transactionId: string, fileName: string): string {
  return `/api/transactions/${encodeURIComponent(transactionId)}/attachments?fileName=${encodeURIComponent(fileName)}`
}

/** Libellé du nombre de pièces jointes, pour l'infobulle du trombone. */
export function attachmentCountLabel(count: number): string {
  return count > 1 ? `${count} pièces jointes` : '1 pièce jointe'
}
