import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ReadOnlyDashboard } from './ReadOnlyDashboard';
import { api } from './api';

vi.mock('./api', () => ({ api: vi.fn() }));
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});
describe('未登录只读仪表盘', () => {
  it('读取公开状态，只有登录入口且登录可取消', async () => {
    vi.mocked(api).mockResolvedValue({
      host: { cpuPercent: 10, usedMemoryBytes: 1024 ** 3, availableMemoryBytes: 2 * 1024 ** 3 },
      instances: [
        {
          id: 'synthetic',
          name: '合成实例',
          status: {
            container: 'stopped',
            players: null,
            fps: null,
            cpu: null,
            memoryBytes: null,
            updatedUtc: null,
            stale: false,
          },
        },
      ],
      updatedUtc: '2026-10-06T00:00:00Z',
    });
    render(<ReadOnlyDashboard login={<p>合成登录表单</p>} />);
    expect(await screen.findByText('合成实例')).toBeInTheDocument();
    expect(screen.getByText('停止')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '启动' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '创建新世界' })).not.toBeInTheDocument();
    expect(api).toHaveBeenCalledWith('/dashboard', expect.anything());
    expect(api).toHaveBeenCalledTimes(1);
    await userEvent.click(screen.getByRole('button', { name: '登录' }));
    expect(screen.getByRole('dialog')).toHaveTextContent('合成登录表单');
    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByRole('button', { name: '登录' })).toHaveFocus();
  });
  it('数据读取失败仍显示未知而非零值，并可打开登录', async () => {
    vi.mocked(api).mockRejectedValue(new Error('暂不可用'));
    render(<ReadOnlyDashboard login={<p>登录</p>} />);
    expect(await screen.findByRole('alert')).toHaveTextContent('暂不可用');
    expect(screen.getAllByText('未知')).toHaveLength(4);
    expect(screen.getByRole('button', { name: '登录' })).toBeEnabled();
  });
});
