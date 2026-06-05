import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const collector = 'http://localhost:5005'

export default defineConfig({
  plugins: [react()],
  build: {
    // built into the collector so it can serve the UI itself
    outDir: '../src/Seerlens.Collector/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': collector,
      '/events': collector,
      '/ingest': collector,
    },
  },
})
