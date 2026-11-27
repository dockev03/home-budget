<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { movableAmount, periodLabel, type BudgetLine } from '@/utils/budget'
import { formatAmount, parseAmount } from '@/utils/format'

/** Déplacer un montant du budget d'une catégorie vers une autre, pour la période affichée (D7). */
const props = defineProps<{ lines: BudgetLine[]; periodKey: string | null }>()
const visible = defineModel<boolean>({ required: true })
const emit = defineEmits<{ saved: [] }>()

const fromId = ref<string | null>(null)
const toId = ref<string | null>(null)
const amountText = ref('')
const saving = ref(false)
const error = ref<string | null>(null)

const assignable = computed(() => props.lines.filter((l) => l.kind !== 'Income' && !l.isArchived))
const item = (l: BudgetLine) => ({
  value: l.categoryId,
  title: l.parentId ? `  ${l.name}` : l.name,
  subtitle: `${formatAmount(movableAmount(l))} budgétés`,
})
const sources = computed(() => assignable.value.filter((l) => movableAmount(l) > 0).map(item))
const targets = computed(() => assignable.value.filter((l) => l.categoryId !== fromId.value).map(item))
const available = computed(() => {
  const from = assignable.value.find((l) => l.categoryId === fromId.value)
  return from ? movableAmount(from) : 0
})
const amount = computed(() => parseAmount(amountText.value))
const amountRule = (text: string) => {
  const v = parseAmount(text)
  if (v === null || v <= 0) return 'Montant positif, 2 décimales au plus.'
  if (v > available.value) return `${formatAmount(available.value)} au plus.`
  return true
}
const valid = computed(
  () =>
    !!fromId.value && !!toId.value && fromId.value !== toId.value && amountRule(amountText.value) === true,
)

watch(visible, (open) => {
  if (!open) return
  fromId.value = null
  toId.value = null
  amountText.value = ''
  error.value = null
})

async function save() {
  if (!valid.value || !props.periodKey) return
  saving.value = true
  error.value = null
  try {
    await unwrap(
      api.POST('/api/budgets/{periodKey}/moves', {
        params: { path: { periodKey: props.periodKey } },
        body: { fromCategoryId: fromId.value!, toCategoryId: toId.value!, amount: amount.value! },
      }),
    )
    emit('saved')
    visible.value = false
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Déplacement impossible.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <v-dialog v-model="visible" max-width="480">
    <v-card title="Déplacer du budget">
      <v-card-text>
        <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
        <p class="text-body-2 text-medium-emphasis mb-4">
          Pour {{ periodKey ? periodLabel(periodKey) : 'cette période' }} seulement : les deux budgets
          deviennent des montants propres à la période. Les provisions et le reste reporté ne se déplacent
          pas.
        </p>
        <form @submit.prevent="save">
          <v-select v-model="fromId" :items="sources" label="Depuis" data-testid="move-from" />
          <v-select v-model="toId" :items="targets" label="Vers" data-testid="move-to" />
          <v-text-field
            v-model="amountText"
            label="Montant"
            suffix="€"
            :rules="[amountRule]"
            inputmode="decimal"
            data-testid="move-amount"
          />
          <button type="submit" hidden />
        </form>
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn @click="visible = false">Annuler</v-btn>
        <v-btn color="primary" variant="flat" :loading="saving" :disabled="!valid" @click="save"
          >Déplacer</v-btn
        >
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
