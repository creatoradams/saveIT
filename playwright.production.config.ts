import { defineConfig, devices } from "@playwright/test";
export default defineConfig({
  testDir: "./tests/production/browser",
  workers: 1,
  retries: 0,
  timeout: 60000,
  reporter: [["list"]],
  use: {
    baseURL: "https://saveit.localhost:8443",
    ignoreHTTPSErrors: true,
    trace: "retain-on-failure",
  },
  projects: [
    { name: "production-chromium", use: { ...devices["Desktop Chrome"] } },
  ],
});
