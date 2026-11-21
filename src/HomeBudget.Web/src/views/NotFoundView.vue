<template>
  <v-card class="text-center pa-6">
    <v-card-title class="text-h5">Page introuvable</v-card-title>
    <v-card-text>Cette adresse ne correspond à aucune page.</v-card-text>
    <v-card-actions class="justify-center">
      <v-btn color="primary" :to="{ name: 'home' }">Retour à l'accueil</v-btn>
    </v-card-actions>
  </v-card>
</template>
