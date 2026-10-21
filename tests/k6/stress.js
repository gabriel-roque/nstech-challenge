import http from 'k6/http';
import { check, sleep } from 'k6';
import {
  authenticatedHeaders,
  authenticate,
  baseUrl,
  customerId,
  productId,
  userNumber,
  uuid,
} from './helpers.js';

export const options = {
  stages: [
    { duration: '1m', target: 25 },
    { duration: '1m', target: 25 },
    { duration: '1m', target: 100 },
    { duration: '1m', target: 100 },
    { duration: '1m', target: 0 },
  ],
  thresholds: {
    checks: ['rate==1'],
    http_req_failed: ['rate<0.01'],
    'http_req_duration{operation:read}': ['p(95)<500'],
    'http_req_duration{operation:write}': ['p(95)<1000'],
  },
};

export default function () {
  const number = userNumber();
  const token = authenticate(number);
  const key = uuid();
  const payload = JSON.stringify({
    customerId: customerId(number),
    currency: 'BRL',
    items: [{ productId: productId(((__ITER + number) % 10) + 1), quantity: 1 }],
  });
  const create = http.post(`${baseUrl}/orders`, payload, {
    headers: authenticatedHeaders(token, key),
    tags: { name: 'POST /orders', operation: 'write' },
  });
  check(create, { 'create accepted': (response) => response.status === 201 });
  if (create.status !== 201) {
    sleep(0.1);
    return;
  }

  const id = create.json('id');
  const detail = http.get(`${baseUrl}/orders/${id}`, {
    headers: authenticatedHeaders(token),
    tags: { name: 'GET /orders/:id', operation: 'read' },
  });
  check(detail, { 'detail accepted': (response) => response.status === 200 });
  const confirm = http.post(`${baseUrl}/orders/${id}/confirm`, null, {
    headers: authenticatedHeaders(token),
    tags: { name: 'POST /orders/:id/confirm', operation: 'write' },
  });
  check(confirm, { 'confirm has expected result': (response) => [200, 409].includes(response.status) });
  if (confirm.status === 200) {
    const cancel = http.post(`${baseUrl}/orders/${id}/cancel`, null, {
      headers: authenticatedHeaders(token),
      tags: { name: 'POST /orders/:id/cancel', operation: 'write' },
    });
    check(cancel, { 'cancel accepted': (response) => response.status === 200 });
  }
  const list = http.get(`${baseUrl}/orders?customerId=${customerId(number)}&page=1&pageSize=20`, {
    tags: { name: 'GET /orders', operation: 'read' },
  });
  check(list, { 'list accepted': (response) => response.status === 200 });
  sleep(0.1);
}
