import { afterEach, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { App } from './App';
import { WorldReadOnlyPage } from './WorldReadOnlyPage';
import { parseRoute } from './router';
const world = 'a'.repeat(32);
const snapshot = {
  name: '合成世界',
  worldId: world,
  gameBuild: 'synthetic',
  gamePort: 18211,
  updatedUtc: new Date().toISOString(),
  status: { container: 'stopped', players: null, fps: null, stale: false, updatedUtc: null },
  settings: [{ key: 'ExpRate', label: '经验倍率', applied: '2', observed: null }],
  settingsUnavailable: '运行设置不可用',
  backups: [{ id: 'backup-one', createdUtc: new Date().toISOString(), bytes: 42 }],
};
const response = (data: unknown, status = 200) =>
  ({ ok: status < 400, status, json: async () => data }) as Response;
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.clearAllMocks();
  window.history.replaceState(null, '', '/');
});
it('keeps the visitor entry separate from all administrator requests and supports deep links', async () => {
  const fetch = vi.fn().mockResolvedValue(response({}, 401));
  vi.stubGlobal('fetch', fetch);
  window.history.replaceState(null, '', `/world-view/${world}`);
  render(<App />);
  expect(await screen.findByLabelText('游戏连接密码')).toBeInTheDocument();
  expect(fetch.mock.calls.every(([path]) => String(path).startsWith('/api/v1/world-view/'))).toBe(
    true,
  );
  expect(parseRoute(`/world-view/${world}`).page).toBe('world-view');
  expect(parseRoute('/world-view').page).toBe('world-view');
  expect(parseRoute('/world-view/invalid').page).toBe('not-found');
});
it('sends the password only in POST body, clears it after login, shows safe data and downloads directly', async () => {
  let allowed = false;
  const fetch = vi.fn().mockImplementation(async (path: string, init?: RequestInit) => {
    if (init?.method === 'POST') {
      allowed = true;
      return response({ worldId: world });
    }
    if (!allowed) return response({}, 401);
    return response(
      path.endsWith('/logs')
        ? { text: 'synthetic log', updatedUtc: snapshot.updatedUtc }
        : snapshot,
    );
  });
  vi.stubGlobal('fetch', fetch);
  render(<WorldReadOnlyPage worldId={world} />);
  const user = userEvent.setup();
  await user.type(screen.getByLabelText('游戏连接密码'), 'synthetic-game');
  await user.click(screen.getByRole('button', { name: '查看只读详情' }));
  expect(await screen.findByRole('heading', { name: '合成世界' })).toBeInTheDocument();
  expect(screen.getByText('经验倍率')).toBeInTheDocument();
  await user.click(screen.getByRole('tab', { name: '最近日志' }));
  expect(await screen.findByText('synthetic log')).toBeInTheDocument();
  await user.click(screen.getByRole('tab', { name: '世界备份' }));
  expect(screen.getByRole('link', { name: '下载世界存档 ZIP' })).toHaveAttribute(
    'href',
    `/api/v1/world-view/${world}/backups/backup-one`,
  );
  const post = fetch.mock.calls.find(([, init]) => init?.method === 'POST')!;
  expect(JSON.parse(post[1].body)).toEqual({ worldId: world, password: 'synthetic-game' });
  expect(fetch.mock.calls.every(([path]) => !String(path).includes('synthetic-game'))).toBe(true);
  expect(screen.queryByLabelText('游戏连接密码')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: '启动' })).not.toBeInTheDocument();
});
it('reports wrong password and keeps the input available for retry', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockResolvedValue(response({ title: '世界 Id 或游戏连接密码不可用。' }, 401)),
  );
  render(<WorldReadOnlyPage worldId="" />);
  const user = userEvent.setup();
  await user.type(screen.getByLabelText('世界 Id'), world);
  await user.type(screen.getByLabelText('游戏连接密码'), 'wrong');
  await user.click(screen.getByRole('button', { name: '查看只读详情' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('世界 Id 或游戏连接密码不可用');
  expect(screen.getByLabelText('游戏连接密码')).toHaveValue('wrong');
});
it('exit cancels pending reads and prevents late data from restoring the detail', async () => {
  let resolveRead: (v: Response) => void = () => {};
  let reads = 0;
  vi.stubGlobal(
    'fetch',
    vi.fn().mockImplementation(async (path: string, init?: RequestInit) => {
      if (init?.method === 'DELETE') return response(null, 204);
      if (path.endsWith('/logs')) return response({ text: 'log', updatedUtc: snapshot.updatedUtc });
      if (++reads === 1) return response(snapshot);
      return new Promise<Response>((resolve) => {
        resolveRead = resolve;
      });
    }),
  );
  render(<WorldReadOnlyPage worldId={world} />);
  const user = userEvent.setup();
  await screen.findByRole('heading', { name: '合成世界' });
  await user.click(screen.getByRole('button', { name: '刷新' }));
  await user.click(screen.getByRole('button', { name: '退出只读访问' }));
  await screen.findByLabelText('游戏连接密码');
  await act(async () => resolveRead(response(snapshot)));
  expect(screen.queryByRole('heading', { name: '合成世界' })).not.toBeInTheDocument();
});
it('expired sessions remove the prior detail on refresh', async () => {
  let expired = false;
  vi.stubGlobal(
    'fetch',
    vi
      .fn()
      .mockImplementation(async (path: string) =>
        expired
          ? response({}, 401)
          : response(
              path.endsWith('/logs') ? { text: 'log', updatedUtc: snapshot.updatedUtc } : snapshot,
            ),
      ),
  );
  render(<WorldReadOnlyPage worldId={world} />);
  await screen.findByRole('heading', { name: '合成世界' });
  expired = true;
  await userEvent.setup().click(screen.getByRole('button', { name: '刷新' }));
  await waitFor(() =>
    expect(screen.queryByRole('heading', { name: '合成世界' })).not.toBeInTheDocument(),
  );
  expect(screen.getByLabelText('游戏连接密码')).toBeInTheDocument();
});

it('changing worlds clears the old detail and ignores a late response from the old world', async () => {
  const second = 'b'.repeat(32);
  let resolveOld: (value: Response) => void = () => {};
  const fetch = vi.fn().mockImplementation(async (path: string) => {
    if (path.includes(second)) return response({}, 401);
    return new Promise<Response>((resolve) => {
      resolveOld = resolve;
    });
  });
  vi.stubGlobal('fetch', fetch);
  window.history.replaceState(null, '', `/world-view/${world}`);
  render(<App />);
  await waitFor(() => expect(fetch).toHaveBeenCalled());
  await act(async () => {
    window.history.pushState(null, '', `/world-view/${second}`);
    window.dispatchEvent(new PopStateEvent('popstate'));
  });
  expect(screen.getByLabelText('世界 Id')).toHaveValue(second);
  await act(async () => resolveOld(response(snapshot)));
  expect(screen.queryByRole('heading', { name: '合成世界' })).not.toBeInTheDocument();
  expect(screen.getByLabelText('世界 Id')).toHaveValue(second);
});

it('filters settings by Chinese names and case insensitive field names with an empty result', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockImplementation(async (path: string) =>
      response(
        path.endsWith('/logs')
          ? { text: 'log', updatedUtc: snapshot.updatedUtc }
          : {
              ...snapshot,
              settings: [
                ...snapshot.settings,
                {
                  key: 'DayTimeSpeedRate',
                  label: '白天时间流逝倍率',
                  applied: '1',
                  observed: '1',
                },
              ],
            },
      ),
    ),
  );
  render(<WorldReadOnlyPage worldId={world} />);
  const user = userEvent.setup();
  const input = await screen.findByRole('searchbox', { name: '搜索参数' });
  await user.type(input, '经验');
  expect(screen.getByText('经验倍率')).toBeInTheDocument();
  expect(screen.queryByText('白天时间流逝倍率')).not.toBeInTheDocument();
  await user.clear(input);
  await user.type(input, '  eXpRaTe  ');
  expect(screen.getByText('经验倍率')).toBeInTheDocument();
  await user.clear(input);
  await user.type(input, 'no-match');
  expect(screen.getByText('没有匹配的参数，请尝试其他中文名或字段名。')).toBeInTheDocument();
  await user.clear(input);
  expect(screen.getByText('白天时间流逝倍率')).toBeInTheDocument();
});
it('uses URL tabs without remounting the world, keeps basic information visible and supports history and keyboard', async () => {
  const fetch = vi
    .fn()
    .mockImplementation(async (path: string) =>
      response(
        path.endsWith('/logs')
          ? { text: 'history log', updatedUtc: snapshot.updatedUtc }
          : snapshot,
      ),
    );
  vi.stubGlobal('fetch', fetch);
  window.history.replaceState(null, '', `/world-view/${world}/backups`);
  render(<App />);
  const user = userEvent.setup();
  expect(await screen.findByRole('link', { name: '下载世界存档 ZIP' })).toBeInTheDocument();
  expect(screen.getByRole('tab', { name: '世界备份' })).toHaveAttribute('aria-selected', 'true');
  expect(screen.getByRole('tab', { name: '最近日志' })).toHaveAttribute(
    'href',
    `/world-view/${world}/logs`,
  );
  expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();
  expect(screen.queryByText('游戏 UDP 端口')).not.toBeInTheDocument();
  expect(screen.queryByRole('navigation', { name: '只读详情内容' })).not.toBeInTheDocument();
  await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
  await user.click(screen.getByRole('tab', { name: '游戏设置' }));
  await user.type(screen.getByRole('searchbox'), '经验');
  await user.click(screen.getByRole('tab', { name: '最近日志' }));
  expect(window.location.pathname).toBe(`/world-view/${world}/logs`);
  expect(screen.getByRole('heading', { name: '基本信息' })).toBeInTheDocument();
  expect(screen.queryByRole('link', { name: '下载世界存档 ZIP' })).not.toBeInTheDocument();
  await act(async () => {
    window.history.back();
  });
  await waitFor(() => expect(screen.getByRole('searchbox')).toHaveValue('经验'));
  expect(fetch).toHaveBeenCalledTimes(2);
  screen.getByRole('tab', { name: '游戏设置' }).focus();
  await user.keyboard('{End}');
  expect(screen.getByRole('tab', { name: '最近日志' })).toHaveFocus();
  expect(screen.getByRole('tab', { name: '最近日志' })).toHaveAttribute('aria-selected', 'true');
  expect(parseRoute(`/world-view/${world}/tasks`).page).toBe('not-found');
});

it('shows observed current values and catalog defaults without substituting the applied baseline', async () => {
  vi.stubGlobal(
    'fetch',
    vi.fn().mockImplementation(async (path: string) =>
      response(
        path.endsWith('/logs')
          ? { text: '', updatedUtc: snapshot.updatedUtc }
          : {
              ...snapshot,
              settings: [
                { key: 'ExpRate', label: '经验倍率', applied: '9', observed: '3' },
                {
                  key: 'RandomizerSeed',
                  label: '随机种子',
                  applied: 'private-baseline',
                  observed: '',
                },
                { key: 'FutureSetting', label: '新参数', applied: '7', observed: null },
              ],
            },
      ),
    ),
  );
  render(<WorldReadOnlyPage worldId={world} />);
  await screen.findByRole('heading', { name: '游戏设置' });
  expect(screen.getAllByRole('columnheader').map((el) => el.textContent)).toEqual([
    '参数',
    '当前值',
    '默认值',
  ]);
  const row = screen.getByText('经验倍率').closest('tr')!;
  expect(
    within(row)
      .getAllByRole('cell')
      .map((el) => el.textContent),
  ).toEqual(['经验倍率ExpRate', '3', '1']);
  expect(
    within(screen.getByText('随机种子').closest('tr')!)
      .getAllByRole('cell')
      .map((el) => el.textContent),
  ).toEqual(['随机种子RandomizerSeed', '空', '空']);
  expect(
    within(screen.getByText('新参数').closest('tr')!)
      .getAllByRole('cell')
      .map((el) => el.textContent),
  ).toEqual(['新参数FutureSetting', '未知', '未知']);
  expect(screen.queryByText('private-baseline')).not.toBeInTheDocument();
});
