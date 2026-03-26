import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      "/api": "http://localhost:5204",
      "/content": "http://localhost:5204",
    },
  },
  build: {
    outDir: "../wwwroot",
    emptyOutDir: true,
  },
});
