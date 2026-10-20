import http from 'k6/http';
import { check } from 'k6';

export const baseUrl = __ENV.BASE_URL || 'http://localhost:8080';
export const password = __ENV.DEMO_PASSWORD || 'Demo@123456';

export function userNumber() {
  return ((__VU - 1) % 10) + 1;
}

export function customerId(number) {
  return `00000000-0000-0000-0000-${String(number).padStart(12, '0')}`;
}

export function productId(number) {
  return `10000000-0000-0000-0000-${String(number).padStart(12, '0')}`;
}

export function uuid() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (character) => {
    const random = Math.floor(Math.random() * 16);
    const value = character === 'x' ? random : (random & 0x3) | 0x8;
    return value.toString(16);
  });
}

export function authenticate(number) {
  const username = `user${String(number).padStart(2, '0')}`;
  const response = http.post(
    `${baseUrl}/auth/token`,
    JSON.stringify({ username, password }),
    { headers: { 'Content-Type': 'application/json' }, tags: { operation: 'write' } },
  );
  check(response, { 'authentication succeeds': (result) => result.status === 200 });
  return response.json('accessToken');
}

export function authenticatedHeaders(token, idempotencyKey) {
  const headers = {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
  };
  if (idempotencyKey) {
    headers['Idempotency-Key'] = idempotencyKey;
  }
  return headers;
}

