/* global __ENV */
import http from 'k6/http';
import exec from 'k6/execution';
import { check, fail, sleep } from 'k6';

const baseUrl = (__ENV.BASE_URL || 'http://localhost:5259').replace(/\/$/, '');

export const options = {
  vus: Number(__ENV.VUS || 1),
  duration: __ENV.DURATION || '1m',
  thresholds: {
    checks: ['rate>0.99'],
    http_req_failed: ['rate<0.01'],
    'http_req_duration{name:current-user}': ['p(95)<750'],
    'http_req_duration{name:uploads-mine}': ['p(95)<750'],
    'http_req_duration{name:sessions-mine}': ['p(95)<750'],
  },
};

function credentials() {
  if (__ENV.LOAD_TEST_USERS_JSON) {
    try {
      const users = JSON.parse(__ENV.LOAD_TEST_USERS_JSON);
      if (Array.isArray(users) && users.length > 0) return users;
    } catch {
      fail('LOAD_TEST_USERS_JSON must be a non-empty JSON array of email/password objects.');
    }
  }

  if (__ENV.LOAD_TEST_EMAIL && __ENV.LOAD_TEST_PASSWORD) {
    return [{ email: __ENV.LOAD_TEST_EMAIL, password: __ENV.LOAD_TEST_PASSWORD }];
  }

  fail('Set LOAD_TEST_USERS_JSON or LOAD_TEST_EMAIL and LOAD_TEST_PASSWORD.');
}

export function setup() {
  const tokens = credentials().map((user) => {
    const response = http.post(
      `${baseUrl}/auth/login`,
      JSON.stringify(user),
      { headers: { 'Content-Type': 'application/json' }, tags: { name: 'login' } },
    );
    check(response, { 'login succeeds': (result) => result.status === 200 });
    const token = response.json('token');
    if (!token) fail(`Login failed for ${user.email}; HTTP ${response.status}.`);
    return token;
  });

  return { tokens };
}

export default function (data) {
  const token = data.tokens[(exec.vu.idInTest - 1) % data.tokens.length];
  const params = { headers: { Authorization: `Bearer ${token}` } };
  const responses = http.batch([
    ['GET', `${baseUrl}/me`, null, { ...params, tags: { name: 'current-user' } }],
    ['GET', `${baseUrl}/uploads/mine?page=1&pageSize=20`, null, { ...params, tags: { name: 'uploads-mine' } }],
    ['GET', `${baseUrl}/sessions/mine?page=1&pageSize=20`, null, { ...params, tags: { name: 'sessions-mine' } }],
  ]);

  check(responses[0], { '/me succeeds': (response) => response.status === 200 });
  check(responses[1], { 'uploads list succeeds': (response) => response.status === 200 });
  check(responses[2], { 'sessions list succeeds': (response) => response.status === 200 });
  sleep(Number(__ENV.ITERATION_PAUSE_SECONDS || 2));
}
