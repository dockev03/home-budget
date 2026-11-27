<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { budgetModes, canCarryOver, periodLabel, type BudgetLine, type BudgetMode } from '@/utils/budget'
import { formatAmount, formatSignedAmount, parseAmount, roundCents } from '@/utils/format'

const props = defineProps<{
  line: BudgetLine | null
  incomeBase: number
  periodKey: string | null
  /** Mode enveloppes actif (D7) : toute dépense reporte son reste. */
  envelope?: boolean
}>()
const visible = defineModel<boolean>({ required: true })
const emit = defineEmits<{ saved: [] }>()

/** Portée : budget par défaut (toutes les périodes) ou budget propre à la période affichée (D4). */
type Scope = 'default' | 'period'

const scope = ref<Scope>('default')
const mode = ref<BudgetMode>('Amount')
const valueText = ref('')
const saving = ref(false)
const deleting = ref(false)
const error = ref<string | null>(null)
/** Report du reste (D6) : période de début, null si désactivé ; modifié aussitôt, sans « Enregistrer ». */
const carryOverSince = ref<string | null>(null)
const carrying = ref(false)

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
const periodName = computed(() => (props.periodKey ? periodLabel(props.periodKey) : ''))
const canDelete = computed(() =>
  scope.value === 'period' ? !!props.line?.periodBudget : props.line?.budgetVersion !== null,
)

function fill() {
  const line = props.line
  if (!line) return
  let next: { mode: BudgetMode; value: number | null }
  if (scope.value === 'period' && line.periodBudget) {
    next = { mode: line.periodBudget.mode ?? 'Amount', value: line.periodBudget.value }
  } else if (scope.value === 'period') {
    // Nouveau budget de la période : on part de celui qui s'applique aujourd'hui.
    next = { mode: line.mode ?? 'Amount', value: line.mode === line.defaultMode ? line.value : null }
  } else if (line.defaultMode) {
    next = { mode: line.defaultMode, value: line.value }
  } else {
    next = { mode: line.kind === 'Fixed' ? 'Auto' : 'Amount', value: null }
  }
  mode.value = next.mode
  valueText.value = next.value !== null && next.mode !== 'Auto' ? String(next.value).replace('.', ',') : ''
}

watch(visible, (open) => {
  if (!open || !props.line) return
  error.value = null
  carryOverSince.value = props.line.carryOverSince
  scope.value = props.line.periodBudget ? 'period' : 'default'
  fill()
})

async function toggleCarryOver(enabled: boolean | null) {
  const line = props.line
  if (!line) return
  carrying.value = true
  error.value = null
  try {
    const result = await unwrap(
      api.PUT('/api/budgets/categories/{categoryId}/carry-over', {
        params: { path: { categoryId: line.categoryId } },
        body: { enabled: !!enabled, since: null },
      }),
    )
    carryOverSince.value = result.since
    emit('saved')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    carrying.value = false
  }
}
watch(scope, () => {
  if (visible.value) fill()
})

async function save() {
  const line = props.line
  if (!line || !valid.value) return
  saving.value = true
  error.value = null
  const body = { mode: mode.value, value: mode.value === 'Auto' ? 0 : (value.value ?? 0) }
  try {
    if (scope.value === 'period' && props.periodKey) {
      await unwrap(
        api.PUT('/api/budgets/{periodKey}/categories/{categoryId}', {
          params: { path: { periodKey: props.periodKey, categoryId: line.categoryId } },
          headers: line.periodBudget ? ifMatch(line.periodBudget.version) : {},
          body,
        }),
      )
    } else {
      await unwrap(
        api.PUT('/api/budgets/categories/{categoryId}', {
          params: { path: { categoryId: line.categoryId } },
          headers: line.budgetVersion !== null ? ifMatch(line.budgetVersion) : {},
          body,
        }),
      )
    }
    emit('saved')
    visible.value = false
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

async function reset() {
  const line = props.line
  if (!line) return
  deleting.value = true
  error.value = null
  try {
    if (scope.value === 'period' && line.periodBudget && props.periodKey) {
      await unwrap(
        api.DELETE('/api/budgets/{periodKey}/categories/{categoryId}', {
          params: { path: { periodKey: props.periodKey, categoryId: line.categoryId } },
          headers: ifMatch(line.periodBudget.version),
        }),
      )
    } else if (line.budgetVersion !== null) {
      await unwrap(
        api.DELETE('/api/budgets/categories/{categoryId}', {
          params: { path: { categoryId: line.categoryId } },
          headers: ifMatch(line.budgetVersion),
        }),
      )
    }
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
        <v-radio-group v-if="periodKey" v-model="scope" inline hide-details class="mb-2" label="S'applique à">
          <v-radio value="default" label="Toutes les périodes" />
          <v-radio value="period" :label="`${periodName} seulement`" />
        </v-radio-group>
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
          </p>
          <p v-if="line.provisioned > 0" class="text-body-2 text-medium-emphasis mt-2">
            Les provisions de la catégorie y ajoutent {{ formatAmount(line.provisioned) }} sur cette période.
          </p>
          <button type="submit" hidden />
        </form>
        <template v-if="canCarryOver(line)">
          <v-divider class="my-4" />
          <v-switch
            :model-value="envelope || carryOverSince !== null"
            :disabled="envelope"
            :loading="carrying"
            color="primary"
            label="Reporter le reste sur la période suivante"
            :hint="
              envelope
                ? 'Toujours actif en mode enveloppes.'
                : carryOverSince
                  ? `Depuis ${periodLabel(carryOverSince)} : un reste positif s'ajoute au budget suivant, un dépassement s'en déduit.`
                  : 'Un reste positif s\'ajoute au budget suivant, un dépassement s\'en déduit.'
            "
            persistent-hint
            data-testid="carry-over"
            @update:model-value="toggleCarryOver"
          />
          <p v-if="line.carriedOver !== 0" class="text-body-2 text-medium-emphasis mt-4">
            Report des périodes précédentes sur {{ periodName }} : {{ formatSignedAmount(line.carriedOver) }}.
          </p>
        </template>
      </v-card-text>
      <v-card-actions>
        <v-btn v-if="canDelete" color="error" :loading="deleting" @click="reset">
          {{ scope === 'period' ? 'Revenir au budget par défaut' : 'Revenir au défaut' }}
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
