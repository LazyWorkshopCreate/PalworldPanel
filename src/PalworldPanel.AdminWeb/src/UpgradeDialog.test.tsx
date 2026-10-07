import React from 'react';
import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { UpgradeDialog } from './UpgradeDialog';
import { api } from './api';

vi.mock('./api', () => ({ api: vi.fn() }));
afterEach(() => {
  cleanup();
  vi.resetAllMocks();
});
const props = {
  instanceId: 'synthetic',
  onClose: vi.fn(),
  onUpgrade: vi.fn(),
  returnFocus: { current: null },
};

it('无更新时明确提示且不能提交升级', async () => {
  vi.mocked(api).mockResolvedValue({
    status: 'up-to-date',
    currentVersion: 'v1.0.5.102999',
    targetVersion: 'v1.0.5',
    updateToken: null,
  });
  render(<UpgradeDialog {...props} />);
  expect(await screen.findByText('当前已是最新版本，不需要更新。')).toBeVisible();
  expect(screen.queryByRole('button', { name: '继续升级' })).not.toBeInTheDocument();
  expect(api).toHaveBeenCalledWith(
    '/instances/synthetic/update-check',
    expect.objectContaining({ method: 'POST' }),
  );
});

it('显示当前和目标版本并携带检查结果进入确认', async () => {
  vi.mocked(api).mockResolvedValue({
    status: 'available',
    currentVersion: 'v1.0.4',
    targetVersion: 'v1.0.5',
    updateToken: 'synthetic-token',
  });
  render(<UpgradeDialog {...props} />);
  await userEvent.click(await screen.findByRole('button', { name: '继续升级' }));
  expect(props.onUpgrade).toHaveBeenCalledWith({
    updateToken: 'synthetic-token',
    currentVersion: 'v1.0.4',
    targetVersion: 'v1.0.5',
  });
  expect(screen.getByText('v1.0.4')).toBeVisible();
  expect(screen.queryByText('synthetic-token')).not.toBeInTheDocument();
});

it('检查失败不能误报最新，重试期间保持不可升级', async () => {
  vi.mocked(api).mockRejectedValueOnce(new Error('暂时无法检查更新')).mockResolvedValueOnce({
    status: 'up-to-date',
    currentVersion: 'v1.0.5',
    targetVersion: 'v1.0.5',
  });
  render(<UpgradeDialog {...props} />);
  expect(await screen.findByRole('alert')).toHaveTextContent('暂时无法检查更新');
  expect(screen.queryByRole('button', { name: '继续升级' })).not.toBeInTheDocument();
  await userEvent.click(screen.getByRole('button', { name: '重新检查' }));
  expect(await screen.findByText('当前已是最新版本，不需要更新。')).toBeVisible();
});

it('关闭时取消检查请求，检查过程中不能提交', () => {
  vi.mocked(api).mockImplementation(() => new Promise(() => {}));
  const view = render(<UpgradeDialog {...props} />);
  expect(screen.getByRole('status')).toHaveTextContent('正在检查更新');
  const signal = vi.mocked(api).mock.calls[0][1]?.signal;
  view.unmount();
  expect(signal?.aborted).toBe(true);
});
