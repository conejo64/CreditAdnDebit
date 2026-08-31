import http from 'k6/http';
import { check, sleep } from 'k6';
import { generateAuthorizeTransactionPayload } from './payloads.js';
import { Rate } from 'k6/metrics';

// Custom metric for failure rate
export const errorRate = new Rate('errors');

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5000';
const PROFILE = __ENV.PROFILE || 'smoke';

// Define profiles
const profiles = {
    smoke: {
        vus: 1,
        duration: '10s',
    },
    soak: {
        vus: 10,
        duration: '5m',
    },
    stress: {
        stages: [
            { duration: '2m', target: 50 },  // ramp up
            { duration: '2m', target: 50 },  // stay
            { duration: '1m', target: 0 },   // ramp down
        ],
    },
};

export const options = {
    ...profiles[PROFILE],
    thresholds: {
        // 95% of requests must complete below 500ms
        http_req_duration: ['p(95)<500', 'p(99)<1000'],
        // Error rate must be less than 1%
        errors: ['rate<0.01'],
    },
};

export default function () {
    const payload = JSON.stringify(generateAuthorizeTransactionPayload());
    
    const params = {
        headers: {
            'Content-Type': 'application/json',
        },
    };

    const res = http.post(`${BASE_URL}/api/iso/authorize`, payload, params);

    const success = check(res, {
        'status is 200': (r) => r.status === 200,
    });
    
    errorRate.add(!success);

    sleep(1);
}
