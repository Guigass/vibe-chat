/**
 * VibeChat B-146 — capacity workloads by profile.
 *
 * Smoke (default): one VU, 15s, relaxed thresholds. Full benchmark:
 *   MODE=full PROFILE=small|standard|large WORKLOAD=send|history|search|upload|reconnect|admin|isolation
 *
 * Cross-tenant: WORKLOAD=isolation reads FOREIGN_CHANNEL_ID and expects 403/404.
 * Noisy neighbor: set TENANT_B_USER to add a second scenario tagged noisy-neighbor.
 *
 * Usage:
 *   task load:capacity
 */
import http from 'k6/http';
import { check, sleep } from 'k6';

const catalog = JSON.parse(open('./capacity-profiles.json'));
const profileId = __ENV.PROFILE || 'small';
const profile = catalog.profiles[profileId];
if (!profile) {
  throw new Error(`unknown profile ${profileId}`);
}

const mode = __ENV.MODE || 'smoke';
const workload = __ENV.WORKLOAD || 'send';
const smoke = mode !== 'full';
const apiBase = (__ENV.API_BASE || 'http://localhost:5080').replace(/\/$/, '');
const devUser = __ENV.DEV_USER || 'alice';
const adminUser = __ENV.ADMIN_USER || 'demo';
const channelId = __ENV.CHANNEL_ID || '22222222-2222-2222-2222-222222222222';
const workspaceId = __ENV.WORKSPACE_ID || '11111111-1111-1111-1111-111111111111';
const foreignChannel = __ENV.FOREIGN_CHANNEL_ID || '00000000-0000-0000-0000-000000000000';
const tenantB = __ENV.TENANT_B_USER || '';

const vus = Number(__ENV.VUS || (smoke ? 1 : Math.min(profile.concurrency, 20)));
const duration = __ENV.DURATION || (smoke ? '15s' : '5m');

http.setResponseCallback(http.expectedStatuses(200, 201, 202, 204, 400, 401, 403, 404, 429));

export const options = {
  scenarios: {
    capacity: {
      executor: 'constant-vus',
      vus,
      duration,
      exec: 'run',
      tags: { profile: profileId, workload },
    },
    ...(tenantB
      ? {
          neighbor: {
            executor: 'constant-vus',
            vus: 1,
            duration,
            exec: 'neighbor',
            tags: { profile: profileId, workload: 'noisy-neighbor' },
          },
        }
      : {}),
  },
  thresholds: smoke
    ? { http_req_failed: ['rate<0.05'], http_req_duration: ['p(95)<2000'] }
    : {
        http_req_failed: ['rate<0.01'],
        http_req_duration: [`p(95)<${catalog.slis['api-latency']}`],
      },
};

function headers(user) {
  return { 'Content-Type': 'application/json', 'X-Dev-User': user };
}

function newId() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

export function run() {
  const selected = workloads[workload] || workloads.send;
  selected(workload === 'admin' ? adminUser : devUser);
  sleep(1);
}

export function neighbor() {
  workloads.isolation(tenantB);
}

const workloads = {
  send(user) {
    const messageId = newId();
    const response = http.post(
      `${apiBase}/api/v1/channels/${channelId}/messages`,
      JSON.stringify({ messageId, idempotencyKey: `k6-${messageId}`, body: `k6 capacity ${messageId}` }),
      { headers: headers(user) },
    );
    check(response, { 'send accepted or limited': (r) => r.status === 202 || r.status === 200 || r.status === 429 });
  },
  history(user) {
    const response = http.get(`${apiBase}/api/v1/channels/${channelId}/messages?limit=20`, { headers: headers(user) });
    check(response, { 'history 200': (r) => r.status === 200 });
  },
  search(user) {
    const response = http.get(
      `${apiBase}/api/v1/search/messages?workspaceId=${workspaceId}&q=k6&sort=date&limit=20`,
      { headers: headers(user) },
    );
    check(response, { 'search 200': (r) => r.status === 200 });
  },
  upload(user) {
    const response = http.post(
      `${apiBase}/api/v1/channels/${channelId}/attachments`,
      JSON.stringify({ fileName: 'k6.txt', contentType: 'text/plain', sizeBytes: 32, kind: 'File' }),
      { headers: headers(user) },
    );
    check(response, {
      'upload route': (r) => r.status === 200 || r.status === 201 || r.status === 202 || r.status === 429,
    });
  },
  reconnect(user) {
    const health = http.get(`${apiBase}/health/ready`);
    const me = http.get(`${apiBase}/api/v1/me`, { headers: headers(user) });
    const history = http.get(`${apiBase}/api/v1/channels/${channelId}/messages?limit=5`, { headers: headers(user) });
    check(health, { 'reconnect ready': (r) => r.status === 200 });
    check(me, { 'reconnect me': (r) => r.status === 200 });
    check(history, { 'reconnect history': (r) => r.status === 200 });
  },
  admin(user) {
    const response = http.get(`${apiBase}/api/v1/workspaces/${workspaceId}/capacity`, { headers: headers(user) });
    check(response, { 'capacity open or disabled': (r) => r.status === 200 || r.status === 404 });
  },
  isolation(user) {
    const response = http.get(`${apiBase}/api/v1/channels/${foreignChannel}/messages?limit=1`, {
      headers: headers(user),
    });
    check(response, { 'cross-tenant history denied': (r) => r.status === 403 || r.status === 404 });
  },
};
