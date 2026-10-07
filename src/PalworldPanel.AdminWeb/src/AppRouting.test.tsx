import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from './App';
import { api, ApiError } from './api';

vi.mock('./api', async (original) => ({
  ...(await original<typeof import('./api')>()),
  api: vi.fn(),
}));
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
  vi.unstubAllGlobals();
});
const rules = {
  name: '路由实例',
  description: '',
  maxPlayers: 16,
  deathPenalty: 'None',
  offlinePenalty: false,
  deteriorationRate: 0,
  attackDamageRate: 1,
  cpu: 4,
  memoryMiB: 4096,
};
function fixture(authenticated = true, setup = false) {
  vi.stubGlobal(
    'EventSource',
    class {
      close() {}
    },
  );
  vi.mocked(api).mockImplementation(async (path, options) => {
    if (path === '/setup') return { required: setup };
    if (path === '/session') {
      if (options?.method === 'POST') {
        authenticated = true;
        return {};
      }
      if (options?.method === 'DELETE') {
        authenticated = false;
        return {};
      }
      if (!authenticated) throw new ApiError(401, 'Unauthorized', '需要登录');
      return { userName: 'admin', csrfToken: 'synthetic' };
    }
    if (path === '/instances')
      return [
        {
          id: 'synthetic',
          name: rules.name,
          revision: 1,
          writable: true,
          owned: true,
          configurationKnown: true,
          resourceBudgetKnown: true,
          desired: rules,
          applied: rules,
          image: 'synthetic-image',
          worldGuid: 'synthetic',
          gamePort: 18211,
          desiredPower: 'stopped',
        },
      ];
    if (path === '/host')
      return {
        memoryMiB: 16384,
        reservedMemoryMiB: 4096,
        cpuCount: 8,
        committedCpu: 4,
        committedMemoryMiB: 20480,
        runtimeUsedMemoryBytes: 2 * 1024 ** 3,
        runtimeAvailableMemoryBytes: 14 * 1024 ** 3,
      };
    if (['/tasks', '/audit', '/backups'].includes(path) || path.endsWith('/backups')) return [];
    if (path.endsWith('/observations'))
      return { container: 'stopped', containerPresent: true, gameApi: 'unknown', stale: false };
    if (path.endsWith('/settings'))
      return { desired: rules, applied: rules, configured: {}, observed: {}, status: 'observed' };
    if (path.endsWith('/logs')) return { text: '合成日志' };
    if (path === '/dashboard')
      return { host: {}, instances: [], updatedUtc: '2026-10-07T00:00:00Z' };
    throw new Error('Unexpected fixture request: ' + path);
  });
}

it.each(['overview', 'settings', 'logs', 'backups', 'tasks'])(
  '直接访问实例 %s 路由恢复正确页签',
  async (tab) => {
    fixture();
    window.history.replaceState(null, '', `/instances/synthetic/${tab}`);
    render(<App />);
    const names: Record<string, string> = {
      overview: '概览',
      settings: '设置',
      logs: '日志',
      backups: '备份与恢复',
      tasks: '任务',
    };
    expect(await screen.findByRole('tab', { name: names[tab] })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    if (tab === 'settings') expect(screen.getByText('配置草稿')).toBeInTheDocument();
    if (tab === 'logs') expect(await screen.findByText('合成日志')).toBeInTheDocument();
    expect(document.title).toContain('路由实例');
  },
);

it('未登录深链接回跳、退出及历史返回均受认证保护', async () => {
  fixture(false);
  window.history.replaceState(null, '', '/instances/synthetic/logs');
  render(<App />);
  await screen.findByRole('button', { name: '登录面板' });
  await waitFor(() => expect(window.location.pathname).toBe('/login'));
  expect(new URLSearchParams(window.location.search).get('returnTo')).toBe(
    '/instances/synthetic/logs',
  );
  expect(vi.mocked(api).mock.calls.some(([path]) => path === '/instances')).toBe(false);
  await userEvent.type(screen.getByLabelText('管理员密码'), 'synthetic-password');
  await userEvent.click(screen.getByRole('button', { name: '登录面板' }));
  expect(await screen.findByRole('tab', { name: '日志' })).toHaveAttribute('aria-selected', 'true');
  expect(window.location.pathname).toBe('/instances/synthetic/logs');
  await userEvent.click(screen.getByRole('link', { name: '主机与安全' }));
  expect(window.location.pathname).toBe('/host');
  await userEvent.click(screen.getByRole('button', { name: '退出' }));
  await screen.findByRole('heading', { name: '仪表盘' });
  expect(window.location.pathname).toBe('/dashboard');
  expect(screen.queryByRole('tab')).not.toBeInTheDocument();
  window.history.back();
  await screen.findByRole('button', { name: '登录面板' });
  await waitFor(() => expect(window.location.pathname).toBe('/login'));
  expect(new URLSearchParams(window.location.search).get('returnTo')).toBe(
    '/instances/synthetic/logs',
  );
  expect(screen.queryByRole('tab')).not.toBeInTheDocument();
});

it('首次初始化有独立路径，未知地址和不存在的实例有明确反馈', async () => {
  fixture(true, true);
  const view = render(<App />);
  await screen.findByRole('heading', { name: '设置管理员密码' });
  await waitFor(() => expect(window.location.pathname).toBe('/setup'));
  view.unmount();
  fixture();
  window.history.replaceState(null, '', '/does-not-exist');
  const missing = render(<App />);
  expect(await screen.findByRole('heading', { name: '页面不存在' })).toBeInTheDocument();
  missing.unmount();
  window.history.replaceState(null, '', '/instances/missing');
  render(<App />);
  expect(
    await screen.findByRole('heading', { name: '实例不存在或已取消接管' }),
  ).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: '创建新世界' })).not.toBeInTheDocument();
});

it('内部链接和页签切换更新 URL，返回列表不会保留旧实例', async () => {
  fixture();
  render(<App />);
  await userEvent.click(await screen.findByRole('link', { name: '查看实例' }));
  expect(window.location.pathname).toBe('/instances/synthetic');
  await userEvent.click(screen.getByRole('tab', { name: '任务' }));
  expect(window.location.pathname).toBe('/instances/synthetic/tasks');
  await userEvent.click(screen.getByRole('link', { name: '主机与安全' }));
  expect(window.location.pathname).toBe('/host');
  expect(await screen.findByRole('heading', { name: '监控指标' })).toBeInTheDocument();
  expect(screen.queryByRole('tab')).not.toBeInTheDocument();
  window.history.back();
  expect(await screen.findByRole('tab', { name: '任务' })).toHaveAttribute('aria-selected', 'true');
});

it('内存同时显示实际使用、已分配和总量，超额分配不误报实时内存不足', async () => {
  fixture();
  window.history.replaceState(null, '', '/instances');
  render(<App />);
  expect(await screen.findByText('使用中 / 已分配 / 总内存')).toBeInTheDocument();
  expect(await screen.findByText('2.0 / 20.0 / 16.0 GiB')).toBeInTheDocument();
  expect(screen.getByText('可用 14.0 GiB · 系统预留 4.0 GiB')).toBeInTheDocument();
  expect(screen.queryByText(/可分配内存不足/)).not.toBeInTheDocument();
});
