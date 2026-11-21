/** En-tête de concurrence optimiste exigé par l'API pour PUT et DELETE. */
export function ifMatch(version: number): { 'If-Match': string } {
  return { 'If-Match': `"${version}"` }
}
