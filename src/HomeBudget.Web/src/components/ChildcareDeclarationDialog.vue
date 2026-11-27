<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { mdiDeleteOutline, mdiPencilOutline, mdiPlus } from '@mdi/js'
import { api } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import {
  declarationForm,
  declarationRequest,
  declarationSections,
  declarationTotals,
  integerFields,
  monthName,
  parseDeclarationValue,
  type ChildcareDeclaration,
  type DeclarationDecimalField,
  type DeclarationForm,
  type DeclarationIntegerField,
} from '@/utils/family'
import { formatAmount } from '@/utils/format'

/**
 * Déclarations Pajemploi d'un mois (P2 bis) : une par salarié, avec le récapitulatif « coût total / CMG / reste à
 * charge » calculé comme sur Pajemploi. Le salarié est un tiers, proposé parmi ceux payés en frais de garde.
 */
const props = defineProps<{
  year: number
  month: number
  declarations: ChildcareDeclaration[]
  /** Tiers déjà payés en frais de garde dans l'année : proposés en premier. */
  employees: { id: string; name: string }[]
}>()
const visible = defineModel<boolean>({ required: true })
const emit = defineEmits<{ changed: [] }>()

const auth = useAuthStore()
const editing = ref<ChildcareDeclaration | null>(null)
const form = ref<DeclarationForm | null>(null)
const saving = ref(false)
const error = ref<string | null>(null)
// Suppression en deux temps : un premier clic arme le bouton, le second supprime.
const pendingDelete = ref<string | null>(null)

const payeeSearch = ref('')
const searchResults = ref<{ id: string; name: string }[]>([])

const title = computed(() => `Déclarations Pajemploi · ${monthName(props.month)} ${props.year}`)
const payeeItems = computed(() => {
  const items = new Map<string, string>()
  if (editing.value) items.set(editing.value.payeeId, editing.value.payeeName)
  for (const e of props.employees) items.set(e.id, e.name)
  for (const p of searchResults.value) items.set(p.id, p.name)
  return [...items].map(([id, name]) => ({ id, name }))
})
const request = computed(() => (form.value ? declarationRequest(form.value, props.year, props.month) : null))
const totals = computed(() => (request.value ? declarationTotals(request.value) : null))
const monthTotals = computed(() =>
  props.declarations.reduce(
    (sum, d) => ({
      totalCost: sum.totalCost + d.totalCost,
      cmg: sum.cmg + d.cmg,
      remaining: sum.remaining + d.remaining,
    }),
    { totalCost: 0, cmg: 0, remaining: 0 },
  ),
)

const fieldRule = (key: DeclarationDecimalField | DeclarationIntegerField) => (v: string) =>
  parseDeclarationValue(v, integerFields.includes(key as DeclarationIntegerField)) !== null ||
  (integerFields.includes(key as DeclarationIntegerField)
    ? 'Nombre entier positif.'
    : 'Nombre positif, 2 décimales au plus.')

// À chaque ouverture, le dialogue repart de la liste du mois.
watch(visible, (shown) => {
  if (shown) close()
})

watch(payeeSearch, async (search) => {
  if (!search || search.length < 2) return
  try {
    const found = await unwrap(api.GET('/api/payees', { params: { query: { search, limit: 10 } } }))
    searchResults.value = found.map((p) => ({ id: p.id, name: p.name }))
  } catch {
    searchResults.value = []
  }
})

function open(d: ChildcareDeclaration | null) {
  error.value = null
  pendingDelete.value = null
  editing.value = d
  form.value = declarationForm(d)
  // Un seul salarié connu et pas encore déclaré ce mois : il est proposé d'office.
  const free = props.employees.filter((e) => !props.declarations.some((x) => x.payeeId === e.id))
  if (!d && free.length === 1) form.value.payeeId = free[0]!.id
}

function close() {
  editing.value = null
  form.value = null
  error.value = null
  pendingDelete.value = null
}

async function save() {
  if (!request.value) return
  saving.value = true
  error.value = null
  try {
    if (editing.value) {
      await unwrap(
        api.PUT('/api/family/childcare/declarations/{id}', {
          params: { path: { id: editing.value.id } },
          headers: ifMatch(editing.value.version),
          body: request.value,
        }),
      )
    } else {
      await unwrap(api.POST('/api/family/childcare/declarations', { body: request.value }))
    }
    close()
    emit('changed')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

async function remove(d: ChildcareDeclaration) {
  if (pendingDelete.value !== d.id) {
    pendingDelete.value = d.id
    return
  }
  pendingDelete.value = null
  error.value = null
  try {
    await unwrap(
      api.DELETE('/api/family/childcare/declarations/{id}', {
        params: { path: { id: d.id } },
        headers: ifMatch(d.version),
      }),
    )
    emit('changed')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  }
}
</script>

<template>
  <v-dialog v-model="visible" max-width="720" scrollable>
    <v-card :title="title" data-testid="childcare-declarations">
      <v-card-text>
        <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

        <!-- Liste des déclarations du mois -->
        <template v-if="!form">
          <v-card
            v-for="d in declarations"
            :key="d.id"
            variant="outlined"
            class="mb-3"
            data-testid="childcare-declaration"
          >
            <v-card-item :title="d.payeeName">
              <v-card-subtitle>
                {{ d.activityDays }} j · {{ String(d.normalHours).replace('.', ',') }} h normales · salaire
                net
                {{ formatAmount(d.netSalary) }}
              </v-card-subtitle>
              <template v-if="auth.canWrite" #append>
                <v-btn
                  :icon="mdiPencilOutline"
                  variant="text"
                  size="small"
                  :aria-label="`Modifier la déclaration de ${d.payeeName}`"
                  @click="open(d)"
                />
                <v-btn
                  :icon="mdiDeleteOutline"
                  variant="text"
                  size="small"
                  :color="pendingDelete === d.id ? 'error' : undefined"
                  :aria-label="
                    pendingDelete === d.id
                      ? 'Confirmer la suppression'
                      : `Supprimer la déclaration de ${d.payeeName}`
                  "
                  @click="remove(d)"
                />
              </template>
            </v-card-item>
            <v-card-text class="pt-0">
              <div class="d-flex justify-space-between">
                <span>Coût total de l'emploi</span><span>{{ formatAmount(d.totalCost) }}</span>
              </div>
              <div class="d-flex justify-space-between text-success">
                <span>Complément du mode de garde (CMG)</span><span>− {{ formatAmount(d.cmg) }}</span>
              </div>
              <div class="d-flex justify-space-between font-weight-bold">
                <span>Montant restant à charge</span><span>{{ formatAmount(d.remaining) }}</span>
              </div>
            </v-card-text>
          </v-card>
          <p v-if="!declarations.length" class="text-medium-emphasis">
            Aucune déclaration pour ce mois. Reportez ici le récapitulatif de votre déclaration Pajemploi.
          </p>
          <div v-else-if="declarations.length > 1" class="d-flex justify-space-between font-weight-bold mb-2">
            <span>Reste à charge du mois</span><span>{{ formatAmount(monthTotals.remaining) }}</span>
          </div>
        </template>

        <!-- Saisie d'une déclaration -->
        <v-form v-else @submit.prevent="save">
          <v-autocomplete
            v-model="form.payeeId"
            v-model:search="payeeSearch"
            :items="payeeItems"
            item-title="name"
            item-value="id"
            label="Salarié"
            hint="Le tiers à qui vous versez le salaire."
            persistent-hint
            no-data-text="Tapez au moins deux lettres du nom du tiers."
            :rules="[(v: string | null) => !!v || 'Choisissez le salarié.']"
            class="mb-2"
            data-testid="declaration-payee"
          />
          <template v-for="section in declarationSections.filter((s) => !s.advanced)" :key="section.title">
            <div class="text-subtitle-2 mt-3 mb-1">{{ section.title }}</div>
            <v-row dense>
              <v-col v-for="f in section.fields" :key="f.key" cols="12" sm="6">
                <v-text-field
                  v-model="form[f.key]"
                  :label="f.label"
                  :suffix="f.suffix"
                  inputmode="decimal"
                  placeholder="0"
                  :rules="[fieldRule(f.key)]"
                  :data-testid="`declaration-${f.key}`"
                />
              </v-col>
            </v-row>
          </template>
          <v-expansion-panels variant="accordion" class="mt-2">
            <v-expansion-panel title="Informations complémentaires">
              <v-expansion-panel-text>
                <v-text-field
                  v-model="form.longCareDays"
                  label="Jours de garde de plus de 24 h"
                  suffix="j"
                  inputmode="numeric"
                  placeholder="0"
                  :rules="[fieldRule('longCareDays')]"
                />
                <v-switch
                  v-model="form.disabledChildCare"
                  label="Accueil d'un enfant handicapé, malade ou invalide"
                  color="primary"
                  hide-details
                />
              </v-expansion-panel-text>
            </v-expansion-panel>
            <v-expansion-panel title="Fin de contrat">
              <v-expansion-panel-text>
                <v-switch
                  v-model="form.isContractEnd"
                  label="S'agit-il d'une fin de contrat ?"
                  color="primary"
                  hide-details
                />
                <template v-if="form.isContractEnd">
                  <v-text-field v-model="form.contractEndDate" label="Date de fin de contrat" type="date" />
                  <v-row dense>
                    <v-col
                      v-for="f in declarationSections.find((s) => s.title === 'Indemnités de fin de contrat')!
                        .fields"
                      :key="f.key"
                      cols="12"
                      sm="6"
                    >
                      <v-text-field
                        v-model="form[f.key]"
                        :label="f.label"
                        :suffix="f.suffix"
                        inputmode="decimal"
                        placeholder="0"
                        :rules="[fieldRule(f.key)]"
                      />
                    </v-col>
                  </v-row>
                </template>
              </v-expansion-panel-text>
            </v-expansion-panel>
          </v-expansion-panels>

          <v-sheet
            v-if="totals"
            color="surface-variant"
            rounded
            class="pa-3 mt-4"
            data-testid="declaration-totals"
          >
            <div class="d-flex justify-space-between">
              <span>Salaire net déclaré</span><span>{{ formatAmount(totals.remuneration) }}</span>
            </div>
            <div class="d-flex justify-space-between">
              <span>Coût total de l'emploi</span><span>{{ formatAmount(totals.totalCost) }}</span>
            </div>
            <div class="d-flex justify-space-between">
              <span>Complément du mode de garde (CMG)</span><span>− {{ formatAmount(totals.cmg) }}</span>
            </div>
            <div class="d-flex justify-space-between font-weight-bold">
              <span>Montant restant à charge</span><span>{{ formatAmount(totals.remaining) }}</span>
            </div>
          </v-sheet>
        </v-form>
      </v-card-text>
      <v-card-actions>
        <template v-if="form">
          <v-spacer />
          <v-btn variant="text" @click="close">Annuler</v-btn>
          <v-btn
            color="primary"
            :disabled="!request"
            :loading="saving"
            data-testid="declaration-save"
            @click="save"
          >
            Enregistrer
          </v-btn>
        </template>
        <template v-else>
          <v-btn
            v-if="auth.canWrite"
            color="primary"
            :prepend-icon="mdiPlus"
            data-testid="declaration-add"
            @click="open(null)"
          >
            Ajouter une déclaration
          </v-btn>
          <v-spacer />
          <v-btn variant="text" @click="visible = false">Fermer</v-btn>
        </template>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
