import { defineConfig } from 'eslint/config'
import pluginVue from 'eslint-plugin-vue'
import tseslint from 'typescript-eslint'

export default defineConfig(
  { ignores: ['node_modules/', 'dist/', 'src/api/schema.d.ts'] },
  ...tseslint.configs.recommended,
  // « essential » : règles de correction uniquement, la mise en forme est confiée à Prettier.
  ...pluginVue.configs['flat/essential'],
  {
    files: ['**/*.vue'],
    languageOptions: { parserOptions: { parser: tseslint.parser } },
  },
)
