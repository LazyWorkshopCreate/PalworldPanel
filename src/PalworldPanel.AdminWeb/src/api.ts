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
  return crypto.randomUUID();
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
