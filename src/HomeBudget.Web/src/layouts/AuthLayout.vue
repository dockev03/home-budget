<template>
  <v-main class="auth-main">
    <v-container class="fill-height justify-center">
      <div class="auth-box">
        <div class="d-flex align-center justify-center mb-6 ga-3">
          <img src="/favicon.svg" alt="" width="40" height="40" />
          <span class="text-h5 font-weight-bold">Home Budget</span>
        </div>
        <slot />
      </div>
    </v-container>
  </v-main>
</template>

<style scoped>
.auth-box {
  width: 100%;
  max-width: 440px;
}
</style>
