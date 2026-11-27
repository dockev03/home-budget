<script setup lang="ts">
import { onMounted, ref } from 'vue'
import {
  mdiAccountPlusOutline,
  mdiContentCopy,
  mdiDeleteOutline,
  mdiShieldCheck,
  mdiShieldOffOutline,
} from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import { formatDateTime, invitationLink, roles, type Role } from '@/utils/users'
import { required } from '@/utils/rules'

type User = Schemas['UserDto']
type Invitation = Schemas['InvitationDto']

/** Utilisateurs du foyer (K1, K5) : invitations, rôles et désactivation, réservés aux administrateurs. */
const auth = useAuthStore()

const users = ref<User[]>([])
const invitations = ref<Invitation[]>([])
const error = ref<string | null>(null)
const busy = ref<string | null>(null)

const dialog = ref(false)
const form = ref({ userName: '', displayName: '', role: 'Member' as Role })
const valid = ref(false)
const saving = ref(false)
const dialogError = ref<string | null>(null)
/** Lien de la dernière invitation : affiché une seule fois. */
const link = ref<string | null>(null)
const copied = ref(false)

const userNameRule = (v: string) =>
  /^[A-Za-z0-9._@-]{3,64}$/.test(v.trim()) || '3 à 64 caractères : lettres sans accent, chiffres, - . _ @'

async function guarded(id: string | null, action: () => Promise<void>) {
  busy.value = id
  error.value = null
  try {
    await action()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Opération impossible.'
    await load()
  } finally {
    busy.value = null
  }
}

async function load() {
  try {
    ;[users.value, invitations.value] = await Promise.all([
      unwrap(api.GET('/api/users')),
      unwrap(api.GET('/api/invitations')),
    ])
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
}

const isSelf = (user: User) => user.id === auth.user?.id

const changeRole = (user: User, role: Role) =>
  guarded(user.id, async () => {
    const updated = await unwrap(
      api.PUT('/api/users/{id}/role', { params: { path: { id: user.id } }, body: { role } }),
    )
    if (isSelf(user)) await auth.refreshUser()
    users.value = users.value.map((u) => (u.id === updated.id ? updated : u))
  })

const changeStatus = (user: User, isActive: boolean) =>
  guarded(user.id, async () => {
    const updated = await unwrap(
      api.PUT('/api/users/{id}/status', { params: { path: { id: user.id } }, body: { isActive } }),
    )
    users.value = users.value.map((u) => (u.id === updated.id ? updated : u))
  })

const revoke = (invitation: Invitation) =>
  guarded(invitation.id, async () => {
    await unwrap(api.DELETE('/api/invitations/{id}', { params: { path: { id: invitation.id } } }))
    invitations.value = invitations.value.filter((i) => i.id !== invitation.id)
  })

function openInvite() {
  form.value = { userName: '', displayName: '', role: 'Member' }
  dialogError.value = null
  link.value = null
  copied.value = false
  dialog.value = true
}

async function invite() {
  if (!valid.value) return
  saving.value = true
  dialogError.value = null
  try {
    const created = await unwrap(
      api.POST('/api/invitations', {
        body: {
          userName: form.value.userName.trim(),
          displayName: form.value.displayName.trim() || null,
          role: form.value.role,
        },
      }),
    )
    link.value = invitationLink(window.location.origin, created.token)
    invitations.value = [...invitations.value, created.invitation]
  } catch (e) {
    dialogError.value = e instanceof ApiError ? e.message : 'Invitation impossible.'
  } finally {
    saving.value = false
  }
}

async function copyLink() {
  if (!link.value) return
  await navigator.clipboard?.writeText(link.value)
  copied.value = true
}

onMounted(load)
</script>

<template>
  <div class="d-flex align-center mb-4 ga-2 flex-wrap">
    <h1 class="text-h5 font-weight-bold">Utilisateurs</h1>
    <v-spacer />
    <v-btn color="primary" :prepend-icon="mdiAccountPlusOutline" @click="openInvite">Inviter</v-btn>
  </div>

  <v-alert
    v-if="error"
    type="error"
    variant="tonal"
    class="mb-4"
    :text="error"
    closable
    @click:close="error = null"
  />

  <v-card class="mb-4">
    <v-list lines="two">
      <v-list-item v-for="user in users" :key="user.id" :class="{ 'text-disabled': !user.isActive }">
        <template #prepend>
          <v-icon
            :icon="user.twoFactorEnabled ? mdiShieldCheck : mdiShieldOffOutline"
            :color="user.twoFactorEnabled ? 'success' : 'warning'"
            :title="user.twoFactorEnabled ? 'Double authentification active' : 'Sans double authentification'"
          />
        </template>
        <v-list-item-title>
          {{ user.displayName || user.userName }}
          <v-chip v-if="isSelf(user)" size="x-small" class="ml-2">Vous</v-chip>
          <v-chip v-if="!user.isActive" size="x-small" color="error" class="ml-2">Désactivé</v-chip>
        </v-list-item-title>
        <v-list-item-subtitle
          >{{ user.userName }} · depuis le {{ formatDateTime(user.createdAt) }}</v-list-item-subtitle
        >
        <template #append>
          <div class="d-flex align-center ga-2 flex-wrap justify-end">
            <v-select
              :model-value="user.role"
              :items="roles"
              item-title="title"
              item-value="value"
              :aria-label="`Rôle de ${user.userName}`"
              density="compact"
              variant="outlined"
              hide-details
              :loading="busy === user.id"
              style="min-width: 170px"
              @update:model-value="(role: string) => changeRole(user, role as Role)"
            />
            <v-switch
              :model-value="user.isActive"
              :aria-label="`${user.userName} actif`"
              color="primary"
              density="compact"
              hide-details
              :disabled="isSelf(user) || busy === user.id"
              @update:model-value="(active) => changeStatus(user, !!active)"
            />
          </div>
        </template>
      </v-list-item>
    </v-list>
  </v-card>

  <v-card v-if="invitations.length">
    <v-card-title>Invitations en attente</v-card-title>
    <v-list>
      <v-list-item
        v-for="invitation in invitations"
        :key="invitation.id"
        :title="invitation.displayName || invitation.userName"
        :subtitle="`${roles.find((r) => r.value === invitation.role)?.title} · expire le ${formatDateTime(invitation.expiresAt)}`"
      >
        <template #append>
          <v-btn
            :icon="mdiDeleteOutline"
            variant="text"
            :aria-label="`Révoquer l’invitation de ${invitation.userName}`"
            :loading="busy === invitation.id"
            @click="revoke(invitation)"
          />
        </template>
      </v-list-item>
    </v-list>
  </v-card>

  <v-dialog v-model="dialog" max-width="520">
    <v-card title="Inviter un membre du foyer">
      <v-card-text v-if="link">
        <v-alert type="success" variant="tonal" class="mb-4">
          Transmettez ce lien à la personne invitée (message, en main propre). Il est valable 48 heures, ne
          sert qu'une fois et <strong>ne sera plus affiché</strong>.
        </v-alert>
        <v-text-field :model-value="link" label="Lien d'invitation" readonly data-testid="invitation-link">
          <template #append-inner>
            <v-btn
              :icon="mdiContentCopy"
              variant="text"
              size="small"
              aria-label="Copier le lien"
              @click="copyLink"
            />
          </template>
        </v-text-field>
        <div v-if="copied" class="text-caption text-success">Lien copié.</div>
      </v-card-text>
      <v-card-text v-else>
        <v-alert v-if="dialogError" type="error" variant="tonal" class="mb-4" :text="dialogError" />
        <v-form v-model="valid" @submit.prevent="invite">
          <v-text-field
            v-model="form.userName"
            label="Identifiant de connexion"
            autocomplete="off"
            :rules="[required, userNameRule]"
            v-autofocus
          />
          <v-text-field v-model="form.displayName" label="Nom affiché (facultatif)" maxlength="60" />
          <v-select v-model="form.role" :items="roles" item-title="title" item-value="value" label="Rôle">
            <template #item="{ props: itemProps, item }">
              <v-list-item v-bind="itemProps" :subtitle="item.description" />
            </template>
          </v-select>
          <p class="text-caption text-medium-emphasis">
            La personne choisira son mot de passe, puis devra activer la double authentification.
          </p>
          <button type="submit" hidden />
        </v-form>
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn @click="dialog = false">{{ link ? 'Fermer' : 'Annuler' }}</v-btn>
        <v-btn
          v-if="!link"
          color="primary"
          variant="flat"
          :loading="saving"
          :disabled="!valid"
          @click="invite"
          >Créer le lien</v-btn
        >
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
