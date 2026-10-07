export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
  ) {
    super(message);
  }
}
let csrf = '';
export function setCsrf(value: string) {
  csrf = value;
}
export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body && !(init.body instanceof FormData) && !headers.has('Content-Type'))
    headers.set('Content-Type', 'application/json');
  if (init.method && init.method !== 'GET') headers.set('X-CSRF-Token', csrf);
  const response = await fetch(`/api/v1${path}`, { ...init, headers, credentials: 'same-origin' });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new ApiError(
      response.status,
      error.code || 'RequestFailed',
      error.title || `请求失败（${response.status}）`,
    );
  }
  return response.status === 204 ? (undefined as T) : response.json();
}
export function commandKey() {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
export async function downloadEncryptedBackup(path: string, passphrase: string): Promise<void> {
  const prepared = await api<{ downloadUrl: string }>(path + '-prepare', {
    method: 'POST',
    body: JSON.stringify({ passphrase }),
  });
  const link = document.createElement('a');
  link.href = prepared.downloadUrl;
  link.download = '';
  link.click();
}
