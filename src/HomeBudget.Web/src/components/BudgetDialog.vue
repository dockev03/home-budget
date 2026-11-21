<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { budgetModes, type BudgetLine, type BudgetMode } from '@/utils/budget'
import { formatAmount, parseAmount, roundCents } from '@/utils/format'

const props = defineProps<{ line: BudgetLine | null; incomeBase: number }>()
const visible = defineModel<boolean>({ required: true })
const emit = defineEmits<{ saved: [] }>()

const mode = ref<BudgetMode>('Amount')
const valueText = ref('')
const saving = ref(false)
const deleting = ref(false)
const error = ref<string | null>(null)

// Un revenu ne peut pas être budgété en % des revenus (calcul circulaire).
const modes = computed(() =>
  budgetModes.filter((m) => m.value !== 'PercentOfIncome' || props.line?.kind !== 'Income'),
)
const value = computed(() => parseAmount(valueText.value))
const valueRule = (text: string) => {
  const v = parseAmount(text)
  if (v === null || v < 0) return 'Nombre positif, 2 décimales au plus.'
  if (mode.value === 'PercentOfIncome' && v > 100) return 'Le pourcentage va de 0 à 100.'
  return true
}
const valid = computed(() => mode.value === 'Auto' || valueRule(valueText.value) === true)
const preview = computed(() =>
  mode.value === 'PercentOfIncome' && value.value !== null
    ? `Soit ${formatAmount(roundCents((props.incomeBase * value.value) / 100))} sur ${formatAmount(props.incomeBase)} de revenus budgétés.`
    : '',
)
const hasChildren = computed(() => !!props.line && props.line.budgeted !== props.line.ownBudgeted)

watch(visible, (open) => {
  if (!open || !props.line) return
  error.value = null
  mode.value = props.line.mode ?? 'Amount'
  valueText.value =
    props.line.value !== null && props.line.mode !== 'Auto' ? String(props.line.value).replace('.', ',') : ''
})

async function save() {
  if (!props.line || !valid.value) return
  saving.value = true
  error.value = null
  try {
    await unwrap(
      api.PUT('/api/budgets/categories/{categoryId}', {
        params: { path: { categoryId: props.line.categoryId } },
        headers: props.line.budgetVersion !== null ? ifMatch(props.line.budgetVersion) : {},
        body: { mode: mode.value, value: mode.value === 'Auto' ? 0 : (value.value ?? 0) },
      }),
    )
    emit('saved')
    visible.value = false
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

async function reset() {
  if (!props.line || props.line.budgetVersion === null) return
  deleting.value = true
  error.value = null
  try {
    await unwrap(
      api.DELETE('/api/budgets/categories/{categoryId}', {
        params: { path: { categoryId: props.line.categoryId } },
        headers: ifMatch(props.line.budgetVersion),
      }),
    )
    emit('saved')
    visible.value = false
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  } finally {
    deleting.value = false
  }
}
</script>

<template>
  <v-dialog v-model="visible" max-width="480">
    <v-card v-if="line" :title="`Budget : ${line.name}`">
      <v-card-text>
        <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
        <p v-if="hasChildren" class="text-body-2 text-medium-emphasis mb-4">
          Ce budget s'ajoute à celui des sous-catégories.
        </p>
        <v-btn-toggle v-model="mode" mandatory color="primary" variant="outlined" divided class="mb-4">
          <v-btn v-for="m in modes" :key="m.value" :value="m.value">{{ m.title }}</v-btn>
        </v-btn-toggle>

        <form @submit.prevent="save">
          <v-text-field
            v-if="mode !== 'Auto'"
            v-model="valueText"
            :label="mode === 'Amount' ? 'Montant par période' : 'Pourcentage des revenus budgétés'"
            :suffix="mode === 'Amount' ? '€' : '%'"
            :rules="[valueRule]"
            :hint="preview"
            persistent-hint
            inputmode="decimal"
            autofocus
          />
          <p v-else class="text-body-2">
            Le budget est la somme des échéances de récurrence de la période.
            <span v-if="line.kind === 'Fixed'">C'est le mode par défaut des dépenses fixes.</span>
            Les récurrences arriveront à l'étape suivante : d'ici là, il vaut 0 €.
          </p>
          <button type="submit" hidden />
        </form>
      </v-card-text>
      <v-card-actions>
        <v-btn v-if="line.budgetVersion !== null" color="error" :loading="deleting" @click="reset">
          Revenir au défaut
        </v-btn>
        <v-spacer />
        <v-btn @click="visible = false">Annuler</v-btn>
        <v-btn color="primary" variant="flat" :loading="saving" :disabled="!valid" @click="save">
          Enregistrer
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
