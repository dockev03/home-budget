<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useTheme } from 'vuetify'
import { mdiContentSaveOutline, mdiDeleteOutline, mdiFileDelimitedOutline, mdiMicrosoftExcel } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import PeriodNavigator from '@/components/PeriodNavigator.vue'
import { VChart } from '@/plugins/echarts'
import { useAuthStore } from '@/stores/auth'
import { useReferenceStore } from '@/stores/reference'
import { chartColors } from '@/utils/charts'
import { formatAmount, formatDate, formatSignedAmount } from '@/utils/format'
import { categoryKinds } from '@/utils/labels'
import { periodLabel } from '@/utils/budget'
import {
  emptyReportForm,
  formFromSaved,
  groupByOptions,
  rangeOptions,
  reportBarsOption,
  reportExportUrl,
  reportQuery,
  sankeyOption,
  savedReportBody,
  shiftPeriodKey,
  type Flows,
  type Report,
  type ReportForm,
  type SavedReport,
} from '@/utils/reports'
import { required } from '@/utils/rules'

/** Rapports personnalisés (G8) et diagramme de flux revenus → types → catégories (G9). */
const theme = useTheme()
const auth = useAuthStore()
const reference = useReferenceStore()
const dark = computed(() => theme.global.current.value.dark)
const colors = computed(() => chartColors(dark.value))

const tab = ref<'report' | 'flows'>('report')
const error = ref<string | null>(null)

// --- Rapport personnalisé (G8) ---
const form = ref<ReportForm>(emptyReportForm())
const report = ref<Report | null>(null)
const running = ref(false)
const saved = ref<SavedReport[]>([])
const current = ref<SavedReport | null>(null)
const name = ref('')
const saving = ref(false)
const confirmDelete = ref(false)
const payees = ref<{ value: string; title: string }[]>([])
const tags = ref<{ value: string; title: string }[]>([])

const accounts = computed(() => reference.accounts.map((a) => ({ value: a.id, title: a.name })))
const categories = computed(() =>
  reference.categories.map((c) => ({ value: c.id, title: reference.categoryLabel(c.id) })),
)
const chart = computed(() => (report.value ? reportBarsOption(report.value, colors.value) : null))
const rowLabel = (label: string) => (report.value?.groupBy === 'Period' ? periodLabel(label) : label)
const customRangeValid = computed(
  () =>
    form.value.range !== 'Custom' ||
    (!!form.value.from && !!form.value.to && form.value.from <= form.value.to),
)

async function run() {
  if (!customRangeValid.value) return
  running.value = true
  error.value = null
  try {
    report.value = await unwrap(
      api.GET('/api/reports/custom', { params: { query: reportQuery(form.value) } }),
    )
  } catch (e) {
    report.value = null
    error.value = e instanceof ApiError ? e.message : 'Rapport impossible.'
  } finally {
    running.value = false
  }
}

async function loadSaved() {
  saved.value = await unwrap(api.GET('/api/reports/saved'))
}

function open(report: SavedReport | null) {
  current.value = report
  confirmDelete.value = false
  name.value = report?.name ?? ''
  form.value = report ? formFromSaved(report) : emptyReportForm()
  void run()
}

async function save(asNew: boolean) {
  if (!name.value.trim()) return
  saving.value = true
  error.value = null
  const body = savedReportBody(name.value, form.value)
  try {
    const result =
      current.value && !asNew
        ? await unwrap(
            api.PUT('/api/reports/saved/{id}', {
              params: { path: { id: current.value.id } },
              headers: ifMatch(current.value.version),
              body,
            }),
          )
        : await unwrap(api.POST('/api/reports/saved', { body }))
    await loadSaved()
    current.value = saved.value.find((r) => r.id === result.id) ?? null
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

async function remove() {
  if (!current.value) return
  if (!confirmDelete.value) {
    confirmDelete.value = true
    return
  }
  try {
    await unwrap(
      api.DELETE('/api/reports/saved/{id}', {
        params: { path: { id: current.value.id } },
        headers: ifMatch(current.value.version),
      }),
    )
    await loadSaved()
    open(null)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  }
}

// --- Flux (G9) ---
const flowsEnd = ref<string | null>(null)
const flowsSpan = ref<1 | 3 | 12>(1)
const flows = ref<Flows | null>(null)
const flowsPeriod = ref<Schemas['PeriodDto'] | null>(null)
const flowsLoading = ref(false)
const sankey = computed(() =>
  flows.value && flows.value.nodes.length ? sankeyOption(flows.value, colors.value) : null,
)

async function loadFlows() {
  flowsLoading.value = true
  error.value = null
  try {
    flowsPeriod.value = flowsEnd.value
      ? await unwrap(api.GET('/api/periods/{key}', { params: { path: { key: flowsEnd.value } } }))
      : await unwrap(api.GET('/api/periods/current'))
    const to = flowsPeriod.value.key
    flows.value = await unwrap(
      api.GET('/api/reports/flows', {
        params: { query: { from: shiftPeriodKey(to, 1 - flowsSpan.value), to } },
      }),
    )
  } catch (e) {
    flows.value = null
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    flowsLoading.value = false
  }
}

watch([flowsEnd, flowsSpan], loadFlows)
watch(tab, (t) => {
  if (t === 'flows' && !flows.value) void loadFlows()
})

onMounted(async () => {
  try {
    const [, , payeeList, tagList] = await Promise.all([
      reference.load(),
      loadSaved(),
      unwrap(api.GET('/api/payees', { params: { query: { limit: 100 } } })),
      unwrap(api.GET('/api/tags')),
    ])
    payees.value = payeeList.map((p) => ({ value: p.id, title: p.name }))
    tags.value = tagList.map((t) => ({ value: t.id, title: t.name }))
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
  await run()
})
</script>

<template>
  <h1 class="text-h5 font-weight-bold mb-4">Rapports</h1>

  <v-tabs v-model="tab" color="primary" class="mb-4">
    <v-tab value="report">Rapport personnalisé</v-tab>
    <v-tab value="flows">Flux</v-tab>
  </v-tabs>

  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

  <template v-if="tab === 'report'">
    <v-card class="mb-4" data-testid="report-form">
      <v-card-text>
        <v-row dense>
          <v-col cols="12" md="6">
            <v-select
              :model-value="current?.id ?? null"
              :items="[
                { value: null, title: 'Nouveau rapport' },
                ...saved.map((r) => ({ value: r.id, title: r.name })),
              ]"
              label="Rapport enregistré"
              data-testid="saved-reports"
              @update:model-value="(id) => open(saved.find((r) => r.id === id) ?? null)"
            />
          </v-col>
          <v-col cols="6" md="3">
            <v-select v-model="form.groupBy" :items="groupByOptions" label="Regrouper par" />
          </v-col>
          <v-col cols="6" md="3">
            <v-select v-model="form.range" :items="rangeOptions" label="Plage" />
          </v-col>
          <template v-if="form.range === 'Custom'">
            <v-col cols="6">
              <v-text-field v-model="form.from" type="date" label="Du" :rules="[required]" />
            </v-col>
            <v-col cols="6">
              <v-text-field v-model="form.to" type="date" label="Au" :rules="[required]" />
            </v-col>
          </template>
          <v-col cols="12" md="6">
            <v-autocomplete
              v-model="form.accountIds"
              :items="accounts"
              label="Comptes"
              multiple
              chips
              closable-chips
              clearable
            />
          </v-col>
          <v-col cols="12" md="6">
            <v-autocomplete
              v-model="form.categoryIds"
              :items="categories"
              label="Catégories"
              multiple
              chips
              closable-chips
              clearable
            />
          </v-col>
          <v-col cols="12" md="6">
            <v-autocomplete
              v-model="form.payeeIds"
              :items="payees"
              label="Tiers"
              multiple
              chips
              closable-chips
              clearable
            />
          </v-col>
          <v-col cols="12" md="6">
            <v-autocomplete
              v-model="form.tagIds"
              :items="tags"
              label="Tags"
              multiple
              chips
              closable-chips
              clearable
            />
          </v-col>
          <v-col cols="12">
            <v-chip-group v-model="form.kinds" multiple column selected-class="text-primary">
              <v-chip v-for="k in categoryKinds" :key="k.value" :value="k.value" filter variant="outlined">{{
                k.title
              }}</v-chip>
            </v-chip-group>
            <v-switch
              v-model="form.includeTransfers"
              label="Inclure les virements sans catégorie"
              color="primary"
              hide-details
              density="compact"
            />
          </v-col>
        </v-row>
      </v-card-text>
      <v-card-actions class="flex-wrap">
        <v-btn
          color="primary"
          variant="flat"
          :loading="running"
          :disabled="!customRangeValid"
          data-testid="run-report"
          @click="run"
        >
          Afficher
        </v-btn>
        <v-btn
          :href="reportExportUrl(form, 'csv')"
          :prepend-icon="mdiFileDelimitedOutline"
          variant="text"
          :disabled="!customRangeValid"
        >
          CSV
        </v-btn>
        <v-btn
          :href="reportExportUrl(form, 'xlsx')"
          :prepend-icon="mdiMicrosoftExcel"
          variant="text"
          :disabled="!customRangeValid"
        >
          Excel
        </v-btn>
        <v-spacer />
        <template v-if="auth.canWrite">
          <v-text-field
            v-model="name"
            label="Nom du rapport"
            density="compact"
            hide-details
            maxlength="80"
            style="min-width: 180px; max-width: 260px"
            data-testid="report-name"
          />
          <v-btn
            :prepend-icon="mdiContentSaveOutline"
            variant="text"
            :loading="saving"
            :disabled="!name.trim() || !customRangeValid"
            data-testid="save-report"
            @click="save(false)"
          >
            {{ current ? 'Mettre à jour' : 'Enregistrer' }}
          </v-btn>
          <v-btn v-if="current" variant="text" :disabled="!name.trim()" @click="save(true)"
            >Enregistrer sous</v-btn
          >
          <v-btn
            v-if="current"
            :prepend-icon="mdiDeleteOutline"
            :color="confirmDelete ? 'error' : undefined"
            :variant="confirmDelete ? 'flat' : 'text'"
            @click="remove"
          >
            {{ confirmDelete ? 'Confirmer' : 'Supprimer' }}
          </v-btn>
        </template>
      </v-card-actions>
    </v-card>

    <template v-if="report">
      <v-card class="mb-4" data-testid="report-result">
        <v-card-title class="text-wrap">
          Du {{ formatDate(report.from) }} au {{ formatDate(report.to) }}
        </v-card-title>
        <v-card-subtitle class="text-wrap">
          Entrées {{ formatAmount(report.credit) }} · Sorties {{ formatAmount(report.debit) }} · Solde
          <strong data-testid="report-net">{{ formatSignedAmount(report.net) }}</strong> ·
          {{ report.count }} opération(s)
        </v-card-subtitle>
        <div v-if="chart && report.rows.length" class="px-2" style="height: 300px">
          <VChart
            :option="chart"
            :theme="dark ? 'dark' : undefined"
            autoresize
            aria-label="Entrées et sorties par groupe"
          />
        </div>
        <v-table density="compact">
          <thead>
            <tr>
              <th>{{ groupByOptions.find((g) => g.value === report!.groupBy)?.title }}</th>
              <th class="text-right">Entrées</th>
              <th class="text-right">Sorties</th>
              <th class="text-right">Solde</th>
              <th class="text-right">Opérations</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="row in report.rows" :key="row.key">
              <td>
                <span v-if="row.color" class="hb-swatch mr-1" :style="{ background: row.color }" />
                {{ rowLabel(row.label) }}
              </td>
              <td class="text-right">{{ formatAmount(row.credit) }}</td>
              <td class="text-right">{{ formatAmount(row.debit) }}</td>
              <td class="text-right font-weight-medium" :class="row.net < 0 ? 'text-error' : ''">
                {{ formatSignedAmount(row.net) }}
              </td>
              <td class="text-right">{{ row.count }}</td>
            </tr>
            <tr v-if="report.rows.length === 0">
              <td colspan="5" class="text-medium-emphasis">Aucune opération ne correspond.</td>
            </tr>
          </tbody>
        </v-table>
      </v-card>
    </template>
  </template>

  <template v-else>
    <div class="d-flex align-center flex-wrap ga-2 mb-4">
      <v-btn-toggle
        v-model="flowsSpan"
        mandatory
        density="compact"
        variant="outlined"
        color="primary"
        divided
        class="mr-auto"
      >
        <v-btn :value="1" size="small">1 période</v-btn>
        <v-btn :value="3" size="small">3 périodes</v-btn>
        <v-btn :value="12" size="small">12 périodes</v-btn>
      </v-btn-toggle>
      <PeriodNavigator :period="flowsPeriod" @go="(key) => (flowsEnd = key)" />
    </div>
    <v-progress-linear v-if="flowsLoading" indeterminate color="primary" class="mb-2" />
    <v-card v-if="flows" data-testid="flows">
      <v-card-subtitle class="pt-3 text-wrap">
        <template v-if="flowsSpan > 1"
          >{{ periodLabel(flows.from) }} – {{ periodLabel(flows.to) }} ·
        </template>
        Revenus {{ formatAmount(flows.income) }} · Dépenses et épargne {{ formatAmount(flows.outflows) }}
      </v-card-subtitle>
      <div v-if="sankey" class="px-2" style="height: 480px">
        <VChart
          :option="sankey"
          :theme="dark ? 'dark' : undefined"
          autoresize
          aria-label="Flux des revenus vers les dépenses fixes, variables et l'épargne, par catégorie"
        />
      </div>
      <v-card-text v-else class="text-medium-emphasis"
        >Aucun revenu ni dépense catégorisés sur cette période.</v-card-text
      >
    </v-card>
  </template>
</template>

<style scoped>
.hb-swatch {
  display: inline-block;
  width: 10px;
  height: 10px;
  border-radius: 50%;
}
</style>
