<script setup lang="ts">
import { ref, watch } from 'vue'
import { api } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import type { Account } from '@/stores/reference'
import { accountTypes, type AccountType } from '@/utils/labels'
import { parseAmount, todayIso } from '@/utils/format'
import { required } from '@/utils/rules'
import AppearancePicker from './AppearancePicker.vue'

const props = defineProps<{ account: Account | null }>()
const visible = defineModel<boolean>({ required: true })
const emit = defineEmits<{ saved: [] }>()

const form = ref({
  name: '',
  type: 'Checking' as AccountType,
  initialBalance: '0',
  initialDate: todayIso(),
  icon: null as string | null,
  color: null as string | null,
  includeInForecast: true,
  includeInNetWorth: true,
  overdraftThreshold: '0',
  isArchived: false,
  isPrivate: false,
  isEmergencyFund: false,
})
const valid = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)

const amountRule = (v: string) => parseAmount(v) !== null || 'Montant invalide (2 décimales au plus).'

watch(visible, (open) => {
  if (!open) return
  error.value = null
  const a = props.account
  form.value = a
    ? {
        name: a.name,
        type: a.type,
        initialBalance: String(a.initialBalance).replace('.', ','),
        initialDate: a.initialDate,
        icon: a.icon,
        color: a.color,
        includeInForecast: a.includeInForecast,
        includeInNetWorth: a.includeInNetWorth,
        overdraftThreshold: String(a.overdraftThreshold).replace('.', ','),
        isArchived: a.isArchived,
        isPrivate: a.isPrivate,
        isEmergencyFund: a.isEmergencyFund,
      }
    : {
        name: '',
        type: 'Checking',
        initialBalance: '0',
        initialDate: todayIso(),
        icon: 'wallet',
        color: null,
        includeInForecast: true,
        includeInNetWorth: true,
        overdraftThreshold: '0',
        isArchived: false,
        isPrivate: false,
        isEmergencyFund: false,
      }
})

// Par défaut, seuls les comptes courants entrent dans la prévision, et seuls les comptes d'épargne
// dans l'épargne de précaution.
watch(
  () => form.value.type,
  (type) => {
    if (props.account) return
    form.value.includeInForecast = type === 'Checking'
    form.value.isEmergencyFund = type === 'Savings'
  },
)

async function save() {
  if (!valid.value) return
  saving.value = true
  error.value = null
  const f = form.value
  const body = {
    name: f.name,
    type: f.type,
    initialBalance: parseAmount(f.initialBalance) ?? 0,
    initialDate: f.initialDate,
    icon: f.icon,
    color: f.color,
    includeInForecast: f.includeInForecast,
    includeInNetWorth: f.includeInNetWorth,
    overdraftThreshold: parseAmount(f.overdraftThreshold) ?? 0,
    isArchived: f.isArchived,
    sortOrder: null,
    isPrivate: f.isPrivate,
    isEmergencyFund: f.isEmergencyFund,
  }
  try {
    if (props.account) {
      await unwrap(
        api.PUT('/api/accounts/{id}', {
          params: { path: { id: props.account.id } },
          headers: ifMatch(props.account.version),
          body,
        }),
      )
    } else {
      await unwrap(api.POST('/api/accounts', { body }))
    }
    emit('saved')
    visible.value = false
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <v-dialog v-model="visible" max-width="560" scrollable>
    <v-card :title="account ? 'Modifier le compte' : 'Nouveau compte'">
      <v-card-text>
        <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
        <v-form v-model="valid" @submit.prevent="save">
          <v-text-field v-model="form.name" label="Nom" :rules="[required]" maxlength="100" autofocus />
          <v-select v-model="form.type" :items="accountTypes" label="Type" />
          <v-row dense>
            <v-col cols="12" sm="6">
              <v-text-field
                v-model="form.initialBalance"
                label="Solde initial"
                suffix="€"
                inputmode="decimal"
                :rules="[amountRule]"
              />
            </v-col>
            <v-col cols="12" sm="6">
              <v-text-field v-model="form.initialDate" label="À la date du" type="date" :rules="[required]" />
            </v-col>
          </v-row>
          <AppearancePicker v-model:icon="form.icon" v-model:color="form.color" />
          <v-text-field
            v-model="form.overdraftThreshold"
            label="Seuil d'alerte"
            suffix="€"
            inputmode="decimal"
            hint="Alerte quand le solde prévu passe sous ce montant."
            persistent-hint
            :rules="[amountRule]"
            class="mb-2"
          />
          <v-switch
            v-model="form.includeInForecast"
            label="Inclus dans la prévision"
            color="primary"
            hide-details
          />
          <v-switch
            v-model="form.includeInNetWorth"
            label="Inclus dans le patrimoine"
            color="primary"
            hide-details
          />
          <v-switch
            v-model="form.isEmergencyFund"
            label="Épargne de précaution"
            color="primary"
            hint="Compte disponible en cas de coup dur : il entre dans le nombre de mois de dépenses couverts."
            persistent-hint
            class="mb-2"
          />
          <v-switch
            v-model="form.isPrivate"
            label="Compte privé (visible de vous seul)"
            color="primary"
            hint="Ses opérations et son solde sont masqués aux autres membres du foyer, y compris dans les totaux."
            persistent-hint
            class="mb-2"
          />
          <v-switch
            v-if="account"
            v-model="form.isArchived"
            label="Archivé (masqué, historique conservé)"
            color="primary"
            hide-details
          />
          <button type="submit" hidden />
        </v-form>
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn @click="visible = false">Annuler</v-btn>
        <v-btn color="primary" variant="flat" :loading="saving" :disabled="!valid" @click="save">
          Enregistrer
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
