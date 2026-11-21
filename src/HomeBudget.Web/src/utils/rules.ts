/** Règles de validation des champs de formulaire Vuetify (true ou message d'erreur). */
export const required = (value: unknown) =>
  (value !== null && value !== undefined && String(value).trim() !== '') || 'Champ obligatoire.'

export const minLength = (length: number) => (value: string) =>
  (value ?? '').length >= length || `${length} caractères minimum.`

export const passwordRules = [required, minLength(12)]

export const totpCode = (value: string) =>
  /^\d{6}$/.test((value ?? '').replace(/\s/g, '')) || 'Le code comporte 6 chiffres.'
