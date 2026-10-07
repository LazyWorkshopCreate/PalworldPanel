import { afterEach, describe, expect, it, vi } from 'vitest';
import { commandKey } from './api';

afterEach(() => vi.unstubAllGlobals());

describe('command keys over LAN HTTP', () => {
  it('creates unique UUIDs when randomUUID is unavailable in an insecure context', () => {
    const secureRandom = globalThis.crypto.getRandomValues.bind(globalThis.crypto);
    vi.stubGlobal('crypto', { getRandomValues: secureRandom });
    const keys = Array.from({ length: 100 }, () => commandKey());
    expect(new Set(keys).size).toBe(keys.length);
    for (const key of keys)
      expect(key).toMatch(/^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/);
  });
});
