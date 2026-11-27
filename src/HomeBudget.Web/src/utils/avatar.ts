/** Photo de profil : pastille et menu du compte. */

/** Côté du carré envoyé, en pixels : assez pour un écran haute densité, quelques dizaines de Ko. */
export const AVATAR_SIZE = 256

/** Plafond accepté par le serveur (256 Kio). */
export const AVATAR_MAX_BYTES = 256 * 1024

/** Adresse de la photo ; la date sert de version, pour ne pas réafficher l'ancienne copie gardée hors ligne. */
export function avatarUrl(avatarUpdatedAt: string | null | undefined): string | null {
  return avatarUpdatedAt ? `/api/account/avatar?v=${encodeURIComponent(avatarUpdatedAt)}` : null
}

/** Carré centré découpé dans l'image d'origine (portrait ou paysage). */
export function squareCrop(width: number, height: number): { x: number; y: number; size: number } {
  const size = Math.min(width, height)
  return { x: Math.floor((width - size) / 2), y: Math.floor((height - size) / 2), size }
}

function toBlob(canvas: HTMLCanvasElement, type: string, quality: number): Promise<Blob | null> {
  return new Promise((resolve) => canvas.toBlob(resolve, type, quality))
}

/**
 * Recadre la photo choisie en carré et la réduit à {@link AVATAR_SIZE} pixels, en WebP (JPEG si le navigateur ne sait
 * pas l'écrire). L'orientation EXIF des photos de téléphone est appliquée par le navigateur au décodage.
 * Rejette si l'image ne peut pas être lue (format inconnu du navigateur, HEIC hors Safari…).
 */
export async function resizeAvatar(file: Blob): Promise<Blob> {
  const bitmap = await createImageBitmap(file)
  try {
    const crop = squareCrop(bitmap.width, bitmap.height)
    const canvas = document.createElement('canvas')
    canvas.width = canvas.height = Math.min(AVATAR_SIZE, crop.size)
    const context = canvas.getContext('2d')
    if (!context) throw new Error('Canvas indisponible.')
    context.imageSmoothingQuality = 'high'
    context.drawImage(bitmap, crop.x, crop.y, crop.size, crop.size, 0, 0, canvas.width, canvas.height)
    const webp = await toBlob(canvas, 'image/webp', 0.85)
    if (webp?.type === 'image/webp') return webp
    const jpeg = await toBlob(canvas, 'image/jpeg', 0.85)
    if (!jpeg) throw new Error('Conversion impossible.')
    return jpeg
  } finally {
    bitmap.close()
  }
}
