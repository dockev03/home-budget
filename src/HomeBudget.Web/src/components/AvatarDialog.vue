<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { mdiDeleteOutline, mdiImageEditOutline } from '@mdi/js'
import { api, type CurrentUser } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import { AVATAR_MAX_BYTES, avatarUrl, resizeAvatar } from '@/utils/avatar'

/** Photo de profil de l'utilisateur connecté : choisie, recadrée en carré et réduite dans le navigateur. */
const visible = defineModel<boolean>({ required: true })

const auth = useAuthStore()
const input = ref<HTMLInputElement | null>(null)
const saving = ref(false)
const error = ref<string | null>(null)

const src = computed(() => avatarUrl(auth.user?.avatarUpdatedAt))
const initial = computed(() => auth.user?.userName.charAt(0).toUpperCase() ?? '?')

watch(visible, (open) => {
  if (open) error.value = null
})

async function run(request: () => Promise<CurrentUser>) {
  saving.value = true
  error.value = null
  try {
    auth.user = await request()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

async function choose(event: Event) {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  target.value = ''
  if (!file) return
  let photo: Blob
  try {
    photo = await resizeAvatar(file)
  } catch {
    error.value = 'Cette image ne peut pas être lue par le navigateur. Essayez une photo JPEG ou PNG.'
    return
  }
  if (photo.size > AVATAR_MAX_BYTES) {
    error.value = 'La photo reste trop lourde une fois réduite.'
    return
  }
  await run(() =>
    unwrap(
      api.PUT('/api/account/avatar', {
        body: photo as unknown as string,
        bodySerializer: (body) => body as unknown as BodyInit,
        headers: { 'Content-Type': photo.type },
      }),
    ),
  )
}

const remove = () => run(() => unwrap(api.DELETE('/api/account/avatar')))
</script>

<template>
  <v-dialog v-model="visible" max-width="400">
    <v-card title="Photo de profil" data-testid="avatar-dialog">
      <v-card-text class="text-center">
        <v-alert v-if="error" type="error" variant="tonal" class="mb-4 text-left" :text="error" />
        <v-avatar color="primary" size="128" class="mb-4">
          <v-img v-if="src" :src="src" alt="Photo de profil" cover />
          <span v-else class="text-h3">{{ initial }}</span>
        </v-avatar>
        <p class="text-body-2 text-medium-emphasis">
          Affichée dans la pastille du compte. La photo est recadrée au centre et réduite avant l’envoi.
        </p>
        <input
          ref="input"
          type="file"
          accept="image/jpeg,image/png,image/webp,image/heic,image/*"
          class="d-none"
          data-testid="avatar-input"
          @change="choose"
        />
      </v-card-text>
      <v-card-actions>
        <v-btn v-if="src" :prepend-icon="mdiDeleteOutline" color="error" :disabled="saving" @click="remove">
          Retirer
        </v-btn>
        <v-spacer />
        <v-btn :disabled="saving" @click="visible = false">Fermer</v-btn>
        <v-btn
          :prepend-icon="mdiImageEditOutline"
          color="primary"
          variant="flat"
          :loading="saving"
          @click="input?.click()"
        >
          {{ src ? 'Changer' : 'Choisir' }}
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
