import React from 'react';
import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ActionConfirmation } from './ActionConfirmation';
import { api, ApiError } from './api';

vi.mock('./api', async (original) => ({
  ...(await original<typeof import('./api')>()),
  api: vi.fn(),
  commandKey: () => 'synthetic-command',
}));
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});
const instance = { id: 'synthetic', name: '合成实例', revision: 3, worldGuid: 'synthetic-world' };
const props = {
  onClose: vi.fn(),
  onComplete: vi.fn().mockResolvedValue(undefined),
  onReauthenticate: vi.fn().mockResolvedValue(undefined),
  returnFocus: { current: null },
};

it.each([
  'start',
  'stop',
  'backup',
  'restart',
  'save',
  'upgrade',
  'adopt',
  'retain-data',
  'purge',
  'unmanage',
  'undo-quarantine',
  'finalize-purge',
])('%s 只预检，取消不执行', async (kind) => {
  vi.mocked(api).mockResolvedValue({ token: 'synthetic-token', hash: 'synthetic-hash' });
  render(<ActionConfirmation {...props} request={{ instance, kind }} />);
  expect(screen.getByRole('dialog')).toHaveTextContent('合成实例');
  await waitFor(() => expect(api).toHaveBeenCalledTimes(1));
  await userEvent.click(screen.getByRole('button', { name: '取消' }));
  expect(props.onClose).toHaveBeenCalledOnce();
  expect(api).not.toHaveBeenCalledWith(expect.stringContaining('/actions'), expect.anything());
});

it('明确确认后携带预检令牌和修订提交，强制停止提示未保存风险', async () => {
  vi.mocked(api).mockResolvedValue({ token: 'synthetic-token', hash: 'synthetic-hash' });
  render(
    <ActionConfirmation
      {...props}
      request={{ instance, kind: 'stop', arguments: { force: true } }}
    />,
  );
  expect(screen.getByRole('dialog')).toHaveTextContent('可能丢失未保存进度');
  const confirm = screen.getByRole('button', { name: '确认强制停止' });
  await waitFor(() => expect(confirm).toBeEnabled());
  await userEvent.click(confirm);
  expect(api).toHaveBeenLastCalledWith(
    '/instances/synthetic/actions',
    expect.objectContaining({
      headers: { 'Idempotency-Key': 'synthetic-command', 'If-Match': '3' },
      body: JSON.stringify({
        kind: 'stop',
        confirmation: 'synthetic-token',
        previewHash: 'synthetic-hash',
        arguments: { force: true },
        typedName: '',
      }),
    }),
  );
});

it('预检失败显示弹窗内错误，不能确认，近期认证成功后仍需明确确认', async () => {
  vi.mocked(api)
    .mockRejectedValueOnce(
      new ApiError(403, 'ReauthenticationRequired', '请重新登录后执行危险操作。'),
    )
    .mockResolvedValue({ token: 'synthetic-token', hash: 'synthetic-hash' });
  render(<ActionConfirmation {...props} request={{ instance, kind: 'restart' }} />);
  expect(await screen.findByRole('alert')).toHaveTextContent('请重新登录');
  const confirm = screen.getByRole('button', { name: '确认重启' });
  expect(confirm).toBeDisabled();
  await userEvent.type(screen.getByLabelText('面板管理员登录密码'), 'synthetic-login');
  await userEvent.click(screen.getByRole('button', { name: '验证身份' }));
  await waitFor(() => expect(confirm).toBeEnabled());
  expect(props.onReauthenticate).toHaveBeenCalledWith('synthetic-login');
  expect(api).toHaveBeenCalledTimes(2);
  expect(api).not.toHaveBeenCalledWith(expect.stringContaining('/actions'), expect.anything());
});

it('清理须完整实例名；预检等待中可取消', async () => {
  vi.mocked(api).mockResolvedValue({ token: 'synthetic-token', hash: 'synthetic-hash' });
  const view = render(<ActionConfirmation {...props} request={{ instance, kind: 'purge' }} />);
  const confirm = screen.getByRole('button', { name: '确认隔离清理' });
  await screen.findByLabelText('输入完整实例名确认清理');
  expect(confirm).toBeDisabled();
  await userEvent.type(screen.getByLabelText('输入完整实例名确认清理'), instance.name);
  await waitFor(() => expect(confirm).toBeEnabled());
  view.unmount();
  vi.mocked(api).mockImplementation(() => new Promise(() => {}));
  render(<ActionConfirmation {...props} request={{ instance, kind: 'start' }} />);
  expect(screen.getByRole('button', { name: '确认启动' })).toBeDisabled();
  await userEvent.click(screen.getByRole('button', { name: '取消' }));
  expect(props.onClose).toHaveBeenCalledOnce();
});
