/* global __ENV */
import http from 'k6/http';
import exec from 'k6/execution';
import { check, sleep } from 'k6';
import { SharedArray } from 'k6/data';
import { Rate } from 'k6/metrics';

const baseUrl = (__ENV.BASE_URL || '').replace(/\/$/, '');
const maxVus = Number(__ENV.MAX_VUS || 1000);
const stageDuration = __ENV.STAGE_DURATION || '2m';
const thinkTimeSeconds = Number(__ENV.THINK_TIME_SECONDS || 5);
const runId = __ENV.LOAD_TEST_RUN_ID || `capacity-${Date.now()}`;

validateSafety();

const tokens = new SharedArray('capacity-test-tokens', () => {
  const path = __ENV.LOAD_TEST_TOKENS_FILE;
  if (!path) throw new Error('LOAD_TEST_TOKENS_FILE is required.');
  const parsed = JSON.parse(open(path));
  const values = Array.isArray(parsed) ? parsed : parsed.tokens;
  if (!Array.isArray(values) || values.length === 0) {
    throw new Error('The load-test token file must contain a tokens array.');
  }
  return values;
});

if (tokens.length < maxVus) {
  throw new Error(`Capacity test needs ${maxVus} unique tokens; the file contains ${tokens.length}.`);
}

const capacityErrors = new Rate('capacity_errors');
const rateLimited = new Rate('capacity_rate_limited');
const serverErrors = new Rate('capacity_server_errors');

function stageTarget(ratio) {
  return Math.max(1, Math.ceil(maxVus * ratio));
}

export const options = {
  discardResponseBodies: true,
  scenarios: {
    authenticated_capacity: {
      executor: 'ramping-vus',
      startVUs: 0,
      gracefulRampDown: '30s',
      stages: [
        { duration: stageDuration, target: stageTarget(0.05) },
        { duration: stageDuration, target: stageTarget(0.1) },
        { duration: stageDuration, target: stageTarget(0.25) },
        { duration: stageDuration, target: stageTarget(0.5) },
        { duration: stageDuration, target: maxVus },
        { duration: stageDuration, target: maxVus },
        { duration: stageDuration, target: 0 },
      ],
    },
  },
  thresholds: {
    checks: ['rate>0.99'],
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<750', 'p(99)<1500'],
    capacity_errors: ['rate<0.01'],
    capacity_rate_limited: ['rate<0.001'],
    capacity_server_errors: ['rate<0.001'],
  },
};

const workload = [
  { upperBound: 40, name: 'uploads-mine', path: '/uploads/mine?page=1&pageSize=20' },
  { upperBound: 80, name: 'sessions-mine', path: '/sessions/mine?page=1&pageSize=20' },
  { upperBound: 100, name: 'classes-enrolled', path: '/classes/enrolled' },
];

export default function () {
  const vuIndex = exec.vu.idInTest - 1;
  const token = tokens[vuIndex];
  const bucket = (exec.vu.idInTest + exec.scenario.iterationInInstance) % 100;
  const request = workload.find((candidate) => bucket < candidate.upperBound);
  const response = http.get(`${baseUrl}${request.path}`, {
    headers: {
      Authorization: `Bearer ${token}`,
      'X-Load-Test-Run': runId,
    },
    tags: { name: request.name, workload: 'capacity-read' },
    timeout: __ENV.REQUEST_TIMEOUT || '10s',
  });

  const succeeded = response.status === 200;
  capacityErrors.add(!succeeded);
  rateLimited.add(response.status === 429);
  serverErrors.add(response.status >= 500);
  check(response, { [`${request.name} succeeds`]: () => succeeded });

  const jitter = (exec.vu.idInTest % 5) * 0.2;
  sleep(Math.max(0.1, thinkTimeSeconds + jitter));
}

function validateSafety() {
  if (__ENV.ALLOW_CAPACITY_TEST !== 'true') {
    throw new Error('Set ALLOW_CAPACITY_TEST=true to acknowledge the capacity test.');
  }
  if (!baseUrl) throw new Error('BASE_URL is required.');
  if (!Number.isInteger(maxVus) || maxVus < 1 || maxVus > 5000) {
    throw new Error('MAX_VUS must be an integer between 1 and 5000.');
  }
  if (!Number.isFinite(thinkTimeSeconds) || thinkTimeSeconds <= 0) {
    throw new Error('THINK_TIME_SECONDS must be greater than zero.');
  }

  const targetEnvironment = (__ENV.TARGET_ENVIRONMENT || '').toLowerCase();
  if (!['local', 'staging', 'production'].includes(targetEnvironment)) {
    throw new Error('TARGET_ENVIRONMENT must be local, staging, or production.');
  }
  if (targetEnvironment === 'production' && __ENV.ALLOW_PRODUCTION_CAPACITY_TEST !== 'true') {
    throw new Error('Production testing additionally requires ALLOW_PRODUCTION_CAPACITY_TEST=true.');
  }
}
