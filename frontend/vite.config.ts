import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
export default defineConfig({ plugins: [vue()], server: { proxy: { '/api': 'http://localhost:5100' } }, build: { rollupOptions:{output:{manualChunks:{vue:['vue','vue-router','pinia'],components:['element-plus'],charts:['echarts/core','echarts/charts','echarts/components','echarts/renderers']}}} } });
