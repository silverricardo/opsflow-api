// OpsFlow baseline load test — see perf/README.md for the method and how to read the results.
import http from 'k6/http';
import { check, sleep } from 'k6';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.1.0/index.js';

const BASE = __ENV.BASE_URL || 'http://localhost:8080';
const PASSWORD = __ENV.PERF_PASSWORD;
const VUS = parseInt(__ENV.VUS || '20', 10);
const DURATION = __ENV.DURATION || '2m';

// Must not exceed PerformanceDataSeeder: 20 managers × 10 employees, 5 finance users. Signing in is
// deliberately slow (PBKDF2), so setup uses a representative subset of the 200 employees.
const EMPLOYEES = 40;
const MANAGERS = 20;
const FINANCE = 5;

export const options = {
  setupTimeout: '5m',
  scenarios: {
    // Warm-up (JIT, connection pool, SQL Server plan and buffer caches): excluded from the thresholds.
    warmup: { executor: 'constant-vus', vus: Math.max(2, Math.floor(VUS / 4)), duration: '30s', tags: { phase: 'warmup' } },
    measure: { executor: 'constant-vus', vus: VUS, duration: DURATION, startTime: '35s', tags: { phase: 'measure' } },
  },
  // NFR-10 targets. These are goals to compare against, not claims: see the measured results in perf/README.md.
  thresholds: {
    'http_req_failed{phase:measure}': ['rate<0.01'],
    'http_req_duration{phase:measure,op:list_mine}': ['p(95)<200'],
    'http_req_duration{phase:measure,op:list_all}': ['p(95)<200'],
    'http_req_duration{phase:measure,op:get}': ['p(95)<200'],
    'http_req_duration{phase:measure,op:inbox}': ['p(95)<200'],
    'http_req_duration{phase:measure,op:create}': ['p(95)<200'],
    'http_req_duration{phase:measure,op:submit}': ['p(95)<200'],
    'http_req_duration{phase:measure,op:decide}': ['p(95)<200'],
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

function login(email) {
  const res = http.post(`${BASE}/api/v1/auth/token`, JSON.stringify({ email, password: PASSWORD }), {
    headers: { 'Content-Type': 'application/json' },
    tags: { op: 'login' },
  });
  if (res.status !== 200) {
    throw new Error(`login failed for ${email}: ${res.status} ${res.body}`);
  }
  return res.json('accessToken');
}

export function setup() {
  if (!PASSWORD) {
    throw new Error('Set PERF_PASSWORD (the Seed:DemoPassword used for the performance seed).');
  }
  const employees = [];
  for (let i = 1; i <= EMPLOYEES; i++) employees.push(login(`employee${i}@perf.local`));
  const managers = [];
  for (let i = 1; i <= MANAGERS; i++) managers.push(login(`manager${i}@perf.local`));
  const finance = [];
  for (let i = 1; i <= FINANCE; i++) finance.push(login(`finance${i}@perf.local`));
  return { employees, managers, finance };
}

const auth = (token, extra = {}) => ({ headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...extra } });
const pick = (items) => items[Math.floor(Math.random() * items.length)];

// Read-heavy mix, typical for an approval tool: people browse far more than they act.
export default function (data) {
  const roll = Math.random();

  if (roll < 0.35) {
    // Employee browsing their own requests, with a filter or sort now and then.
    const filters = ['', '&status=InApproval', '&sort=-amount', '&category=Expense', '&q=Laptop'];
    const res = http.get(`${BASE}/api/v1/requests?pageSize=20${pick(filters)}`, { ...auth(pick(data.employees)), tags: { op: 'list_mine' } });
    check(res, { 'list mine 200': (r) => r.status === 200 });
  } else if (roll < 0.55) {
    // Finance browsing everything: the broadest query (100k rows visible).
    const filters = ['', '&status=Approved', '&minAmount=5000', '&sort=-amount', '&createdFrom=2026-06-01'];
    const res = http.get(`${BASE}/api/v1/requests?pageSize=20${pick(filters)}`, { ...auth(pick(data.finance)), tags: { op: 'list_all' } });
    check(res, { 'list all 200': (r) => r.status === 200 });
  } else if (roll < 0.75) {
    // Open a request from the first page.
    const token = pick(data.finance);
    const list = http.get(`${BASE}/api/v1/requests?pageSize=20&page=${1 + Math.floor(Math.random() * 50)}`, { ...auth(token), tags: { op: 'list_all' } });
    const items = list.status === 200 ? list.json('items') : [];
    if (items.length > 0) {
      const res = http.get(`${BASE}/api/v1/requests/${pick(items).id}`, { ...auth(token), tags: { op: 'get' } });
      check(res, { 'get 200': (r) => r.status === 200 });
    }
  } else if (roll < 0.9) {
    // Manager checks the inbox and approves the oldest item.
    const token = pick(data.managers);
    const inbox = http.get(`${BASE}/api/v1/approvals/inbox?pageSize=10`, { ...auth(token), tags: { op: 'inbox' } });
    check(inbox, { 'inbox 200': (r) => r.status === 200 });
    const items = inbox.status === 200 ? inbox.json('items') : [];
    if (items.length > 0) {
      const item = items[0];
      const detail = http.get(`${BASE}/api/v1/requests/${item.requestId}`, { ...auth(token), tags: { op: 'get' } });
      if (detail.status === 200) {
        const res = http.post(`${BASE}/api/v1/requests/${item.requestId}/steps/${item.stepId}/decision`,
          JSON.stringify({ decision: 'Approve' }), { ...auth(token, { 'If-Match': detail.headers['Etag'] }), tags: { op: 'decide' } });
        // 412 / 409 are legitimate outcomes when two VUs act on the same request at once.
        check(res, { 'decide 200/409/412': (r) => [200, 409, 412].includes(r.status) });
      }
    }
  } else {
    // Employee creates and submits a request (write path: insert, audit, approval chain).
    const token = pick(data.employees);
    const created = http.post(`${BASE}/api/v1/requests`,
      JSON.stringify({ title: 'Load test purchase', category: 'Purchase', amount: Math.round(Math.random() * 9000 + 100) }),
      { ...auth(token, { 'Idempotency-Key': `k6-${__VU}-${__ITER}` }), tags: { op: 'create' } });
    check(created, { 'create 201': (r) => r.status === 201 });
    if (created.status === 201) {
      const res = http.post(`${BASE}/api/v1/requests/${created.json('id')}/submit`, null,
        { ...auth(token, { 'If-Match': created.headers['Etag'] }), tags: { op: 'submit' } });
      check(res, { 'submit 200': (r) => r.status === 200 });
    }
  }

  sleep(Math.random() * 0.5 + 0.25); // think time: 0.25–0.75 s
}

export function handleSummary(data) {
  const stamp = new Date().toISOString().replace(/[:.]/g, '-');
  return {
    stdout: textSummary(data, { indent: '  ', enableColors: false }),
    [`/results/baseline-${stamp}.json`]: JSON.stringify(data, null, 2),
  };
}
