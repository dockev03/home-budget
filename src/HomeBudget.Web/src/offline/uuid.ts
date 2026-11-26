/**
 * UUID v7 (RFC 9562) : horodatage en millisecondes sur 48 bits, puis de l'aléa. Triable par date et généré sur
 * l'appareil, il identifie une saisie hors ligne et rend son envoi idempotent (DM2).
 */
export function uuidv7(
  now: number = Date.now(),
  random: Uint8Array = crypto.getRandomValues(new Uint8Array(10)),
): string {
  const bytes = new Uint8Array(16)
  let time = now
  for (let i = 5; i >= 0; i--) {
    bytes[i] = time % 256
    time = Math.floor(time / 256)
  }
  bytes.set(random, 6)
  bytes[6] = 0x70 | (bytes[6]! & 0x0f) // version 7
  bytes[8] = 0x80 | (bytes[8]! & 0x3f) // variante RFC 4122
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}
