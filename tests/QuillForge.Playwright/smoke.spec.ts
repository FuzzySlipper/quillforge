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

  test('profiles returns conductors and lore sets', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/profiles`)).json();
    expect(d.conductors).toBeTruthy();
    expect(d.loreSets).toBeTruthy();
    expect(d.narrativeRules).toBeTruthy();
    expect(d.writingStyles).toBeTruthy();
    expect(d.activeNarrativeRules).toBeTruthy();
  });

  test('layouts returns list', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/layouts`)).json();
    expect(d.layouts).toBeTruthy();
    expect(d.layouts.length).toBeGreaterThan(0);
  });

  test('sessions returns object with array', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/sessions`)).json();
    expect(d.sessions).toBeTruthy();
    expect(Array.isArray(d.sessions)).toBe(true);
  });

  test('providers returns object', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/providers`)).json();
    expect(d.providers).toBeTruthy();
  });

  test('lore returns files', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/lore`)).json();
    expect(d.files).toBeTruthy();
    expect(d.activeProject).toBeTruthy();
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
    expect(created.sessionId).toBeTruthy();
    const list = await (await request.get(`${BASE}/api/sessions`)).json();
    expect(Array.isArray(list.sessions)).toBe(true);
    expect(list.sessions.some((s: { id: string }) => s.id === created.sessionId)).toBe(true);
  });

  test('session load and delete use GUID-based contract', async ({ request }) => {
    const created = await (await request.post(`${BASE}/api/session/new`)).json();
    const sessionId = created.sessionId as string;

    const loadedResp = await request.post(`${BASE}/api/sessions/${sessionId}/load`);
    expect(loadedResp.ok()).toBe(true);
    const loaded = await loadedResp.json();
    expect(loaded.sessionId).toBe(sessionId);
    expect(Array.isArray(loaded.messages)).toBe(true);

    const deletedResp = await request.delete(`${BASE}/api/sessions/${sessionId}`);
    expect(deletedResp.ok()).toBe(true);

    const missingLoad = await request.post(`${BASE}/api/sessions/${sessionId}/load`);
    expect(missingLoad.status()).toBe(404);
  });

  test('profiles/switch succeeds', async ({ request }) => {
    const resp = await request.post(`${BASE}/api/profiles/switch`, {
      data: { conductor: 'default', lore: 'default', narrativeRules: 'default', writingStyle: 'default' },
    });
    expect(resp.ok()).toBe(true);
  });

  test('conductors returns data', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/conductors`)).json();
    expect(d.files).toBeTruthy();
  });

  test('writing-styles returns data', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/writing-styles`)).json();
    expect(d.files).toBeTruthy();
  });

  test('narrative-rules returns data', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/narrative-rules`)).json();
    expect(d.files).toBeTruthy();
    expect(d.active).toBeTruthy();
  });

  test('backgrounds returns data', async ({ request }) => {
    const d = await (await request.get(`${BASE}/api/backgrounds`)).json();
    expect(d.backgrounds).toBeTruthy();
  });

  test('conversation/history returns selected-session data shape', async ({ request }) => {
    const created = await (await request.post(`${BASE}/api/session/new`)).json();
    const d = await (await request.get(`${BASE}/api/conversation/history?sessionId=${created.sessionId}`)).json();
    expect(Array.isArray(d.messages)).toBe(true);
    expect(d.count).toBeDefined();
    expect(d.sessionId).toBe(created.sessionId);
  });

  test('plots list, load, and unload work with explicit session ids', async ({ request }) => {
    const created = await (await request.post(`${BASE}/api/session/new`)).json();
    const sessionId = created.sessionId as string;

    const list = await (await request.get(`${BASE}/api/plots?sessionId=${sessionId}`)).json();
    expect(Array.isArray(list.files)).toBe(true);
    expect(list.files.some((p: { name: string }) => p.name === "default")).toBe(true);

    const loadedResp = await request.post(`${BASE}/api/plots/load`, {
      data: { sessionId, name: "default" },
    });
    expect(loadedResp.ok()).toBe(true);
    const loaded = await loadedResp.json();
    expect(loaded.sessionId).toBe(sessionId);
    expect(loaded.activePlotFile).toBe("default");

    const unloadedResp = await request.post(`${BASE}/api/plots/unload`, {
      data: { sessionId },
    });
    expect(unloadedResp.ok()).toBe(true);
    const unloaded = await unloadedResp.json();
    expect(unloaded.sessionId).toBe(sessionId);
    expect(unloaded.activePlotFile).toBeNull();
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

  test('plot panel opens from the header', async ({ page }) => {
    setupErrorTracking(page);
    await page.goto(`${BASE}/`);
    await page.waitForLoadState('networkidle');
    await page.getByTitle('Browse plot arcs').click();
    await expect(page.getByRole('heading', { name: 'Plots' })).toBeVisible();
    assertNoErrors('plot panel open');
  });
});
