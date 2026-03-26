import { test, expect, type Page, type ConsoleMessage } from '@playwright/test';

/**
 * Smoke tests for QuillForge UI and API endpoints.
 * Requires the app to be running: dotnet run --project src/QuillForge.Web
 */

const BASE = 'http://localhost:5204';

let consoleErrors: string[];
let pageErrors: string[];
let failedRequests: string[];

function setupErrorTracking(page: Page) {
  consoleErrors = [];
  pageErrors = [];
  failedRequests = [];

  page.on('console', (msg: ConsoleMessage) => {
    if (msg.type() === 'error') {
      const text = msg.text();
      if (text.includes('favicon.ico')) return;
      consoleErrors.push(text);
    }
  });

  page.on('pageerror', (err: Error) => {
    pageErrors.push(err.message);
  });

  page.on('response', (response) => {
    const status = response.status();
    const url = response.url();
    if (status >= 405 && url.includes('/api/')) {
      failedRequests.push(`${status} ${response.request().method()} ${url}`);
    }
  });
}

function assertNoErrors(context: string) {
  if (pageErrors.length > 0) {
    throw new Error(`[${context}] Uncaught JS exceptions:\n${pageErrors.join('\n')}`);
  }
  if (failedRequests.length > 0) {
    throw new Error(`[${context}] Failed API requests:\n${failedRequests.join('\n')}`);
  }
}

// --- API endpoint tests (use Playwright request context, no browser needed) ---

test.describe('API Smoke Tests', () => {

  test('status returns ready with version', async ({ request }) => {
    const resp = await request.get(`${BASE}/api/status`);
    expect(resp.ok()).toBe(true);
    const d = await resp.json();
    expect(d.status).toBe('ready');
    expect(d.version).toBeTruthy();
    expect(d.build).toBeTruthy();
    expect(d.mode).toBeTruthy();
  });

  test('debug returns build diagnostics', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/debug`)).json();
    expect(d.build).toBeTruthy();
    expect(d.build.version).toBeTruthy();
    expect(d.build.uptime).toBeTruthy();
  });

  test('profiles returns personas and lore sets', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/profiles`)).json();
    expect(d.personas).toBeTruthy();
    expect(d.lore_sets).toBeTruthy();
    expect(d.writing_styles).toBeTruthy();
  });

  test('layouts returns list', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/layouts`)).json();
    expect(d.layouts).toBeTruthy();
    expect(d.layouts.length).toBeGreaterThan(0);
  });

  test('sessions returns array', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/sessions`)).json();
    expect(Array.isArray(d)).toBe(true);
  });

  test('providers returns object', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/providers`)).json();
    expect(d.providers).toBeTruthy();
  });

  test('lore returns files', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/lore`)).json();
    expect(d.files).toBeTruthy();
    expect(d.active_project).toBeTruthy();
  });

  test('forge/projects returns array', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/forge/projects`)).json();
    expect(Array.isArray(d)).toBe(true);
  });

  test('agents/models returns assignments', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/agents/models`)).json();
    expect(d.assignments).toBeTruthy();
    expect(d.assignments.orchestrator).toBeTruthy();
  });

  test('mode switch round-trip', async ({ request }) => {
    for (const mode of ['general', 'writer', 'roleplay', 'forge', 'council']) {
      const d = await (await request.post(`${BASE}/api/mode`, { data: { mode } })).json();
      expect(d.mode).toBe(mode);
    }
    await request.post(`${BASE}/api/mode`, { data: { mode: 'general' } });
  });

  test('session create and list', async ({ request }) => {
    const created = await (await request.post(`${BASE}/api/session/new`)).json();
    expect(created.session_id).toBeTruthy();
    const list = await (await request.get(`${BASE}/api/sessions`)).json();
    expect(Array.isArray(list)).toBe(true);
  });

  test('profiles/switch succeeds', async ({ request }) => {
    const resp = await request.post(`${BASE}/api/profiles/switch`, {
      data: { persona: 'default', lore: 'default', writing_style: 'default' },
    });
    expect(resp.ok()).toBe(true);
  });

  test('persona returns data', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/persona`)).json();
    expect(d.personas).toBeTruthy();
  });

  test('writing-styles returns data', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/writing-styles`)).json();
    expect(d.styles).toBeTruthy();
  });

  test('backgrounds returns data', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/backgrounds`)).json();
    expect(d.backgrounds).toBeTruthy();
  });

  test('conversation/history returns data', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/conversation/history`)).json();
    expect(d.messages).toBeTruthy();
  });
});

// --- UI rendering tests ---

test.describe('UI Smoke Tests', () => {

  test('app loads and renders content', async ({ page }) => {
    setupErrorTracking(page);
    await page.goto(`${BASE}/`);
    await page.waitForLoadState('networkidle');
    const root = page.locator('#root');
    await expect(root).not.toBeEmpty();
    assertNoErrors('main page load');
  });

  test('version badge is visible', async ({ page }) => {
    await page.goto(`${BASE}/`);
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);
    const badge = page.locator('#qf-version-badge');
    await expect(badge).toBeVisible();
    expect(await badge.textContent()).toContain('v0.');
  });

  test('no uncaught exceptions after idle', async ({ page }) => {
    setupErrorTracking(page);
    await page.goto(`${BASE}/`);
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(3000);
    assertNoErrors('main page idle');
  });

  test('no 405 errors on page load', async ({ page }) => {
    setupErrorTracking(page);
    await page.goto(`${BASE}/`);
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(3000);
    if (failedRequests.length > 0) {
      throw new Error(`405+ errors on load:\n${failedRequests.join('\n')}`);
    }
  });
});
