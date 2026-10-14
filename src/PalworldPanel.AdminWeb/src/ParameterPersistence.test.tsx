import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
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
  name: '草稿测试世界',
  description: '',
  maxPlayers: 16,
  deathPenalty: 'None',
  offlinePenalty: false,
  deteriorationRate: 0,
  attackDamageRate: 1,
  cpu: 4,
  memoryMiB: 16384,
  additional: { PalCaptureRate: '1.5' },
};
function fixture(fail = false) {
  let saved = {
    id: 'synthetic',
    name: rules.name,
    revision: 1,
    writable: true,
    owned: true,
    configurationKnown: true,
    resourceBudgetKnown: true,
    desired: rules,
    applied: rules,
    image: 'synthetic',
    gamePort: 18211,
    desiredPower: 'stopped',
  };
  vi.stubGlobal(
    'EventSource',
    class {
      close() {}
    },
  );
  vi.mocked(api).mockImplementation(async (path, options) => {
    if (path === '/setup') return { required: false };
    if (path === '/session') return { userName: 'admin', csrfToken: 'synthetic' };
    if (path === '/instances') return [saved];
    if (path === '/host') return { memoryMiB: 32768, reservedMemoryMiB: 4096, cpuCount: 8 };
    if (['/tasks', '/audit', '/backups'].includes(path)) return [];
    if (path.endsWith('/observations'))
      return { container: 'stopped', containerPresent: true, gameApi: 'unknown' };
    if (path === '/instances/synthetic/settings') {
      if (options?.method === 'PATCH') {
        if (fail) throw new ApiError(412, 'RevisionConflict', '配置已变化，保存失败');
        expect(new Headers(options.headers).get('If-Match')).toBe(String(saved.revision));
        saved = {
          ...saved,
          desired: JSON.parse(String(options.body)),
          revision: saved.revision + 1,
        };
        return saved;
      }
      return {
        desired: saved.desired,
        applied: rules,
        configured: {},
        observed: rules,
        status: 'observed',
      };
    }
    throw new Error('Unexpected request ' + path);
  });
  window.history.replaceState(null, '', '/instances/synthetic/settings');
  return () => saved;
}
async function edit(label: string, value: string) {
  await userEvent.click(await screen.findByRole('button', { name: '修改' + label }));
  const dialog = screen.getByRole('dialog');
  await userEvent.clear(within(dialog).getByLabelText(label));
  await userEvent.type(within(dialog).getByLabelText(label), value);
  await userEvent.click(within(dialog).getByRole('button', { name: '保存到草稿' }));
}
it('弹窗直接持久保存，连续修改使用新修订并保留之前字段，重新打开后草稿仍存在', async () => {
  const saved = fixture();
  const view = render(<App />);
  await edit('玩家上限', '20');
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(saved().desired.maxPlayers).toBe(20);
  await edit('经验获取倍率', '3');
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  expect(saved().desired).toMatchObject({
    maxPlayers: 20,
    additional: { PalCaptureRate: '1.5', ExpRate: '3' },
  });
  expect(screen.queryByRole('button', { name: '保存草稿' })).not.toBeInTheDocument();
  view.unmount();
  render(<App />);
  const row = await screen.findByRole('row', { name: /ServerPlayerMaxNum/ });
  expect(within(row).getByText('20')).toBeInTheDocument();
  expect(within(row).getByText('待应用')).toBeInTheDocument();
});
it('接口拒绝时保留弹窗输入，不将未保存值显示为已保存草稿', async () => {
  const saved = fixture(true);
  render(<App />);
  await edit('玩家上限', '20');
  expect(await screen.findByRole('alert')).toHaveTextContent('保存失败');
  expect(screen.getByLabelText('玩家上限')).toHaveValue(20);
  expect(saved().desired.maxPlayers).toBe(16);
  expect(
    vi.mocked(api).mock.calls.filter(([, options]) => options?.method === 'PATCH'),
  ).toHaveLength(1);
});
