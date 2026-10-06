import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
export default defineConfig({
  plugins: [react()],
  build: { outDir: "../LocalCloud.Server/wwwroot", emptyOutDir: true },
  server: {
    proxy: {
      "/api": "http://localhost:43110",
      "/uploads": "http://localhost:43110",
      "/hubs": { target: "http://localhost:43110", ws: true },
    },
  },
});
