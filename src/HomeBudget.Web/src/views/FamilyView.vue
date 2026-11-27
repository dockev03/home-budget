<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { mdiCalendarToday, mdiChevronLeft, mdiChevronRight, mdiClose, mdiDownload } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import {
  benefitStatuses,
  gapColor,
  monthName,
  reimbursers,
  reimburserTitle,
  unreimbursed,
  type Reimburser,
} from '@/utils/family'
import { formatAmount, formatDate, formatSignedAmount } from '@/utils/format'

/** Suivis du foyer : aides CAF et CMG (P1), frais de garde (P2), santé (P4), sur une année civile. */
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

type Tab = 'aides' | 'garde' | 'sante'
const tabs: { value: Tab; title: string }[] = [
  { value: 'aides', title: 'Aides' },
  { value: 'garde', title: 'Garde' },
  { value: 'sante', title: 'Santé' },
]

const currentYear = new Date().getFullYear()
const year = computed(() => Number(route.params.year) || currentYear)
const tab = computed<Tab>({
  get: () => (tabs.some((t) => t.value === route.query.onglet) ? (route.query.onglet as Tab) : 'aides'),
  set: (value) => router.replace({ query: { ...route.query, onglet: value } }),
})

const benefits = ref<Schemas['BenefitsDto'] | null>(null)
const childcare = ref<Schemas['ChildcareDto'] | null>(null)
const health = ref<Schemas['HealthDto'] | null>(null)
const organisms = ref<Schemas['PayeeDto'][]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const showUpcoming = ref(false)
const onlyUnreimbursed = ref(false)

const benefitRows = computed(
  () => benefits.value?.rows.filter((r) => showUpcoming.value || r.status !== 'Upcoming') ?? [],
)
const healthRows = computed(() =>
  onlyUnreimbursed.value ? unreimbursed(health.value?.rows ?? []) : (health.value?.rows ?? []),
)

async function load() {
  loading.value = true
  error.value = null
  try {
    if (tab.value === 'aides') {
      benefits.value = await unwrap(
        api.GET('/api/family/benefits/{year}', { params: { path: { year: year.value } } }),
      )
    } else if (tab.value === 'garde') {
      childcare.value = await unwrap(
        api.GET('/api/family/childcare/{year}', { params: { path: { year: year.value } } }),
      )
    } else {
      const [report, payees] = await Promise.all([
        unwrap(
          api.GET('/api/family/health', {
            params: { query: { from: `${year.value}-01-01`, to: `${year.value}-12-31` } },
          }),
        ),
        unwrap(api.GET('/api/payees', { params: { query: { reimbursers: true, limit: 100 } } })),
      ])
      health.value = report
      organisms.value = payees
    }
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

function goTo(target: number | null) {
  router.push({
    name: 'family',
    params: target && target !== currentYear ? { year: target } : {},
    query: route.query,
  })
}

// Organismes de santé : un tiers désigné CPAM ou mutuelle classe ses remboursements liés.
const payeeSearch = ref('')
const payeeOptions = ref<Schemas['PayeeDto'][]>([])
const newOrganism = ref<string | null>(null)
const newKind = ref<Reimburser>('PrimaryInsurance')
const savingOrganism = ref(false)

watch(payeeSearch, async (search) => {
  if (!search || search.length < 2) return
  try {
    payeeOptions.value = await unwrap(api.GET('/api/payees', { params: { query: { search, limit: 10 } } }))
  } catch {
    payeeOptions.value = []
  }
})

async function setReimburser(id: string, reimburser: Reimburser) {
  savingOrganism.value = true
  error.value = null
  try {
    await unwrap(api.PUT('/api/payees/{id}/reimburser', { params: { path: { id } }, body: { reimburser } }))
    newOrganism.value = null
    await load()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    savingOrganism.value = false
  }
}

watch([year, tab], load, { immediate: true })
</script>

<template>
  <div class="d-flex align-center flex-wrap ga-2 mb-4">
    <h1 class="text-h5 font-weight-bold mr-auto">Famille</h1>
    <div class="d-flex align-center">
      <v-btn :icon="mdiChevronLeft" variant="text" aria-label="Année précédente" @click="goTo(year - 1)" />
      <div class="text-subtitle-1 font-weight-bold text-center" style="min-width: 80px">{{ year }}</div>
      <v-btn :icon="mdiChevronRight" variant="text" aria-label="Année suivante" @click="goTo(year + 1)" />
      <v-btn
        v-if="year !== currentYear"
        :icon="mdiCalendarToday"
        variant="text"
        aria-label="Année en cours"
        @click="goTo(null)"
      />
    </div>
  </div>

  <v-tabs v-model="tab" show-arrows class="mb-4">
    <v-tab v-for="t in tabs" :key="t.value" :value="t.value">{{ t.title }}</v-tab>
  </v-tabs>

  <v-progress-linear v-if="loading" indeterminate color="primary" class="mb-2" />
  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

  <!-- P1 : aides attendues contre reçues -->
  <template v-if="tab === 'aides' && benefits">
    <v-alert v-if="!benefits.rows.length" type="info" variant="tonal" class="mb-4">
      Aucune aide suivie cette année. Donnez le rôle « Aide suivie » ou « Aide à la garde » à une catégorie de
      revenus, et créez une récurrence « à valider » pour chaque versement attendu.
    </v-alert>
    <template v-else>
      <v-row class="mb-2" dense>
        <v-col v-for="c in benefits.categories" :key="c.categoryId" cols="12" sm="6" md="4">
          <v-card :data-testid="`benefit-total-${c.categoryName}`">
            <v-card-title class="text-subtitle-1">{{ c.categoryName }}</v-card-title>
            <v-card-text>
              <div class="d-flex justify-space-between">
                <span>Attendu</span><span>{{ formatAmount(c.expected) }}</span>
              </div>
              <div class="d-flex justify-space-between">
                <span>Reçu</span><span>{{ formatAmount(c.received) }}</span>
              </div>
              <div class="d-flex justify-space-between font-weight-bold">
                <span>Écart</span>
                <span :class="gapColor(c.gap) ? `text-${gapColor(c.gap)}` : ''">
                  {{ formatSignedAmount(c.gap) }}
                </span>
              </div>
              <div v-if="c.upcoming" class="d-flex justify-space-between text-medium-emphasis">
                <span>À venir</span><span>{{ formatAmount(c.upcoming) }}</span>
              </div>
            </v-card-text>
          </v-card>
        </v-col>
      </v-row>

      <v-card title="Versements">
        <template #append>
          <v-switch
            v-model="showUpcoming"
            label="Afficher l'à venir"
            color="primary"
            hide-details
            density="compact"
          />
        </template>
        <v-table density="comfortable">
          <thead>
            <tr>
              <th>Date</th>
              <th>Aide</th>
              <th>Statut</th>
              <th class="text-right">Attendu</th>
              <th class="text-right">Reçu</th>
              <th class="text-right">Écart</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="r in benefitRows"
              :key="`${r.occurrenceId ?? r.transactionId ?? r.recurrenceId}-${r.date}`"
              :data-testid="`benefit-${r.label}-${r.date}`"
            >
              <td class="text-no-wrap">{{ formatDate(r.date) }}</td>
              <td>
                {{ r.label }}
                <div class="text-caption text-medium-emphasis">{{ r.categoryName }}</div>
              </td>
              <td>
                <v-chip :color="benefitStatuses[r.status].color" size="small" variant="tonal">
                  {{ benefitStatuses[r.status].title }}
                </v-chip>
              </td>
              <td class="text-right text-no-wrap">
                {{ r.status === 'Unscheduled' ? '—' : formatAmount(r.expected) }}
              </td>
              <td class="text-right text-no-wrap">
                {{ r.received == null ? '—' : formatAmount(r.received) }}
              </td>
              <td class="text-right text-no-wrap" :class="gapColor(r.gap) ? `text-${gapColor(r.gap)}` : ''">
                {{ r.gap == null ? '—' : formatSignedAmount(r.gap) }}
              </td>
            </tr>
          </tbody>
        </v-table>
      </v-card>
    </template>
  </template>

  <!-- P2 : frais de garde de l'année -->
  <template v-if="tab === 'garde' && childcare">
    <v-card class="mb-4" title="Pour la déclaration d'impôts">
      <template #append>
        <v-btn
          :href="`/api/family/childcare/${year}/export`"
          download
          variant="tonal"
          color="primary"
          :prepend-icon="mdiDownload"
        >
          CSV
        </v-btn>
      </template>
      <v-card-text>
        <div class="d-flex justify-space-between">
          <span>Frais de garde payés</span><span>{{ formatAmount(childcare.paid) }}</span>
        </div>
        <div class="d-flex justify-space-between">
          <span>Aides à la garde reçues</span><span>− {{ formatAmount(childcare.aid) }}</span>
        </div>
        <div class="d-flex justify-space-between text-h6 mt-2" data-testid="childcare-declarable">
          <span>Montant déclarable</span><span>{{ formatAmount(childcare.declarable) }}</span>
        </div>
        <div class="text-caption text-medium-emphasis mt-2">
          Frais de l'année civile, moins les aides reçues pour la garde (CMG). Chaque opération compte dans
          son « mois concerné » s'il est renseigné (salaire de novembre payé en décembre), sinon dans le mois
          de sa date. Vérifiez le montant avec votre attestation avant de le reporter.
        </div>
      </v-card-text>
    </v-card>

    <v-row dense>
      <v-col cols="12" md="7">
        <v-card title="Par mois">
          <v-table density="compact">
            <thead>
              <tr>
                <th>Mois</th>
                <th class="text-right">Payé</th>
                <th class="text-right">Aides</th>
                <th class="text-right">Reste</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="m in childcare.months" :key="m.month">
                <td>{{ monthName(m.month) }}</td>
                <td class="text-right text-no-wrap">{{ formatAmount(m.paid) }}</td>
                <td class="text-right text-no-wrap">{{ formatAmount(m.aid) }}</td>
                <td class="text-right text-no-wrap">{{ formatAmount(m.net) }}</td>
              </tr>
            </tbody>
          </v-table>
        </v-card>
      </v-col>
      <v-col cols="12" md="5">
        <v-card title="Par tiers">
          <v-list v-if="childcare.payees.length">
            <v-list-item v-for="p in childcare.payees" :key="p.payeeId ?? 'none'" :title="p.name">
              <template #append>{{ formatAmount(p.paid) }}</template>
            </v-list-item>
          </v-list>
          <v-card-text v-else class="text-medium-emphasis">
            Aucun frais de garde cette année. Donnez le rôle « Frais de garde » à la catégorie de la nounou ou
            de la crèche.
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
  </template>

  <!-- P4 : santé, remboursements et reste à charge -->
  <template v-if="tab === 'sante' && health">
    <v-row class="mb-2" dense>
      <v-col
        v-for="kpi in [
          { title: 'Dépenses', value: health.expense },
          { title: 'CPAM', value: health.primaryInsurance },
          { title: 'Mutuelle', value: health.mutual },
          { title: 'Reste à charge', value: health.remaining },
        ]"
        :key="kpi.title"
        cols="6"
        md="3"
      >
        <v-card>
          <v-card-text>
            <div class="text-caption text-medium-emphasis">{{ kpi.title }}</div>
            <div class="text-h6" :data-testid="`health-${kpi.title}`">{{ formatAmount(kpi.value) }}</div>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>
    <v-alert v-if="health.other" type="info" variant="tonal" density="compact" class="mb-2">
      {{ formatAmount(health.other) }} de remboursements viennent d'un tiers qui n'est désigné ni CPAM ni
      mutuelle.
    </v-alert>
    <v-alert v-if="health.unlinked" type="info" variant="tonal" density="compact" class="mb-2">
      {{ formatAmount(health.unlinked) }} de remboursements classés en santé ne sont liés à aucune dépense :
      liez-les depuis l'opération pour les voir ici.
    </v-alert>

    <v-card class="mb-4" title="Dépenses de santé">
      <template #append>
        <v-switch
          v-model="onlyUnreimbursed"
          label="Non remboursées"
          color="primary"
          hide-details
          density="compact"
        />
      </template>
      <v-table density="comfortable">
        <thead>
          <tr>
            <th>Date</th>
            <th>Dépense</th>
            <th class="text-right">Montant</th>
            <th class="text-right">CPAM</th>
            <th class="text-right">Mutuelle</th>
            <th class="text-right">Reste à charge</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="r in healthRows"
            :key="r.transactionId"
            :data-testid="`health-row-${r.payee ?? r.label}`"
          >
            <td class="text-no-wrap">{{ formatDate(r.date) }}</td>
            <td>
              {{ r.payee ?? r.label ?? 'Sans tiers' }}
              <div class="text-caption text-medium-emphasis">{{ r.categoryName }}</div>
            </td>
            <td class="text-right text-no-wrap">{{ formatAmount(r.expense) }}</td>
            <td class="text-right text-no-wrap">{{ formatAmount(r.primaryInsurance) }}</td>
            <td class="text-right text-no-wrap">
              {{ formatAmount(r.mutual) }}
              <div v-if="r.other" class="text-caption text-medium-emphasis">
                + {{ formatAmount(r.other) }} autre
              </div>
            </td>
            <td class="text-right text-no-wrap font-weight-bold">{{ formatAmount(r.remaining) }}</td>
          </tr>
          <tr v-if="!healthRows.length">
            <td colspan="6" class="text-medium-emphasis">Aucune dépense de santé.</td>
          </tr>
        </tbody>
      </v-table>
    </v-card>

    <v-card
      title="Organismes de santé"
      subtitle="Leurs remboursements liés à une dépense sont classés à part."
    >
      <v-list>
        <v-list-item
          v-for="o in organisms"
          :key="o.id"
          :title="o.name"
          :subtitle="reimburserTitle(o.reimburser)"
        >
          <template v-if="auth.canWrite" #append>
            <v-btn
              :icon="mdiClose"
              variant="text"
              size="small"
              :aria-label="`Retirer ${o.name}`"
              :loading="savingOrganism"
              @click="setReimburser(o.id, 'None')"
            />
          </template>
        </v-list-item>
        <v-list-item v-if="!organisms.length" class="text-medium-emphasis">
          Aucun organisme désigné.
        </v-list-item>
      </v-list>
      <v-card-text v-if="auth.canWrite" class="d-flex flex-wrap ga-2 align-center">
        <v-autocomplete
          v-model="newOrganism"
          v-model:search="payeeSearch"
          :items="payeeOptions"
          item-title="name"
          item-value="id"
          label="Tiers"
          density="compact"
          hide-details
          no-filter
          style="min-width: 200px"
          data-testid="organism-payee"
        />
        <v-select
          v-model="newKind"
          :items="reimbursers"
          item-title="title"
          item-value="value"
          label="Organisme"
          density="compact"
          hide-details
          style="max-width: 160px"
        />
        <v-btn
          color="primary"
          :disabled="!newOrganism"
          :loading="savingOrganism"
          @click="newOrganism && setReimburser(newOrganism, newKind)"
        >
          Désigner
        </v-btn>
      </v-card-text>
    </v-card>
  </template>
</template>
