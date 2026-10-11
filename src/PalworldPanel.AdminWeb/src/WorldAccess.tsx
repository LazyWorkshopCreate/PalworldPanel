import { Fragment, useCallback, useEffect, useRef, useState } from 'react';
import { api, ApiError, setCsrf } from './api';
import { Modal } from './Modal';

type Player = {
  userId: string | null;
  name: string | null;
  level: number | null;
  playerId: string | null;
  online: boolean;
  banned: boolean | null;
};
type SaveFile = {
  name: string;
  role: string;
  container: string;
  reason: string | null;
};
type World = {
  running: boolean;
  players: Player[];
  playersUnavailable: string | null;
};
type Row = {
  key: string;
  title: string;
  detail: string;
  userId: string | null;
  banned: boolean | null;
  online: boolean;
  saves: SaveFile[];
};

const filePlayerId = (name: string) => {
  const base = name.slice(name.lastIndexOf('/') + 1);
  return base.toLowerCase().endsWith('.sav') ? base.slice(0, -4).toLowerCase() : '';
};

function rowsOf(world: World, files: SaveFile[]): Row[] {
  const used = new Set<string>();
  const rows = world.players.map((player) => {
    const saves = player.playerId
      ? files.filter((file) => filePlayerId(file.name) === player.playerId?.toLowerCase())
      : [];
    saves.forEach((file) => used.add(file.name));
    const identity = player.userId ?? `存档 ${player.playerId}`;
    return {
      key: player.userId ?? `save:${player.playerId}`,
      title: player.name || (player.userId ? '未命名' : identity),
      detail: player.userId
        ? `${player.userId}${player.level == null ? '' : ` · 等级 ${player.level}`} · ${player.online ? '在线' : '离线'}`
        : '没有对应的用户编号',
      userId: player.userId,
      banned: player.banned,
      online: player.online,
      saves,
    };
  });
  for (const file of files) {
    if (used.has(file.name)) continue;
    const id = filePlayerId(file.name);
    const key = `save:${id}`;
    const existing = rows.find((row) => row.key === key);
    if (existing) {
      existing.saves.push(file);
      used.add(file.name);
      continue;
    }
    used.add(file.name);
    rows.push({
      key,
      title: `存档 ${id}`,
      detail: '没有对应的用户编号',
      userId: null,
      banned: null,
      online: false,
      saves: [file],
    });
  }
  return rows;
}

type WorldAccessProps = {
  instanceId: string;
  userName: string;
  disabled: boolean;
};

export function WorldAccess(props: WorldAccessProps) {
  return <WorldAccessContent key={props.instanceId} {...props} />;
}

function WorldAccessContent({ instanceId, userName, disabled }: WorldAccessProps) {
  const [world, setWorld] = useState<World | null>(null);
  const [files, setFiles] = useState<SaveFile[]>([]);
  const [open, setOpen] = useState<string | null>(null);
  const [noticeDraft, setNoticeDraft] = useState<{
    action: 'kick' | 'ban';
    userId: string;
    title: string;
    message: string;
  } | null>(null);
  const [password, setPassword] = useState('');
  const [reauthenticate, setReauthenticate] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [worldError, setWorldError] = useState('');
  const [inventoryError, setInventoryError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const pending = useRef(false);
  const lifetime = useRef<AbortController | null>(null);
  const generation = useRef(0);
  const load = useCallback(
    async (signal: AbortSignal) => {
      if (signal.aborted) return;
      const current = ++generation.current;
      const active = () => !signal.aborted && current === generation.current;
      await Promise.all([
        api<World>(`/instances/${instanceId}/world`, { signal })
          .then((access) => {
            if (active()) {
              setWorld(access);
              setWorldError('');
            }
          })
          .catch((failure: unknown) => {
            if (active())
              setWorldError(failure instanceof Error ? failure.message : '玩家列表读取失败。');
          }),
        api<{ files: SaveFile[] }>(`/instances/${instanceId}/save-inventory`, { signal })
          .then((inventory) => {
            if (active()) {
              setFiles(inventory.files.filter((file) => file.role === 'player'));
              setInventoryError('');
            }
          })
          .catch((failure: unknown) => {
            if (active())
              setInventoryError(failure instanceof Error ? failure.message : '存档列表读取失败。');
          }),
      ]);
    },
    [instanceId],
  );
  useEffect(() => {
    const controller = new AbortController();
    lifetime.current = controller;
    let timer: ReturnType<typeof setTimeout>;
    const poll = async () => {
      if (!pending.current) await load(controller.signal);
      if (!controller.signal.aborted) timer = setTimeout(() => void poll(), 5000);
    };
    void poll();
    return () => {
      controller.abort();
      clearTimeout(timer);
    };
  }, [load]);
  const actionsDisabled = disabled || submitting || !world || !!worldError;
  const moderate = async (
    action: 'kick' | 'ban' | 'unban',
    userId: string,
    message: string | null,
  ) => {
    const signal = lifetime.current?.signal;
    if (actionsDisabled || pending.current || !signal || signal.aborted) return;
    pending.current = true;
    setSubmitting(true);
    ++generation.current;
    setError('');
    setNotice('');
    try {
      if (reauthenticate) {
        await api('/session', {
          method: 'POST',
          body: JSON.stringify({ userName, password }),
          signal,
        });
        if (signal.aborted) return;
        const session = await api<{ csrfToken: string }>('/session', { signal });
        if (signal.aborted) return;
        setCsrf(session.csrfToken);
        setReauthenticate(false);
      }
      await api(`/instances/${instanceId}/moderation`, {
        method: 'POST',
        body: JSON.stringify({ action, userId, message }),
        signal,
      });
      if (signal.aborted) return;
      if (action === 'kick' || action === 'ban') setNoticeDraft(null);
      setNotice(action === 'kick' ? '已踢出玩家，对方可以再次连接。' : '连接控制已提交。');
      await load(signal);
    } catch (failure) {
      if (signal.aborted) return;
      if (failure instanceof ApiError && failure.code === 'ReauthenticationRequired') {
        setReauthenticate(true);
        setError('需要验证面板管理员身份后才能控制连接。');
      } else setError(failure instanceof Error ? failure.message : '连接控制失败。');
    } finally {
      pending.current = false;
      setSubmitting(false);
      setPassword('');
    }
  };
  const rows = world ? rowsOf(world, files) : [];
  return (
    <section className="card" aria-labelledby="world-access-title">
      <h2 id="world-access-title">谁可以连接</h2>
      <p className="muted">
        每一行是一名已知玩家。状态只表示本实例是否已封禁。展开后是该玩家仍在世界中的存档文件。
      </p>
      {world?.playersUnavailable && <p role="status">{world.playersUnavailable}</p>}
      {worldError && <p role="status">玩家列表暂时无法更新；已有信息可能过期。{worldError}</p>}
      {inventoryError && (
        <p role="status">存档列表暂时无法更新；存档信息可能过期。{inventoryError}</p>
      )}
      {reauthenticate && !noticeDraft && (
        <label>
          面板管理员密码
          <input
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </label>
      )}
      {error && <p role="alert">{error}</p>}
      {notice && <p role="status">{notice}</p>}
      {rows.length === 0 ? (
        <p className="empty-state">
          {!world
            ? worldError
              ? '玩家列表暂不可用'
              : '正在读取玩家列表…'
            : inventoryError
              ? '已读取玩家列表，存档列表暂不可用'
              : '还没有可显示的玩家或存档'}
        </p>
      ) : (
        <div className="table-scroll">
          <table className="player-table">
            <caption className="muted">玩家与封禁状态</caption>
            <thead>
              <tr>
                <th>玩家</th>
                <th>状态</th>
                <th>操作</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => {
                const expanded = open === row.key;
                return (
                  <Fragment key={row.key}>
                    <tr>
                      <td>
                        <strong>{row.title}</strong>
                        <div className="muted">{row.detail}</div>
                      </td>
                      <td>{row.banned === null ? '无法判断' : row.banned ? '已封禁' : '未封禁'}</td>
                      <td>
                        <div className="actions">
                          {row.userId && row.online && world?.running && (
                            <button
                              className="ghost"
                              disabled={actionsDisabled}
                              onClick={() =>
                                setNoticeDraft({
                                  action: 'kick',
                                  userId: row.userId!,
                                  title: row.title,
                                  message: '',
                                })
                              }
                            >
                              踢出
                            </button>
                          )}
                          {row.userId && row.banned === false && (
                            <button
                              className="ghost"
                              disabled={actionsDisabled}
                              onClick={() =>
                                setNoticeDraft({
                                  action: 'ban',
                                  userId: row.userId!,
                                  title: row.title,
                                  message: '',
                                })
                              }
                            >
                              封禁
                            </button>
                          )}
                          {row.userId && row.banned === true && (
                            <button
                              className="ghost"
                              disabled={actionsDisabled}
                              onClick={() => void moderate('unban', row.userId!, null)}
                            >
                              解除封禁
                            </button>
                          )}
                          <button
                            className="text-button"
                            aria-expanded={expanded}
                            onClick={() => setOpen(expanded ? null : row.key)}
                          >
                            {expanded ? '收起存档' : '展开存档'}
                          </button>
                        </div>
                      </td>
                    </tr>
                    {expanded && (
                      <tr>
                        <td colSpan={3}>
                          {row.saves.length === 0 ? (
                            <p className="empty-state">没有对应的玩家存档。</p>
                          ) : (
                            <table>
                              <caption className="muted">{row.title} 的存档</caption>
                              <thead>
                                <tr>
                                  <th>文件</th>
                                  <th>容器</th>
                                </tr>
                              </thead>
                              <tbody>
                                {row.saves.map((file) => (
                                  <tr key={file.name}>
                                    <td>{file.name}</td>
                                    <td>{file.container}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          )}
                        </td>
                      </tr>
                    )}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      <Modal
        open={noticeDraft !== null}
        onOpenChange={(next) => {
          if (!next) setNoticeDraft(null);
        }}
        title={noticeDraft?.action === 'kick' ? '踢出玩家' : '封禁玩家'}
        description={
          noticeDraft?.action === 'kick'
            ? '这句会显示给被踢出的玩家。可以留空。对方之后仍可以连接。'
            : '实例运行时，这句会显示给被封禁的玩家。可以留空。实例停止时只写入本实例封禁列表。'
        }
      >
        {noticeDraft && (
          <form
            onSubmit={(event) => {
              event.preventDefault();
              const text = noticeDraft.message.trim();
              void moderate(noticeDraft.action, noticeDraft.userId, text.length > 0 ? text : null);
            }}
          >
            <p>
              {noticeDraft.title} · {noticeDraft.userId}
            </p>
            <label>
              提示信息
              <input
                value={noticeDraft.message}
                maxLength={80}
                onChange={(event) =>
                  setNoticeDraft({ ...noticeDraft, message: event.target.value })
                }
              />
            </label>
            {reauthenticate && (
              <label>
                面板管理员密码
                <input
                  type="password"
                  autoComplete="current-password"
                  value={password}
                  onChange={(event) => setPassword(event.target.value)}
                />
              </label>
            )}
            <div className="actions">
              <button type="button" className="ghost" onClick={() => setNoticeDraft(null)}>
                取消
              </button>
              <button className="primary" disabled={actionsDisabled}>
                {noticeDraft.action === 'kick' ? '确认踢出' : '确认封禁'}
              </button>
            </div>
          </form>
        )}
      </Modal>
    </section>
  );
}
