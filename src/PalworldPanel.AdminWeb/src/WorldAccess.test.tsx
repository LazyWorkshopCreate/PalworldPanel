import { afterEach, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { WorldAccess } from './WorldAccess';
import { api } from './api';

vi.mock('./api', async (original) => ({
  ...(await original<typeof import('./api')>()),
  api: vi.fn(),
}));
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.clearAllMocks();
});

const save = {
  name: 'Players/0123456789abcdef0123456789abcdef.sav',
  role: 'player',
  container: 'PlM',
  canIsolate: true,
  reason: null,
};
const other = {
  name: 'Players/abcdefabcdefabcdefabcdefabcdefab.sav',
  role: 'player',
  container: 'PlZ',
  canIsolate: true,
  reason: null,
};

function world(running: boolean) {
  vi.mocked(api).mockImplementation(async (path) => {
    if (String(path).endsWith('/world'))
      return {
        running,
        players: [
          ...(running
            ? [
                {
                  userId: 'steam_100',
                  name: '艾拉',
                  level: 12,
                  playerId: '0123456789abcdef0123456789abcdef',
                  online: true,
                  banned: false,
                },
              ]
            : []),
          {
            userId: 'steam_200',
            name: '柏',
            level: 30,
            playerId: null,
            online: false,
            banned: true,
          },
          {
            userId: null,
            name: null,
            level: null,
            playerId: 'abcdefabcdefabcdefabcdefabcdefab',
            online: false,
            banned: null,
          },
        ],
        playersUnavailable: running
          ? null
          : '实例未运行。列表仍包含曾经见到的玩家、封禁编号和现存的玩家存档。',
      };
    if (String(path).includes('/save-inventory')) return { files: [save, other] };
    return undefined;
  });
}

it('按封禁状态显示操作，展开后只显示对应存档', async () => {
  world(true);
  render(<WorldAccess instanceId="synthetic" userName="admin" disabled={false} />);
  expect(await screen.findByText('艾拉')).toBeInTheDocument();
  const aila = screen.getByText('艾拉').closest('tr')!;
  expect(aila).toHaveTextContent('未封禁');
  expect(
    within(aila).getByRole('button', { name: (value) => value === '封禁' }),
  ).toBeInTheDocument();
  expect(within(aila).getByRole('button', { name: '踢出' })).toBeInTheDocument();
  expect(within(aila).queryByRole('button', { name: '解除封禁' })).not.toBeInTheDocument();
  const banned = screen.getByText('柏').closest('tr')!;
  expect(banned).toHaveTextContent('已封禁');
  expect(banned).toHaveTextContent('离线');
  expect(within(banned).getByRole('button', { name: '解除封禁' })).toBeInTheDocument();
  expect(
    within(banned).queryByRole('button', { name: (value) => value === '封禁' }),
  ).not.toBeInTheDocument();
  expect(within(banned).queryByRole('button', { name: '踢出' })).not.toBeInTheDocument();
  expect(screen.getByText('无法判断')).toBeInTheDocument();
  expect(screen.queryByLabelText('提示信息')).not.toBeInTheDocument();
  await userEvent.click(within(aila).getByRole('button', { name: (value) => value === '封禁' }));
  const dialog = await screen.findByRole('dialog', { name: '封禁玩家' });
  expect(api).not.toHaveBeenCalledWith('/instances/synthetic/moderation', expect.anything());
  await userEvent.type(within(dialog).getByLabelText('提示信息'), '请勿再连接');
  await userEvent.click(within(dialog).getByRole('button', { name: '确认封禁' }));
  await userEvent.click(within(aila).getByRole('button', { name: '踢出' }));
  const kickDialog = await screen.findByRole('dialog', { name: '踢出玩家' });
  await userEvent.type(within(kickDialog).getByLabelText('提示信息'), '请稍后再进入');
  await userEvent.click(within(kickDialog).getByRole('button', { name: '确认踢出' }));
  expect(api).toHaveBeenCalledWith(
    '/instances/synthetic/moderation',
    expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ action: 'kick', userId: 'steam_100', message: '请稍后再进入' }),
    }),
  );
  expect(api).toHaveBeenCalledWith(
    '/instances/synthetic/moderation',
    expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ action: 'ban', userId: 'steam_100', message: '请勿再连接' }),
    }),
  );
  await userEvent.click(screen.getAllByRole('button', { name: '展开存档' })[0]);
  expect(screen.getByText(save.name)).toBeInTheDocument();
  expect(screen.queryByText(other.name)).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: '隔离' })).not.toBeInTheDocument();
});

it('停止时不显示踢出', async () => {
  world(false);
  render(<WorldAccess instanceId="synthetic" userName="admin" disabled={false} />);
  expect(await screen.findByText('柏')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: '踢出' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: '隔离' })).not.toBeInTheDocument();
});

it('切换实例清除旧玩家和确认弹窗，读取失败时不能误提交', async () => {
  world(true);
  const view = render(<WorldAccess instanceId="A" userName="admin" disabled={false} />);
  const player = await screen.findByText('艾拉');
  await userEvent.click(within(player.closest('tr')!).getByRole('button', { name: /^封禁$/ }));
  expect(screen.getByRole('dialog')).toBeInTheDocument();
  vi.mocked(api).mockImplementation(async () => {
    throw new Error('B unavailable');
  });
  view.rerender(<WorldAccess instanceId="B" userName="admin" disabled={false} />);
  expect(screen.queryByText('艾拉')).not.toBeInTheDocument();
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  await screen.findByText(/玩家列表暂时无法更新/);
  expect(screen.queryByRole('button', { name: /^封禁$/ })).not.toBeInTheDocument();
  expect(api).not.toHaveBeenCalledWith('/instances/B/moderation', expect.anything());
});

it('存档请求失败仍显示玩家并允许连接控制', async () => {
  world(true);
  const implementation = vi.mocked(api).getMockImplementation()!;
  vi.mocked(api).mockImplementation(async (path, init) => {
    if (String(path).endsWith('/save-inventory')) throw new Error('inventory unavailable');
    return implementation(path, init);
  });
  render(<WorldAccess instanceId="synthetic" userName="admin" disabled={false} />);
  const player = await screen.findByText('艾拉');
  await screen.findByText(/存档列表暂时无法更新/);
  expect(screen.queryByText('还没有可显示的玩家或存档')).not.toBeInTheDocument();
  await userEvent.click(within(player.closest('tr')!).getByRole('button', { name: /^封禁$/ }));
  await userEvent.click(screen.getByRole('button', { name: '确认封禁' }));
  expect(api).toHaveBeenCalledWith(
    '/instances/synthetic/moderation',
    expect.objectContaining({ method: 'POST' }),
  );
});

it('自动刷新上线和离线状态，失败后保留过期提示并在后续采样恢复', async () => {
  vi.useFakeTimers();
  world(false);
  render(<WorldAccess instanceId="synthetic" userName="admin" disabled={false} />);
  await act(async () => {});
  expect(screen.queryByText('艾拉')).not.toBeInTheDocument();
  world(true);
  await act(async () => {
    await vi.advanceTimersByTimeAsync(5000);
  });
  expect(screen.getByText('艾拉')).toBeInTheDocument();
  vi.mocked(api).mockRejectedValue(new Error('sampling unavailable'));
  await act(async () => {
    await vi.advanceTimersByTimeAsync(5000);
  });
  expect(screen.getByText(/已有信息可能过期/)).toBeInTheDocument();
  expect(
    within(screen.getByText('艾拉').closest('tr')!).getByRole('button', { name: '踢出' }),
  ).toBeDisabled();
  world(false);
  await act(async () => {
    await vi.advanceTimersByTimeAsync(5000);
  });
  expect(screen.queryByText(/已有信息可能过期/)).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: '踢出' })).not.toBeInTheDocument();
});

it('切换实例后忽略迟到响应并取消旧实例请求', async () => {
  let finish: (value: unknown) => void = () => {};
  let oldSignal: AbortSignal | undefined;
  vi.mocked(api).mockImplementation(async (path, init) => {
    if (path === '/instances/A/world') {
      oldSignal = init?.signal as AbortSignal;
      return new Promise((resolve) => {
        finish = resolve;
      });
    }
    if (String(path).endsWith('/world'))
      return { running: false, players: [], playersUnavailable: null };
    return { files: [] };
  });
  const view = render(<WorldAccess instanceId="A" userName="admin" disabled={false} />);
  view.rerender(<WorldAccess instanceId="B" userName="admin" disabled={false} />);
  await screen.findByText('还没有可显示的玩家或存档');
  expect(oldSignal?.aborted).toBe(true);
  await act(async () => {
    finish({
      running: true,
      players: [{ userId: 'steam_100', name: '旧实例玩家' }],
      playersUnavailable: null,
    });
  });
  expect(screen.queryByText('旧实例玩家')).not.toBeInTheDocument();
});
