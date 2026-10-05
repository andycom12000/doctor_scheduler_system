/// <reference types="vite/client" />

/** 由 vite.config.ts 的 define 注入，來源是 Directory.Build.props 的 <Version>。 */
declare const __APP_VERSION__: string

declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<{}, {}, any>
  export default component
}
