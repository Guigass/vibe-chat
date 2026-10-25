/**
 * VibeChat B-170 — hot-path sample on the B-146 Small and Standard envelopes.
 *
 * Lab (default): arrival rate capped at 8/s, 20s. Full:
 *   MODE=full PROFILE=small|standard WORKLOAD=send|history|search|upload|reconnect|admin|isolation
 *
 * Outbox lag, indexed messages and channel size are SQL after the run, not k6.
 * This script does not open SignalR sockets. No PII in the body.
 *
 * Usage:
 *   task load:performance
 */
import http from 'k6/http';
import { check } from 'k6';

const catalog = JSON.parse(open('./capacity-profiles.json'));
const profileId = __ENV.PROFILE || 'small';
const profile = catalog.profiles[profileId];
if (!profile) {
  throw new Error(`unknown profile ${profileId}`);
}

const mode = __ENV.MODE || 'lab';
const workload = __ENV.WORKLOAD || 'send';
const lab = mode !== 'full';
const apiBase = (__ENV.API_BASE || 'http://localhost:5080').replace(/\/$/, '');
const devUser = __ENV.DEV_USER || 'alice';
const adminUser = __ENV.ADMIN_USER || 'demo';
const channelId = __ENV.CHANNEL_ID || '22222222-2222-2222-2222-222222222222';
const workspaceId = __ENV.WORKSPACE_ID || '11111111-1111-1111-1111-111111111111';
const foreignChannel = __ENV.FOREIGN_CHANNEL_ID || '00000000-0000-0000-0000-000000000000';

const planned = workload === 'admin' || workload === 'upload' || workload === 'isolation'
  ? 1
  : profile.messagesPerSecond;
const rate = Math.max(1, Math.min(planned, lab ? 8 : 20));
const duration = __ENV.DURATION || (lab ? '20s' : '5m');
const vus = Math.max(rate, Math.min(profile.concurrency, lab ? 8 : 20));

http.setResponseCallback(http.expectedStatuses(200, 201, 202, 204, 400, 401, 403, 404, 429));

export const options = {
  scenarios: {
    hot: {
      executor: 'constant-arrival-rate',
      rate,
      timeUnit: '1s',
      duration,
      preAllocatedVUs: vus,
      maxVUs: vus,
      exec: 'run',
      tags: { review: 'B-170', profile: profileId, workload },
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.05'],
    checks: ['rate>0.95'],
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
}

const workloads = {
  send(user) {
    const messageId = newId();
    const response = http.post(
      `${apiBase}/api/v1/channels/${channelId}/messages`,
      JSON.stringify({ messageId, idempotencyKey: `k6-${messageId}`, body: `k6 performance ${messageId}` }),
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
    const response = http.get(`${apiBase}/api/v1/admin/workspaces/${workspaceId}/export`, { headers: headers(user) });
    check(response, { 'export 200': (r) => r.status === 200 });
  },
  isolation(user) {
    const response = http.get(`${apiBase}/api/v1/channels/${foreignChannel}/messages?limit=1`, {
      headers: headers(user),
    });
    check(response, { 'cross-tenant history denied': (r) => r.status === 403 || r.status === 404 });
  },
};
