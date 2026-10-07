import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from './App';
import { api, downloadEncryptedBackup } from './api';

vi.mock('./api', async (original) => ({
  ...(await original<typeof import('./api')>()),
  api: vi.fn(),
  downloadEncryptedBackup: vi.fn(),
}));
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
  vi.unstubAllGlobals();
});
const rules = {
  name: '合成实例',
  description: '',
  maxPlayers: 16,
  deathPenalty: 'None',
  offlinePenalty: false,
  deteriorationRate: 0,
  attackDamageRate: 1,
  cpu: 4,
  memoryMiB: 4096,
};
const instance = {
  id: 'synthetic',
  name: '合成实例',
  revision: 1,
  image: 'synthetic-image',
  owned: true,
  writable: true,
  configurationKnown: true,
  resourceBudgetKnown: true,
  desired: rules,
  applied: rules,
  gamePort: 18211,
  gameAddress: '192.168.1.10',
  worldGuid: 'synthetic-world',
  desiredPower: 'stopped',
  gameBuild: 'synthetic-build',
  quarantinedUtc: null,
  purgedUtc: null,
};

it.each(['Queued', 'Running', 'NeedsAttention'])(
  '未结束任务 %s 锁定实例生命周期操作',
  async (state) => {
    mockApplication(state);
    render(<App />);
    await userEvent.click(await screen.findByRole('link', { name: '查看实例' }));
    for (const name of ['启动', '停止', '离线备份']) {
      expect(screen.getByRole('button', { name })).toBeDisabled();
    }
    await userEvent.click(screen.getByRole('button', { name: '更多' }));
    for (const name of [
      '仅克隆规则',
      '重启',
      '保存',
      '强制停止',
      '升级',
      '移除容器保留数据',
      '隔离清理',
      '取消接管',
    ]) {
      expect(screen.getByRole('button', { name })).toBeDisabled();
    }
    expect(
      vi
        .mocked(api)
        .mock.calls.some(([path]) => path.endsWith('/previews') || path.endsWith('/actions')),
    ).toBe(false);
  },
);

it.each([
  ['移除容器保留数据', 'retain-data'],
  ['隔离清理', 'purge'],
  ['取消接管', 'unmanage'],
])('更多中的 %s 打开确认弹窗并关闭菜单', async (label, kind) => {
  mockApplication();
  render(<App />);
  await userEvent.click(await screen.findByRole('link', { name: '查看实例' }));
  await userEvent.click(screen.getByRole('button', { name: '更多' }));
  await userEvent.click(screen.getByRole('button', { name: label }));
  expect(await screen.findByRole('dialog')).toHaveTextContent(`确认${label}`);
  expect(screen.queryByRole('group', { name: '更多实例操作' })).not.toBeInTheDocument();
  await waitFor(() =>
    expect(vi.mocked(api)).toHaveBeenCalledWith(
      '/instances/synthetic/previews',
      expect.objectContaining({ body: JSON.stringify({ kind }) }),
    ),
  );
  expect(vi.mocked(api).mock.calls.some(([path]) => path.endsWith('/actions'))).toBe(false);
});

function mockApplication(initialState?: string) {
  const task = {
    id: 'synthetic-apply',
    instanceId: instance.id,
    kind: 'apply-config',
    state: initialState || 'Queued',
    phase: 'Preflight',
    createdUtc: '2026-10-06T00:00:00Z',
    message: null,
    safeCode: null,
    recoveryPoint: null,
  };
  let submitted = !!initialState;
  vi.stubGlobal(
    'EventSource',
    class {
      close() {}
    },
  );
  vi.mocked(api).mockImplementation(async (path) => {
    if (path === '/setup') return { required: false };
    if (path === '/session') return { userName: 'admin', csrfToken: 'synthetic-csrf' };
    if (path === '/instances') return [instance];
    if (path === '/host')
      return {
        committedMemoryMiB: 4096,
        memoryMiB: 16384,
        reservedMemoryMiB: 4096,
        committedCpu: 4,
        cpuCount: 8,
      };
    if (path === '/tasks') return submitted ? [{ ...task }] : [];
    if (['/audit', '/backups'].includes(path)) return [];
    if (path.endsWith('/observations'))
      return { container: 'stopped', gameApi: 'unknown', stale: false };
    if (path.endsWith('/settings'))
      return { desired: rules, applied: rules, configured: {}, observed: {}, status: 'observed' };
    if (path.endsWith('/previews')) return { token: 'synthetic-token', hash: 'synthetic-hash' };
    if (path.endsWith('/actions')) {
      submitted = true;
      return { ...task };
    }
    throw new Error('Unexpected synthetic request');
  });
  return task;
}

it('收到任务回执后立即锁定全部操作，刷新暂未返回任务时保持锁定，终态释放', async () => {
  const task = mockApplication();
  const original = vi.mocked(api).getMockImplementation()!;
  let listLagging = true;
  vi.mocked(api).mockImplementation(async (path, options) => {
    if (path === '/tasks' && listLagging) return [];
    return original(path, options);
  });
  render(<App />);
  await userEvent.click(await screen.findByRole('link', { name: '查看实例' }));
  await waitFor(() => expect(screen.getByRole('button', { name: '启动' })).toBeEnabled());
  await userEvent.click(screen.getByRole('button', { name: '启动' }));
  const dialog = await screen.findByRole('dialog');
  await waitFor(() =>
    expect(within(dialog).getByRole('button', { name: '确认启动' })).toBeEnabled(),
  );
  await userEvent.dblClick(within(dialog).getByRole('button', { name: '确认启动' }));
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(screen.getByRole('button', { name: '启动' })).toBeDisabled();
  expect(screen.getByRole('button', { name: '离线备份' })).toBeDisabled();
  await userEvent.click(screen.getByRole('button', { name: '更多' }));
  expect(screen.getByRole('button', { name: '仅克隆规则' })).toBeDisabled();
  expect(screen.getByRole('button', { name: '取消接管' })).toBeDisabled();
  expect(vi.mocked(api).mock.calls.filter(([path]) => path.endsWith('/actions'))).toHaveLength(1);
  listLagging = false;
  task.state = 'Succeeded';
  await userEvent.click(screen.getByRole('button', { name: '刷新' }));
  await waitFor(() => expect(screen.getByRole('button', { name: '启动' })).toBeEnabled());
});

it('应用从确认到后台成功保持锁定，需要处理也不释放，成功后恢复', async () => {
  const task = mockApplication();
  render(<App />);
  await userEvent.click(await screen.findByRole('link', { name: '查看实例' }));
  await userEvent.click(screen.getByRole('tab', { name: '设置' }));
  await userEvent.click(screen.getByRole('button', { name: '应用并重启' }));
  expect(screen.getByText('等待确认…').closest('button')).toBeDisabled();
  const confirm = screen.getByRole('button', { name: '确认应用配置' });
  await waitFor(() => expect(confirm).toBeEnabled());
  await userEvent.click(confirm);
  expect(await screen.findByRole('button', { name: '应用中…' })).toBeDisabled();
  task.state = 'Running';
  await userEvent.click(screen.getByRole('button', { name: '刷新' }));
  expect(screen.getByRole('button', { name: '应用中…' })).toBeDisabled();
  task.state = 'NeedsAttention';
  await userEvent.click(screen.getByRole('button', { name: '刷新' }));
  expect(await screen.findByRole('button', { name: '应用需处理' })).toBeDisabled();
  task.state = 'Succeeded';
  await userEvent.click(screen.getByRole('button', { name: '刷新' }));
  expect(await screen.findByRole('button', { name: '应用并重启' })).toBeEnabled();
  expect(vi.mocked(api).mock.calls.filter(([path]) => path.endsWith('/actions'))).toHaveLength(1);
});

it.each(['Queued', 'Running', 'NeedsAttention'])(
  '重新载入页面时 %s 应用任务仍锁定按钮',
  async (state) => {
    mockApplication(state);
    render(<App />);
    await userEvent.click(await screen.findByRole('link', { name: '查看实例' }));
    await userEvent.click(screen.getByRole('tab', { name: '设置' }));
    expect(
      screen.getByRole('button', { name: state === 'NeedsAttention' ? '应用需处理' : '应用中…' }),
    ).toBeDisabled();
    expect(screen.getByRole('button', { name: '查看任务' })).toBeEnabled();
    expect(vi.mocked(api).mock.calls.filter(([path]) => path.endsWith('/actions'))).toHaveLength(0);
  },
);

it('导出弹窗保留口令校验、取消不导出，提交错误在弹窗内显示', async () => {
  vi.mocked(downloadEncryptedBackup).mockRejectedValue(new Error('合成导出错误'));
  vi.stubGlobal(
    'EventSource',
    class {
      close() {}
    },
  );
  const backup = {
    id: 'synthetic-backup',
    instanceId: instance.id,
    createdUtc: '2026-10-06T00:00:00Z',
    bytes: 1024,
    purpose: 'scheduled',
    protected: false,
    containsInstallation: false,
  };
  vi.mocked(api).mockImplementation(async (path) => {
    if (path === '/setup') return { required: false };
    if (path === '/session') return { userName: 'admin', csrfToken: 'synthetic-csrf' };
    if (path === '/instances') return [instance];
    if (path === '/host')
      return {
        committedMemoryMiB: 4096,
        memoryMiB: 16384,
        reservedMemoryMiB: 4096,
        committedCpu: 4,
        cpuCount: 8,
      };
    if (['/tasks', '/audit'].includes(path)) return [];
    if (path.endsWith('/backups')) return [backup];
    if (path.endsWith('/observations'))
      return { container: 'stopped', gameApi: 'unknown', stale: false };
    if (path.endsWith('/export-prepare')) throw new Error('合成导出错误');
    throw new Error('Unexpected synthetic request');
  });
  render(<App />);
  await userEvent.click(await screen.findByRole('link', { name: '查看实例' }));
  await userEvent.click(screen.getByRole('tab', { name: '备份与恢复' }));
  await userEvent.click(await screen.findByRole('button', { name: '加密导出' }));
  let dialog = screen.getByRole('dialog');
  const input = within(dialog).getByLabelText('独立导出口令');
  expect(input).toHaveAttribute('type', 'password');
  expect(input).toHaveAttribute('minlength', '16');
  await userEvent.click(within(dialog).getByRole('button', { name: '取消' }));
  expect(downloadEncryptedBackup).not.toHaveBeenCalled();
  expect(vi.mocked(api).mock.calls.some(([path]) => path.endsWith('/export-prepare'))).toBe(false);
  await userEvent.click(screen.getByRole('button', { name: '加密导出' }));
  dialog = screen.getByRole('dialog');
  await userEvent.type(
    within(dialog).getByLabelText('独立导出口令'),
    'synthetic-export-passphrase',
  );
  await userEvent.click(within(dialog).getByRole('button', { name: '加密并下载' }));
  expect(await within(dialog).findByRole('alert')).toHaveTextContent('合成导出错误');
  expect(within(dialog).getByLabelText('独立导出口令')).toHaveValue('');
});

it.each([
  ['启动', false],
  ['停止', false],
  ['离线备份', false],
  ['重启', true],
  ['保存', true],
  ['强制停止', true],
  ['升级', true],
  ['仅克隆规则', true],
  ['移除容器保留数据', true],
  ['隔离清理', true],
  ['取消接管', true],
] as const)('操作栏 %s 点击显示弹窗，取消不创建任务', async (name, more) => {
  vi.stubGlobal(
    'EventSource',
    class {
      close() {}
    },
  );
  vi.mocked(api).mockImplementation(async (path) => {
    if (path === '/setup') return { required: false };
    if (path === '/session') return { userName: 'admin', csrfToken: 'synthetic-csrf' };
    if (path === '/instances') return [instance];
    if (path === '/host')
      return {
        committedMemoryMiB: 4096,
        memoryMiB: 16384,
        reservedMemoryMiB: 4096,
        committedCpu: 4,
        cpuCount: 8,
      };
    if (['/tasks', '/backups', '/audit'].includes(path)) return [];
    if (path.endsWith('/observations'))
      return {
        container: name === '启动' ? 'stopped' : 'running',
        gameApi: 'healthy',
        stale: false,
      };
    if (path.endsWith('/update-check'))
      return { status: 'up-to-date', currentVersion: 'v1.0.5', targetVersion: 'v1.0.5' };
    if (path.endsWith('/previews')) return { token: 'synthetic-token', hash: 'synthetic-hash' };
    throw new Error('Unexpected synthetic request');
  });
  render(<App />);
  await userEvent.click(await screen.findByRole('link', { name: '查看实例' }));
  if (more) await userEvent.click(screen.getByRole('button', { name: '更多' }));
  await waitFor(() => expect(screen.getByRole('button', { name })).toBeEnabled());
  await userEvent.click(screen.getByRole('button', { name }));
  const dialog = await screen.findByRole('dialog');
  expect(dialog).toHaveTextContent(name === '升级' ? '检查游戏更新' : name);
  await userEvent.click(
    within(dialog).getByRole('button', { name: name === '升级' ? '关闭' : '取消' }),
  );
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(
    vi
      .mocked(api)
      .mock.calls.some(([path]) => path.endsWith('/actions') || path === '/instances/create'),
  ).toBe(false);
});
