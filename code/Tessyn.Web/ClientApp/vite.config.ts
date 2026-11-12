import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Production build -> ../wwwroot (served by Tessyn.Web). The mock build (mode "mock", VITE_MOCK=1 from .env.mock)
// goes to ./dist-mock so it can never end up in wwwroot.
export default defineConfig(({ mode }) => ({
	plugins: [react()],
	build: {
		outDir: mode === 'mock' ? 'dist-mock' : '../wwwroot',
		emptyOutDir: true,
		sourcemap: false,
		target: 'es2022',
	},
	server: {
		port: 5173,
		proxy: mode === 'mock' ? undefined : { '/api': 'http://127.0.0.1:5320' },
	},
	test: {
		include: ['src/**/*.test.ts'],
		environment: 'node',
	},
}));
