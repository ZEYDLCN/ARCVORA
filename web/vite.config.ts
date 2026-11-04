/// <reference types="vitest" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Geliştirmede /api isteklerini yerel API'ye yönlendirir (varsayılan: dotnet run ile gelen adres).
const target = process.env.VITE_API_TARGET ?? 'http://localhost:5217';

export default defineConfig({
  plugins: [react()],
  server: { port: 5173, proxy: { '/api': { target, changeOrigin: true } } },
  build: { outDir: 'dist', chunkSizeWarningLimit: 1500 },
  test: { environment: 'jsdom', include: ['src/**/*.test.ts', 'src/**/*.test.tsx'] },
});
