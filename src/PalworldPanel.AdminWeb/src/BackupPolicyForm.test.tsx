import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BackupPolicyForm } from './BackupPolicyForm';
import { api, ApiError, setCsrf } from './api';

vi.mock('./api', async (original) => ({
  ...(await original<typeof import('./api')>()),
  api: vi.fn(),
  setCsrf: vi.fn(),
}));
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

it('过期身份保留备份策略并在重新登录后提交', async () => {
  let saved = false;
  vi.mocked(api).mockImplementation(async (path, init) => {
    if (path.endsWith('/backup-policy') && !saved) {
      saved = true;
      throw new ApiError(403, 'ReauthenticationRequired', '请重新登录后执行危险操作。');
    }
    if (path === '/session' && init?.method === 'POST') return undefined;
    if (path === '/session') return { csrfToken: 'synthetic-csrf' };
    if (path.endsWith('/backup-policy')) return {};
    throw new Error(path);
  });
  const onSaved = vi.fn().mockResolvedValue(undefined);
  render(
    <BackupPolicyForm
      instanceId="synthetic"
      revision={4}
      backupTime="05:15"
      retentionDays={14}
      disabled={false}
      userName="admin"
      onSaved={onSaved}
    />,
  );
  await userEvent.click(screen.getByRole('button', { name: '保存备份策略' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('已保留所填策略');
  expect(screen.getByLabelText('北京时间备份时间')).toHaveValue('05:15');
  expect(screen.getByLabelText('保留天数')).toHaveValue(14);
  await userEvent.type(screen.getByLabelText('面板管理员密码'), 'synthetic-admin-password');
  await userEvent.click(screen.getByRole('button', { name: '保存备份策略' }));
  expect(onSaved).toHaveBeenCalledOnce();
  expect(setCsrf).toHaveBeenCalledWith('synthetic-csrf');
  expect(vi.mocked(api)).toHaveBeenCalledWith(
    '/instances/synthetic/backup-policy',
    expect.objectContaining({
      method: 'PATCH',
      headers: { 'If-Match': '4' },
      body: JSON.stringify({ time: '05:15', retentionDays: 14 }),
    }),
  );
});
