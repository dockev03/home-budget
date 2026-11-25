<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { mdiFileUploadOutline, mdiPencil, mdiPlus } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import ImportProfileDialog from '@/components/ImportProfileDialog.vue'
import { useAuthStore } from '@/stores/auth'
import { usePendingStore } from '@/stores/pending'
import { useReferenceStore } from '@/stores/reference'
import { formatDate, formatSignedAmount } from '@/utils/format'
import {
  defaultSelection,
  importStatuses,
  isSelectable,
  maxImportBytes,
  needsProfile,
  selectionSummary,
  toBase64,
  type ImportLine,
} from '@/utils/imports'

type Profile = Schemas['ImportProfileDto']
type Preview = Schemas['ImportPreviewDto']
type Batch = Schemas['ImportBatchDto']

const auth = useAuthStore()
const reference = useReferenceStore()
const pending = usePendingStore()

const accountId = ref<string | null>(null)
const file = ref<File | File[] | null>(null)
const profileId = ref<string | null>(null)
const profiles = ref<Profile[]>([])
const history = ref<Batch[]>([])

const content = ref<string | null>(null)
const fileName = ref('')
const preview = ref<Preview | null>(null)
const selected = ref<number[]>([])
const acceptMatch = ref<Set<number>>(new Set())

const analyzing = ref(false)
const importing = ref(false)
const error = ref<string | null>(null)
const success = ref<string | null>(null)
const profileDialog = ref(false)
const editingProfile = ref<Profile | null>(null)

const accountItems = computed(() => reference.activeAccounts.map((a) => ({ value: a.id, title: a.name })))
const profileItems = computed(() => profiles.value.map((p) => ({ value: p.id, title: p.name })))
const selectedFile = computed(() => (Array.isArray(file.value) ? (file.value[0] ?? null) : file.value))
const showProfile = computed(() => !!selectedFile.value && needsProfile(selectedFile.value.name))
const canAnalyze = computed(
  () => !!accountId.value && !!selectedFile.value && (!showProfile.value || !!profileId.value),
)
const lines = computed(() => preview.value?.lines ?? [])
const counts = computed(() => {
  const result: Record<string, number> = {}
  for (const l of lines.value) result[l.status] = (result[l.status] ?? 0) + 1
  return result
})
const summary = computed(() => selectionSummary(lines.value, selected.value, acceptMatch.value))

// Un autre fichier, compte ou profil : l'aperçu n'est plus valable.
watch([accountId, selectedFile, profileId], () => {
  preview.value = null
  content.value = null
})

const accountName = (id: string) => reference.accountById.get(id)?.name ?? 'Compte inconnu'

function detail(l: ImportLine): string | null {
  if (l.status === 'Match' && l.occurrence) {
    return `Remplace l’échéance « ${l.occurrence.label} » du ${formatDate(l.occurrence.date)} (${formatSignedAmount(l.occurrence.amount)})`
  }
  if ((l.status === 'ProbableDuplicate' || l.status === 'Duplicate') && l.duplicateOfDate) {
    const what = l.duplicateOfLabel ? `« ${l.duplicateOfLabel} »` : 'une opération'
    return `${l.status === 'Duplicate' ? 'Déjà importée' : 'Ressemble à'} ${what} du ${formatDate(l.duplicateOfDate)}`
  }
  return null
}

function toggleMatch(line: number, value: boolean | null) {
  const next = new Set(acceptMatch.value)
  if (value) next.add(line)
  else next.delete(line)
  acceptMatch.value = next
}

async function loadSide() {
  const [list, batches] = await Promise.all([
    unwrap(api.GET('/api/import-profiles')),
    unwrap(api.GET('/api/imports')),
  ])
  profiles.value = list
  history.value = batches
  if (!profileId.value && list.length) profileId.value = list[0]!.id
}

async function analyze() {
  const f = selectedFile.value
  if (!f || !accountId.value) return
  error.value = null
  success.value = null
  if (f.size > maxImportBytes) {
    error.value = 'Fichier trop volumineux (5 Mio au plus).'
    return
  }
  analyzing.value = true
  try {
    content.value = toBase64(new Uint8Array(await f.arrayBuffer()))
    fileName.value = f.name
    const result = await unwrap(
      api.POST('/api/imports/preview', {
        body: {
          accountId: accountId.value,
          fileName: f.name,
          content: content.value,
          profileId: showProfile.value ? profileId.value : null,
        },
      }),
    )
    preview.value = result
    selected.value = defaultSelection(result.lines)
    acceptMatch.value = new Set(result.lines.filter((l) => l.status === 'Match').map((l) => l.line))
  } catch (e) {
    preview.value = null
    error.value = e instanceof ApiError ? e.message : 'Lecture du fichier impossible.'
  } finally {
    analyzing.value = false
  }
}

async function commit() {
  if (!preview.value || !content.value || !accountId.value) return
  importing.value = true
  error.value = null
  const chosen = new Set(selected.value)
  try {
    const result = await unwrap(
      api.POST('/api/imports', {
        body: {
          accountId: accountId.value,
          fileName: fileName.value,
          content: content.value,
          profileId: showProfile.value ? profileId.value : null,
          lines: lines.value
            .filter((l) => chosen.has(l.line) && isSelectable(l))
            .map((l) => ({ line: l.line, acceptMatch: acceptMatch.value.has(l.line) })),
        },
      }),
    )
    success.value =
      `${result.created} opération(s) importée(s)` +
      (result.matched ? `, dont ${result.matched} échéance(s) remplacée(s)` : '') +
      (result.duplicates ? ` ; ${result.duplicates} doublon(s) écarté(s).` : '.')
    preview.value = null
    content.value = null
    file.value = null
    await Promise.all([reference.loadAccounts(), loadSide(), pending.load()])
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Import impossible.'
  } finally {
    importing.value = false
  }
}

function editProfile(profile: Profile | null) {
  editingProfile.value = profile
  profileDialog.value = true
}

async function profileSaved(profile: Profile | null) {
  await loadSide()
  profileId.value = profile?.id ?? profiles.value[0]?.id ?? null
}

onMounted(async () => {
  try {
    await reference.load()
    accountId.value = reference.activeAccounts[0]?.id ?? null
    await loadSide()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
})
</script>

<template>
  <h1 class="text-h5 font-weight-bold mb-4">Importer un relevé</h1>

  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
  <v-alert v-if="success" type="success" variant="tonal" class="mb-4" closable @click:close="success = null">
    {{ success }}
    <template #append>
      <v-btn variant="text" :to="{ name: 'transactions', query: { accountId } }">Voir les opérations</v-btn>
    </template>
  </v-alert>

  <v-card class="mb-4">
    <v-card-text>
      <v-row dense>
        <v-col cols="12" md="4">
          <v-select v-model="accountId" :items="accountItems" label="Compte" :disabled="!auth.canWrite" />
        </v-col>
        <v-col cols="12" md="4">
          <v-file-input
            v-model="file"
            label="Relevé (OFX, CSV, TSV ou QIF)"
            accept=".ofx,.csv,.tsv,.txt,.qif"
            :prepend-icon="mdiFileUploadOutline"
            show-size
            :disabled="!auth.canWrite"
          />
        </v-col>
        <v-col v-if="showProfile" cols="12" md="4">
          <v-select v-model="profileId" :items="profileItems" label="Profil de lecture CSV">
            <template #append>
              <v-btn
                :icon="mdiPencil"
                variant="text"
                size="small"
                aria-label="Modifier le profil"
                :disabled="!profileId"
                @click="editProfile(profiles.find((p) => p.id === profileId) ?? null)"
              />
              <v-btn
                :icon="mdiPlus"
                variant="text"
                size="small"
                aria-label="Nouveau profil"
                @click="editProfile(null)"
              />
            </template>
          </v-select>
        </v-col>
      </v-row>
      <p class="text-caption text-medium-emphasis">
        L’OFX est conseillé : chaque opération y porte un identifiant de la banque, ce qui écarte les doublons
        à coup sûr. Rien n’est enregistré avant la confirmation.
      </p>
    </v-card-text>
    <v-card-actions>
      <v-spacer />
      <v-btn color="primary" variant="flat" :loading="analyzing" :disabled="!canAnalyze" @click="analyze"
        >Analyser</v-btn
      >
    </v-card-actions>
  </v-card>

  <template v-if="preview">
    <div class="d-flex flex-wrap ga-2 mb-2">
      <v-chip v-for="(info, status) in importStatuses" :key="status" :color="info.color" size="small">
        {{ info.title }} : {{ counts[status] ?? 0 }}
      </v-chip>
      <v-chip v-if="preview.errors.length" color="error" size="small"
        >Lignes illisibles : {{ preview.errors.length }}</v-chip
      >
    </div>

    <v-expansion-panels v-if="preview.errors.length" class="mb-4">
      <v-expansion-panel title="Lignes non importées">
        <v-expansion-panel-text>
          <ul class="text-body-2 pl-4">
            <li v-for="e in preview.errors" :key="`${e.line}-${e.message}`">
              <template v-if="e.line">Ligne {{ e.line }} : </template>{{ e.message }}
            </li>
          </ul>
        </v-expansion-panel-text>
      </v-expansion-panel>
    </v-expansion-panels>

    <v-card class="mb-4">
      <v-list v-if="lines.length" lines="three" density="compact">
        <v-list-item v-for="l in lines" :key="l.line" :disabled="!isSelectable(l)">
          <template #prepend>
            <v-checkbox-btn
              v-model="selected"
              :value="l.line"
              :disabled="!isSelectable(l)"
              :aria-label="`Importer ${l.label}`"
            />
          </template>
          <v-list-item-title class="text-wrap">{{ l.label || '(sans libellé)' }}</v-list-item-title>
          <v-list-item-subtitle>
            {{ formatDate(l.date) }}
            <v-chip :color="importStatuses[l.status].color" size="x-small" class="ml-1">
              {{ importStatuses[l.status].title }}
            </v-chip>
          </v-list-item-subtitle>
          <div v-if="detail(l)" class="text-caption text-medium-emphasis">{{ detail(l) }}</div>
          <v-switch
            v-if="l.status === 'Match'"
            :model-value="acceptMatch.has(l.line)"
            label="Remplacer l’échéance"
            color="success"
            density="compact"
            hide-details
            @update:model-value="(v) => toggleMatch(l.line, v)"
          />
          <template #append>
            <span class="font-weight-medium" :class="l.amount > 0 ? 'text-success' : ''">
              {{ formatSignedAmount(l.amount) }}
            </span>
          </template>
        </v-list-item>
      </v-list>
      <v-card-text v-else class="text-medium-emphasis"
        >Aucune opération à importer dans ce fichier.</v-card-text
      >
      <v-card-actions>
        <span class="text-body-2 ml-2">
          {{ summary.count }} opération(s) cochée(s) · {{ formatSignedAmount(summary.total) }}
        </span>
        <v-spacer />
        <v-btn @click="preview = null">Annuler</v-btn>
        <v-btn
          color="primary"
          variant="flat"
          :loading="importing"
          :disabled="summary.count === 0"
          @click="commit"
          >Importer {{ summary.count }} opération(s)</v-btn
        >
      </v-card-actions>
    </v-card>
  </template>

  <v-card v-if="history.length">
    <v-card-title class="text-subtitle-1">Derniers imports</v-card-title>
    <v-list density="compact">
      <v-list-item
        v-for="b in history"
        :key="b.id"
        :title="`${b.fileName} → ${accountName(b.accountId)}`"
        :subtitle="`${formatDate(b.importedAt.slice(0, 10))} · ${b.created} importée(s), dont ${b.matched} échéance(s) · ${b.duplicates} doublon(s)`"
      />
    </v-list>
  </v-card>

  <ImportProfileDialog v-model="profileDialog" :profile="editingProfile" @saved="profileSaved" />
</template>
