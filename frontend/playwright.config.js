import { defineConfig } from '@playwright/test';
export default defineConfig({
  testDir: './e2e', fullyParallel: true,
  use: { channel: 'msedge', headless: true, viewport: { width: 600, height: 700 } },
  // @motion tests sample animation frames, so they run one at a time after the rest: parallel headless instances rendering the
  // SVG glass starve each other's frames.
  projects: [{ name: 'ui', grepInvert: /@motion/ }, { name: 'motion', grep: /@motion/, fullyParallel: false, dependencies: ['ui'] }],
  webServer: { command: 'npx vite preview --host 127.0.0.1 --port 4176', url: 'http://127.0.0.1:4176', reuseExistingServer: false },
});
