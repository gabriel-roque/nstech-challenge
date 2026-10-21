import http from 'k6/http';
import { check } from 'k6';
import {
  authenticatedHeaders,
  authenticate,
  baseUrl,
  customerId,
  productId,
  uuid,
} from './helpers.js';

export const options = {
  vus: 1,
  iterations: 1,
  thresholds: {
    checks: ['rate==1'],
    http_req_failed: ['rate<0.01'],
  },
};

export default function () {
  const token = authenticate(1);
  const key = uuid();
  const payload = JSON.stringify({
    customerId: customerId(1),
    currency: 'BRL',
    items: [{ productId: productId(1), quantity: 1 }],
  });
  const create = http.post(`${baseUrl}/orders`, payload, {
    headers: authenticatedHeaders(token, key),
    tags: { name: 'POST /orders', operation: 'write' },
  });
  check(create, { 'order is created': (response) => response.status === 201 });
  const replay = http.post(`${baseUrl}/orders`, payload, {
    headers: authenticatedHeaders(token, key),
    tags: { name: 'POST /orders', operation: 'write' },
  });
  check(replay, {
    'creation retry is idempotent': (response) => response.status === 200 && response.json('id') === create.json('id'),
  });
  const id = create.json('id');
  const detail = http.get(`${baseUrl}/orders/${id}`, {
    headers: authenticatedHeaders(token),
    tags: { name: 'GET /orders/:id', operation: 'read' },
  });
  check(detail, { 'owner reads order': (response) => response.status === 200 });
  const confirm = http.post(`${baseUrl}/orders/${id}/confirm`, null, {
    headers: authenticatedHeaders(token),
    tags: { name: 'POST /orders/:id/confirm', operation: 'write' },
  });
  check(confirm, { 'order is confirmed': (response) => response.status === 200 });
  const cancel = http.post(`${baseUrl}/orders/${id}/cancel`, null, {
    headers: authenticatedHeaders(token),
    tags: { name: 'POST /orders/:id/cancel', operation: 'write' },
  });
  check(cancel, { 'order is canceled': (response) => response.status === 200 });
  const list = http.get(`${baseUrl}/orders?customerId=${customerId(1)}`, {
    tags: { name: 'GET /orders', operation: 'read' },
  });
  check(list, { 'public list works': (response) => response.status === 200 });
}
