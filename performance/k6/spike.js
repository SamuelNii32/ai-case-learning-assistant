/* global __ENV */
import http from 'k6/http';
import exec from 'k6/execution';
import { check } from 'k6';
import { SharedArray } from 'k6/data';
import { Rate } from 'k6/metrics';

const baseUrl = (__ENV.BASE_URL || '').replace(/\/$/, '');
const spikeVus = Number(__ENV.MAX_VUS || 1000);
const runId = __ENV.LOAD_TEST_RUN_ID || `spike-${Date.now()}`;

validateSafety();

const tokens = new SharedArray('spike-test-tokens', () => {
  const path = __ENV.LOAD_TEST_TOKENS_FILE;
  if (!path) throw new Error('LOAD_TEST_TOKENS_FILE is required.');
  const parsed = JSON.parse(open(path));
  const values = Array.isArray(parsed) ? parsed : parsed.tokens;
  if (!Array.isArray(values) || values.length < spikeVus) {
    throw new Error(`Spike test requires at least ${spikeVus} unique tokens.`);
  }
  return values;
});

const spikeErrors = new Rate('spike_errors');
const rateLimited = new Rate('spike_rate_limited');
const serverErrors = new Rate('spike_server_errors');

export const options = {
  discardResponseBodies: true,
  scenarios: {
    simultaneous_reads: {
      executor: 'per-vu-iterations',
      vus: spikeVus,
      iterations: 1,
      maxDuration: __ENV.SPIKE_MAX_DURATION || '1m',
    },
  },
  thresholds: {
    checks: ['rate>0.99'],
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<1500', 'p(99)<3000'],
    spike_errors: ['rate<0.01'],
    spike_rate_limited: ['rate<0.001'],
    spike_server_errors: ['rate<0.001'],
  },
};

const endpoints = [
  { name: 'uploads-mine-spike', path: '/uploads/mine?page=1&pageSize=20' },
  { name: 'sessions-mine-spike', path: '/sessions/mine?page=1&pageSize=20' },
  { name: 'classes-enrolled-spike', path: '/classes/enrolled' },
];

export default function () {
  const vuIndex = exec.vu.idInTest - 1;
  const request = endpoints[vuIndex % endpoints.length];
  const response = http.get(`${baseUrl}${request.path}`, {
    headers: {
      Authorization: `Bearer ${tokens[vuIndex]}`,
      'X-Load-Test-Run': runId,
    },
    tags: { name: request.name, workload: 'simultaneous-spike' },
    timeout: __ENV.REQUEST_TIMEOUT || '15s',
  });

  const succeeded = response.status === 200;
  spikeErrors.add(!succeeded);
  rateLimited.add(response.status === 429);
  serverErrors.add(response.status >= 500);
  check(response, { [`${request.name} succeeds`]: () => succeeded });
}

function validateSafety() {
  if (__ENV.ALLOW_CAPACITY_TEST !== 'true') {
    throw new Error('Set ALLOW_CAPACITY_TEST=true to acknowledge the spike test.');
  }
  if (!baseUrl) throw new Error('BASE_URL is required.');
  if (!Number.isInteger(spikeVus) || spikeVus < 1 || spikeVus > 5000) {
    throw new Error('MAX_VUS must be an integer between 1 and 5000.');
  }

  const targetEnvironment = (__ENV.TARGET_ENVIRONMENT || '').toLowerCase();
  if (!['local', 'staging', 'production'].includes(targetEnvironment)) {
    throw new Error('TARGET_ENVIRONMENT must be local, staging, or production.');
  }
  if (targetEnvironment === 'production' && __ENV.ALLOW_PRODUCTION_CAPACITY_TEST !== 'true') {
    throw new Error('Production testing additionally requires ALLOW_PRODUCTION_CAPACITY_TEST=true.');
  }
}
