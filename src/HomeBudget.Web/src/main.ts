import { createApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { vuetify } from './plugins/vuetify'
import { router } from './router'
import { registerServiceWorker } from './pwa'
import { vAutofocus } from './directives/autofocus'

createApp(App).use(createPinia()).use(router).use(vuetify).directive('autofocus', vAutofocus).mount('#app')
registerServiceWorker()
