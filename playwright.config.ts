import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/QuillForge.Playwright',
  timeout: 30000,
  retries: 0,
  use: {
    baseURL: 'http://localhost:5204',
    headless: true,
  },
  projects: [
    {
      name: 'chromium',
      use: { browserName: 'chromium' },
    },
  ],
  // Don't auto-start the server — it should already be running
  // Run: dotnet run --project src/QuillForge.Web
});
