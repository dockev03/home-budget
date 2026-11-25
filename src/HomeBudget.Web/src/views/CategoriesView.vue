<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { mdiPlus } from '@mdi/js'
import CategoryDialog from '@/components/CategoryDialog.vue'
import LearnedRulesCard from '@/components/LearnedRulesCard.vue'
import { useAuthStore } from '@/stores/auth'
import { useReferenceStore, type Category } from '@/stores/reference'
import { iconPath } from '@/utils/icons'
import { categoryKinds, type CategoryKind } from '@/utils/labels'

const reference = useReferenceStore()
const auth = useAuthStore()
const tab = ref<CategoryKind>('Variable')
const showArchived = ref(false)
const dialog = ref(false)
const editing = ref<Category | null>(null)
const defaultParentId = ref<string | null>(null)
const error = ref<string | null>(null)

const visible = computed(() =>
  reference.categories.filter((c) => c.kind === tab.value && (showArchived.value || !c.isArchived)),
)
const parents = computed(() => visible.value.filter((c) => !c.parentId))
const childrenOf = (id: string) => visible.value.filter((c) => c.parentId === id)

function edit(category: Category | null, parentId: string | null = null) {
  editing.value = category
  defaultParentId.value = parentId
  dialog.value = true
}

async function reload() {
  await reference.loadCategories()
}

onMounted(() => reference.load(true).catch(() => (error.value = 'Chargement impossible.')))
</script>

<template>
  <div class="d-flex align-center mb-4">
    <h1 class="text-h5 font-weight-bold">Catégories</h1>
    <v-spacer />
    <v-btn v-if="auth.canWrite" color="primary" :prepend-icon="mdiPlus" @click="edit(null)"
      >Nouvelle catégorie</v-btn
    >
  </div>

  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

  <v-tabs v-model="tab" show-arrows class="mb-4">
    <v-tab v-for="kind in categoryKinds" :key="kind.value" :value="kind.value">{{ kind.title }}</v-tab>
  </v-tabs>

  <v-card>
    <v-list>
      <template v-for="parent in parents" :key="parent.id">
        <v-list-item
          :title="parent.name"
          :class="{ 'text-disabled': parent.isArchived }"
          :disabled="!auth.canWrite"
          @click="edit(parent)"
        >
          <template #prepend>
            <v-avatar :color="parent.color ?? 'primary'" variant="tonal" size="36">
              <v-icon :icon="iconPath(parent.icon)" size="20" />
            </v-avatar>
          </template>
          <template #append>
            <v-chip v-if="parent.isArchived" size="x-small" class="mr-2">Archivée</v-chip>
            <v-btn
              v-if="auth.canWrite && !parent.isArchived"
              :icon="mdiPlus"
              variant="text"
              size="small"
              :aria-label="`Ajouter une sous-catégorie à ${parent.name}`"
              @click.stop="edit(null, parent.id)"
            />
          </template>
        </v-list-item>
        <v-list-item
          v-for="child in childrenOf(parent.id)"
          :key="child.id"
          :title="child.name"
          class="pl-12"
          :class="{ 'text-disabled': child.isArchived }"
          :disabled="!auth.canWrite"
          @click="edit(child)"
        >
          <template #prepend>
            <v-icon
              :icon="iconPath(child.icon ?? parent.icon)"
              :color="child.color ?? parent.color ?? undefined"
              size="20"
            />
          </template>
        </v-list-item>
      </template>
      <v-list-item
        v-if="parents.length === 0"
        title="Aucune catégorie de ce type."
        class="text-medium-emphasis"
      />
    </v-list>
  </v-card>

  <v-switch
    v-model="showArchived"
    label="Afficher les catégories archivées"
    color="primary"
    hide-details
    class="mt-2"
  />

  <LearnedRulesCard />

  <CategoryDialog
    v-model="dialog"
    :category="editing"
    :default-kind="tab"
    :default-parent-id="defaultParentId"
    @saved="reload"
  />
</template>
