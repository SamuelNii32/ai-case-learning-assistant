/* global __ENV */
import http from 'k6/http';
import { check, sleep } from 'k6';

const baseUrl = (__ENV.BASE_URL || 'http://localhost:5259').replace(/\/$/, '');

export const options = {
  scenarios: {
    public_health: {
      executor: 'ramping-vus',
      startVUs: 1,
      stages: [
        { duration: __ENV.RAMP_UP || '10s', target: Number(__ENV.VUS || 10) },
        { duration: __ENV.DURATION || '30s', target: Number(__ENV.VUS || 10) },
        { duration: '10s', target: 0 },
      ],
    },
  },
  thresholds: {
    checks: ['rate>0.99'],
    http_req_failed: ['rate<0.01'],
    'http_req_duration{name:health-live}': ['p(95)<250'],
    'http_req_duration{name:health-ready}': ['p(95)<750'],
  },
};

export default function () {
  const live = http.get(`${baseUrl}/health/live`, { tags: { name: 'health-live' } });
  check(live, { 'liveness is healthy': (response) => response.status === 200 });

  const ready = http.get(`${baseUrl}/health/ready`, { tags: { name: 'health-ready' } });
  check(ready, { 'readiness is available': (response) => response.status === 200 });
  sleep(1);
}
