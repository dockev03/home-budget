// Déclaration minimale du paquet « qrcode » (pas de types fournis) : seule la fonction utilisée.
declare module 'qrcode' {
  const QRCode: {
    toDataURL(
      text: string,
      options?: { margin?: number; width?: number; errorCorrectionLevel?: 'L' | 'M' | 'Q' | 'H' },
    ): Promise<string>
  }
  export default QRCode
}
