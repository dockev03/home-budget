<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import AppLayout from '@/layouts/AppLayout.vue'
import AuthLayout from '@/layouts/AuthLayout.vue'
import { useThemePreference } from '@/composables/useThemePreference'

const route = useRoute()
const layout = computed(() => (route.meta.public ? AuthLayout : AppLayout))
useThemePreference()
</script>

<template>
  <v-app>
    <component :is="layout">
      <router-view />
    </component>
  </v-app>
</template>
