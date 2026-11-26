import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { vuetify } from './plugins/vuetify'
import { router } from './router'
import { registerServiceWorker } from './pwa'

createApp(App).use(createPinia()).use(router).use(vuetify).mount('#app')
registerServiceWorker()
