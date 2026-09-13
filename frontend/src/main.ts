import { createApp } from 'vue'
import App from './App.vue'
import { startMocksIfEnabled } from './mocks'
import { router } from './router'
import './styles.css'

async function bootstrap() {
  await startMocksIfEnabled()
  createApp(App).use(router).mount('#app')
}

void bootstrap()
