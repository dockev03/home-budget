<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { mdiDeleteOutline, mdiDownload, mdiFileDocumentOutline, mdiPaperclip } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import {
  ATTACHMENT_ACCEPT,
  attachmentContentUrl,
  attachmentTooLarge,
  formatFileSize,
  isPreviewable,
} from '@/utils/attachments'

/** Pièces jointes d'une opération (B7) : photo d'un ticket, facture, fiche de paie. */
const props = defineProps<{ transactionId: string; canWrite: boolean }>()
const emit = defineEmits<{ changed: [count: number] }>()

const items = ref<Schemas['AttachmentDto'][]>([])
const loading = ref(false)
const uploading = ref(false)
const error = ref<string | null>(null)
const confirmId = ref<string | null>(null)
const input = ref<HTMLInputElement | null>(null)

async function load() {
  loading.value = true
  error.value = null
  try {
    items.value = await unwrap(
      api.GET('/api/transactions/{transactionId}/attachments', {
        params: { path: { transactionId: props.transactionId } },
      }),
    )
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

async function upload(event: Event) {
  const files = Array.from((event.target as HTMLInputElement).files ?? [])
  ;(event.target as HTMLInputElement).value = ''
  if (files.length === 0) return
  error.value = null
  uploading.value = true
  try {
    for (const file of files) {
      if (attachmentTooLarge(file)) {
        error.value = `« ${file.name} » dépasse 10 Mo.`
        continue
      }
      // Le fichier part tel quel : le serveur reconnaît son type sur ses premiers octets.
      const created = await unwrap(
        api.POST('/api/transactions/{transactionId}/attachments', {
          params: { path: { transactionId: props.transactionId }, query: { fileName: file.name } },
          body: file as unknown as string,
          bodySerializer: (body) => body as unknown as BodyInit,
          headers: { 'Content-Type': file.type || 'application/octet-stream' },
        }),
      )
      items.value.push(created)
    }
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Envoi impossible.'
  } finally {
    uploading.value = false
    emit('changed', items.value.length)
  }
}

async function remove(id: string) {
  error.value = null
  try {
    await unwrap(api.DELETE('/api/attachments/{id}', { params: { path: { id } } }))
    items.value = items.value.filter((a) => a.id !== id)
    emit('changed', items.value.length)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  } finally {
    confirmId.value = null
  }
}

onMounted(load)
watch(() => props.transactionId, load)
</script>

<template>
  <div class="mt-3" data-testid="attachments">
    <div class="d-flex align-center">
      <span class="text-subtitle-2">Pièces jointes</span>
      <v-spacer />
      <template v-if="canWrite">
        <input
          ref="input"
          type="file"
          multiple
          :accept="ATTACHMENT_ACCEPT"
          class="d-none"
          data-testid="attachment-input"
          @change="upload"
        />
        <v-btn
          :prepend-icon="mdiPaperclip"
          variant="text"
          size="small"
          :loading="uploading"
          @click="input?.click()"
          >Joindre</v-btn
        >
      </template>
    </div>
    <v-progress-linear v-if="loading" indeterminate color="primary" class="my-1" />
    <v-alert v-if="error" type="error" variant="tonal" density="compact" class="my-1" :text="error" />
    <p v-if="!loading && items.length === 0" class="text-caption text-medium-emphasis">
      Aucune. Photo, ticket, facture ou fiche de paie : JPEG, PNG, WebP, HEIC ou PDF, 10 Mo au plus.
    </p>
    <v-list v-else density="compact" class="py-0">
      <v-list-item
        v-for="item in items"
        :key="item.id"
        :title="item.fileName"
        :subtitle="formatFileSize(item.size)"
        class="px-0"
      >
        <template #prepend>
          <a
            v-if="isPreviewable(item.contentType)"
            :href="attachmentContentUrl(item.id)"
            target="_blank"
            rel="noopener"
            class="mr-3"
            :aria-label="`Voir ${item.fileName}`"
          >
            <img
              :src="attachmentContentUrl(item.id)"
              alt=""
              width="48"
              height="48"
              class="rounded d-block"
              style="object-fit: cover"
            />
          </a>
          <v-icon v-else :icon="mdiFileDocumentOutline" size="32" class="mr-3" />
        </template>
        <template #append>
          <v-btn
            :icon="mdiDownload"
            :href="attachmentContentUrl(item.id, true)"
            download
            variant="text"
            size="small"
            :aria-label="`Télécharger ${item.fileName}`"
          />
          <template v-if="canWrite">
            <v-btn
              v-if="confirmId !== item.id"
              :icon="mdiDeleteOutline"
              variant="text"
              size="small"
              :aria-label="`Retirer ${item.fileName}`"
              @click="confirmId = item.id"
            />
            <v-btn v-else color="error" variant="flat" size="small" @click="remove(item.id)">Retirer</v-btn>
          </template>
        </template>
      </v-list-item>
    </v-list>
  </div>
</template>
