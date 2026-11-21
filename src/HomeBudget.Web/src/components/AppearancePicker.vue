<script setup lang="ts">
import { mdiCheck } from '@mdi/js'
import { iconKeys, iconPath, palette } from '@/utils/icons'

const icon = defineModel<string | null>('icon', { default: null })
const color = defineModel<string | null>('color', { default: null })
</script>

<template>
  <v-select v-model="icon" :items="iconKeys" label="Icône" clearable :menu-props="{ maxHeight: 320 }">
    <template #selection="{ item }">
      <v-icon :icon="iconPath(item)" :color="color ?? undefined" class="mr-2" />
      {{ item }}
    </template>
    <template #item="{ props: itemProps, item }">
      <v-list-item v-bind="itemProps" :prepend-icon="iconPath(item)" />
    </template>
  </v-select>

  <div class="text-body-2 text-medium-emphasis mb-1">Couleur</div>
  <div class="d-flex flex-wrap ga-2 mb-4" role="radiogroup" aria-label="Couleur">
    <v-btn
      v-for="c in palette"
      :key="c"
      :color="c"
      icon
      size="32"
      variant="flat"
      role="radio"
      :aria-checked="color === c"
      :aria-label="`Couleur ${c}`"
      @click="color = c"
    >
      <v-icon v-if="color === c" :icon="mdiCheck" size="18" />
    </v-btn>
  </div>
</template>
