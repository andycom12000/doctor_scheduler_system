import { createApp } from 'vue'
import App from './App.vue'
import { startMocksIfEnabled } from './mocks'
import './styles.css'

async function bootstrap() {
  await startMocksIfEnabled()
  createApp(App).mount('#app')
}

void bootstrap()
