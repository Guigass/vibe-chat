/**
 * VibeChat W5-3 — k6 load smoke (DevAuth path).
 *
 * Prerequisites:
 *   - API running in Development with seed (task setup / task seed)
 *   - Demo channel id: 22222222-2222-2222-2222-222222222222
 *
 * Usage:
 *   task load:smoke
 *   # or: k6 run -e API_BASE=http://localhost:5080 tests/load/smoke.js
 *
 * Env:
 *   API_BASE   default http://localhost:5080
 *   DEV_USER   default alice
 *   CHANNEL_ID default demo #geral
 */
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';

const apiBase = (__ENV.API_BASE || 'http://localhost:5080').replace(/\/$/, '');
const devUser = __ENV.DEV_USER || 'alice';
const channelId = __ENV.CHANNEL_ID || '22222222-2222-2222-2222-222222222222';

const failRate = new Rate('vibechat_smoke_failures');
const sendDuration = new Trend('vibechat_send_duration', true);

export const options = {
  scenarios: {
    smoke: {
      executor: 'constant-vus',
      vus: Number(__ENV.VUS || 2),
      duration: __ENV.DURATION || '20s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.05'],
    vibechat_smoke_failures: ['rate<0.05'],
    http_req_duration: ['p(95)<2000'],
  },
};

function headers() {
  return {
    'Content-Type': 'application/json',
    'X-Dev-User': devUser,
  };
}

export default function () {
  const health = http.get(`${apiBase}/health`);
  const healthOk = check(health, {
    'health 200': (r) => r.status === 200,
  });
  failRate.add(!healthOk);

  const me = http.get(`${apiBase}/api/v1/me`, { headers: headers() });
  const meOk = check(me, {
    'me 200': (r) => r.status === 200,
  });
  failRate.add(!meOk);

  const history = http.get(`${apiBase}/api/v1/channels/${channelId}/messages?limit=20`, {
    headers: headers(),
  });
  const historyOk = check(history, {
    'history 200': (r) => r.status === 200,
  });
  failRate.add(!historyOk);

  const messageId = crypto.randomUUID();
  const idempotencyKey = `k6-${messageId}`;
  const body = JSON.stringify({
    messageId,
    idempotencyKey,
    body: `k6 smoke ${messageId}`,
  });

  const send = http.post(`${apiBase}/api/v1/channels/${channelId}/messages`, body, {
    headers: headers(),
  });
  sendDuration.add(send.timings.duration);
  const sendOk = check(send, {
    'send accepted': (r) => r.status === 202 || r.status === 200,
  });
  failRate.add(!sendOk);

  sleep(1);
}

export function handleSummary(data) {
  const lines = [
    'VibeChat k6 smoke summary',
    `API_BASE=${apiBase} DEV_USER=${devUser}`,
    `http_req_failed=${data.metrics.http_req_failed?.values?.rate ?? 'n/a'}`,
    `p95=${data.metrics.http_req_duration?.values['p(95)'] ?? 'n/a'}`,
    `checks=${JSON.stringify(data.metrics.checks?.values ?? {})}`,
  ];
  return {
    stdout: `${lines.join('\n')}\n`,
    'tests/load/last-smoke-summary.txt': `${lines.join('\n')}\n`,
  };
}
