/* global __ENV */
import http from 'k6/http';
import { check, fail, sleep } from 'k6';

const baseUrl = (__ENV.BASE_URL || 'http://localhost:5259').replace(/\/$/, '');

export const options = {
  vus: 1,
  iterations: Number(__ENV.ITERATIONS || 3),
  thresholds: {
    checks: ['rate>0.99'],
    http_req_failed: ['rate<0.01'],
    'http_req_duration{name:document-answer}': ['p(95)<20000'],
  },
};

export function setup() {
  if (!__ENV.ALLOW_PAID_AI_TEST || __ENV.ALLOW_PAID_AI_TEST.toLowerCase() !== 'true') {
    fail('Set ALLOW_PAID_AI_TEST=true to acknowledge that this test incurs OpenAI usage.');
  }
  if (!__ENV.LOAD_TEST_EMAIL || !__ENV.LOAD_TEST_PASSWORD || !__ENV.LOAD_TEST_UPLOAD_ID) {
    fail('Set LOAD_TEST_EMAIL, LOAD_TEST_PASSWORD, and LOAD_TEST_UPLOAD_ID.');
  }

  const login = http.post(
    `${baseUrl}/auth/login`,
    JSON.stringify({ email: __ENV.LOAD_TEST_EMAIL, password: __ENV.LOAD_TEST_PASSWORD }),
    { headers: { 'Content-Type': 'application/json' }, tags: { name: 'login' } },
  );
  const token = login.json('token');
  if (!token) fail(`Login failed; HTTP ${login.status}.`);
  return { token };
}

export default function (data) {
  const question = encodeURIComponent(__ENV.LOAD_TEST_QUESTION || 'What is the central issue in this document?');
  const response = http.get(
    `${baseUrl}/ask/${encodeURIComponent(__ENV.LOAD_TEST_UPLOAD_ID)}?q=${question}`,
    {
      headers: { Authorization: `Bearer ${data.token}` },
      tags: { name: 'document-answer' },
      timeout: '30s',
    },
  );
  check(response, { 'AI answer succeeds': (result) => result.status === 200 });
  sleep(2);
}
