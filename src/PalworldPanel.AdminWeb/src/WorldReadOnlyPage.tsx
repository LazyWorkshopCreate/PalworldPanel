import React, { useEffect, useRef, useState } from 'react';
import catalog from '../../PalworldPanel.Contracts/game-settings.json';
import { LoginLayout } from './LoginLayout';
import { navigate, RouteLink, usePanelRoute } from './router';

const defaults = new Map(
  catalog.filter((field) => !field.secret).map((field) => [field.key, field]),
);
function defaultValue(key: string): string {
  const field = defaults.get(key);
  if (!field || field.defaultValue === null) return '未知';
  if (key === 'ServerName') return '创建时填写';
  if (field.defaultValue === '') return '空';
  return field.type === 'number' ? String(Number(field.defaultValue)) : field.defaultValue;
}

type Snapshot = {
  name: string;
  worldId: string;
  gameBuild: string | null;
  updatedUtc: string;
  status: {
    container: string;
    players: number | null;
    fps: number | null;
    updatedUtc: string | null;
    stale: boolean;
  };
  settings: { key: string; label: string; applied: string | null; observed: string | null }[];
  settingsUnavailable: string | null;
  backups: { id: string; createdUtc: string; bytes: number; gameBuild: string | null }[];
};
class AccessError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}
async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await fetch(`/api/v1/world-view${path}`, {
    credentials: 'same-origin',
    ...init,
    headers: { 'Content-Type': 'application/json' },
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new AccessError(response.status, error.title || '读取失败，请重试。');
  }
  return response.status === 204 ? (undefined as T) : (response.json() as Promise<T>);
}
const time = (value: string | null) => (value ? new Date(value).toLocaleString('zh-CN') : '未知');
export function WorldReadOnlyPage({ worldId }: { worldId: string }) {
  const { route } = usePanelRoute();
  const tab = ['settings', 'backups', 'logs'].includes(route.tab) ? route.tab : 'settings';
  const [search, setSearch] = useState('');
  const [world, setWorld] = useState(worldId);
  const [password, setPassword] = useState('');
  const [data, setData] = useState<Snapshot | null>(null);
  const [logs, setLogs] = useState<{ text: string; updatedUtc: string } | null>(null);
  const [error, setError] = useState('');
  const [logError, setLogError] = useState('');
  const [busy, setBusy] = useState(false);
  const [revision, setRevision] = useState(0);
  const loginAbort = useRef<AbortController | null>(null);
  const readAbort = useRef<AbortController | null>(null);
  const mounted = useRef(true);
  const hasRead = useRef(false);
  useEffect(() => {
    mounted.current = true;
    document.title = '世界只读详情 · PalworldPanel';
    return () => {
      mounted.current = false;
      loginAbort.current?.abort();
      readAbort.current?.abort();
    };
  }, []);
  useEffect(() => {
    if (!worldId) return;
    const controller = new AbortController();
    readAbort.current = controller;
    let timer: ReturnType<typeof setTimeout>;
    let denied = false;
    const read = async () => {
      try {
        const value = await request<Snapshot>(`/${worldId}`, { signal: controller.signal });
        if (controller.signal.aborted) return;
        hasRead.current = true;
        setData(value);
        setError('');
        try {
          const value = await request<{ text: string; updatedUtc: string }>(`/${worldId}/logs`, {
            signal: controller.signal,
          });
          if (!controller.signal.aborted) {
            setLogs(value);
            setLogError('');
          }
        } catch (e) {
          if (controller.signal.aborted) return;
          if (e instanceof AccessError && e.status === 401) {
            denied = true;
            setData(null);
            setLogs(null);
            setError('只读会话已失效，请重新输入游戏密码。');
          } else {
            setLogs(null);
            setLogError(e instanceof Error ? e.message : '日志不可用。');
          }
        }
      } catch (e) {
        if (!controller.signal.aborted) {
          setData(null);
          setLogs(null);
          denied = e instanceof AccessError && e.status === 401;
          setError(
            e instanceof AccessError && e.status === 401
              ? hasRead.current
                ? '只读会话已失效，请重新输入游戏密码。'
                : ''
              : e instanceof Error
                ? e.message
                : '读取失败。',
          );
        }
      } finally {
        if (!controller.signal.aborted && !denied) timer = setTimeout(() => void read(), 30000);
      }
    };
    void read();
    return () => {
      controller.abort();
      clearTimeout(timer);
    };
  }, [worldId, revision]);
  async function login(event: React.FormEvent) {
    event.preventDefault();
    if (busy) return;
    const controller = new AbortController();
    loginAbort.current = controller;
    setBusy(true);
    setError('');
    try {
      const value = await request<{ worldId: string }>('/session', {
        method: 'POST',
        body: JSON.stringify({ worldId: world, password }),
        signal: controller.signal,
      });
      if (controller.signal.aborted || !mounted.current) return;
      setPassword('');
      if (value.worldId === worldId) setRevision((r) => r + 1);
      else navigate(`/world-view/${value.worldId}`);
    } catch (e) {
      if (!controller.signal.aborted && mounted.current)
        setError(e instanceof Error ? e.message : '登录失败。');
    } finally {
      if (mounted.current) setBusy(false);
    }
  }
  async function logout() {
    readAbort.current?.abort();
    setBusy(true);
    try {
      await request('/session', { method: 'DELETE' });
      if (mounted.current) {
        setData(null);
        setLogs(null);
        setPassword('');
        setError('已退出只读访问。');
      }
    } catch (e) {
      if (mounted.current) {
        setError(e instanceof Error ? e.message : '退出失败，请重试。');
        setRevision((r) => r + 1);
      }
    } finally {
      if (mounted.current) setBusy(false);
    }
  }
  const query = search.trim().toLowerCase();
  const visibleSettings =
    data?.settings.filter((s) => `${s.label} ${s.key}`.toLowerCase().includes(query)) ?? [];
  if (!data)
    return (
      <>
        <LoginLayout
          title="访问你的独立世界"
          description="输入世界 Id 和游戏连接密码，查看设置、日志与备份。"
          footer="只读查看 · 世界设置 · 日志与备份"
        >
          <form onSubmit={(e) => void login(e)}>
            <label>
              世界 Id
              <input
                required
                maxLength={36}
                value={world}
                onChange={(e) => setWorld(e.target.value)}
                autoComplete="off"
              />
            </label>
            <label>
              游戏连接密码
              <input
                required
                type="password"
                maxLength={128}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="off"
              />
            </label>
            {error && (
              <p role="alert" className="error">
                {error}
              </p>
            )}
            <button className="primary" disabled={busy} type="submit">
              {busy ? '验证中…' : '查看只读详情'}
            </button>
          </form>
        </LoginLayout>
        <RouteLink className="login-back" href="/dashboard">
          返回只读仪表盘
        </RouteLink>
      </>
    );
  return (
    <div className="shell world-view">
      <header className="topbar">
        <RouteLink href="/dashboard" className="brand">
          PalworldPanel
        </RouteLink>
        <span>世界只读详情</span>
      </header>
      <div className="page-scroll">
        <main className="main">
          <div className="page-intro">
            <div>
              <p className="eyebrow">PALWORLD WORLD VIEW</p>
              <h1>{data.name}</h1>
              <p className="muted">使用世界 Id 和已应用的游戏连接密码，仅查看和下载。</p>
            </div>
            {data && (
              <div className="actions">
                <button className="ghost" disabled={busy} onClick={() => setRevision((r) => r + 1)}>
                  刷新
                </button>
                <button className="ghost" disabled={busy} onClick={() => void logout()}>
                  退出只读访问
                </button>
              </div>
            )}
          </div>
          {error && (
            <p role="alert" className="notice">
              {error}
            </p>
          )}
          <>
            <section className="card">
              <h2>基本信息</h2>
              <dl className="facts">
                <div>
                  <dt>世界 Id</dt>
                  <dd>{data.worldId}</dd>
                </div>
                <div>
                  <dt>游戏版本</dt>
                  <dd>{data.gameBuild || '未知'}</dd>
                </div>
                <div>
                  <dt>状态</dt>
                  <dd>
                    {(
                      {
                        running: '运行中',
                        stopped: '已停止',
                        exited: '已停止',
                        unknown: '未知',
                      } as Record<string, string>
                    )[data.status.container] || data.status.container}
                    {data.status.stale ? '（观测过期或不可用）' : ''}
                  </dd>
                </div>
                <div>
                  <dt>在线玩家 / FPS</dt>
                  <dd>
                    {data.status.players ?? '未知'} / {data.status.fps ?? '未知'}
                  </dd>
                </div>
                <div>
                  <dt>采样时间</dt>
                  <dd>{time(data.status.updatedUtc)}</dd>
                </div>
              </dl>
            </section>
            <div
              className="tabs"
              role="tablist"
              aria-label="只读详情"
              onKeyDown={(event) => {
                if (!['ArrowRight', 'ArrowLeft', 'Home', 'End'].includes(event.key)) return;
                const tabs = Array.from(
                  event.currentTarget.querySelectorAll<HTMLAnchorElement>('[role="tab"]'),
                );
                const index = tabs.findIndex((element) => element === document.activeElement);
                const next =
                  event.key === 'Home'
                    ? 0
                    : event.key === 'End'
                      ? tabs.length - 1
                      : (index + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
                event.preventDefault();
                tabs[next]?.click();
                tabs[next]?.focus();
              }}
            >
              {[
                ['settings', '游戏设置'],
                ['backups', '世界备份'],
                ['logs', '最近日志'],
              ].map(([key, label]) => (
                <RouteLink
                  key={key}
                  role="tab"
                  id={`world-tab-${key}`}
                  aria-controls={`world-panel-${key}`}
                  aria-selected={tab === key}
                  tabIndex={tab === key ? 0 : -1}
                  className={tab === key ? 'active' : ''}
                  href={`/world-view/${worldId}/${key}`}
                >
                  {label}
                </RouteLink>
              ))}
            </div>
            <div
              role="tabpanel"
              id={`world-panel-${tab}`}
              aria-labelledby={`world-tab-${tab}`}
              tabIndex={0}
            >
              {tab === 'settings' && (
                <section className="card">
                  <h2 id="world-settings">游戏设置</h2>
                  <p className="muted">
                    当前值来自运行观测；默认值来自参数目录，未知值不代表默认值。
                  </p>
                  {data.settingsUnavailable && <p role="status">{data.settingsUnavailable}</p>}
                  <label className="parameter-search">
                    搜索参数
                    <input
                      type="search"
                      placeholder="搜索中文名或字段，如经验 / ExpRate"
                      value={search}
                      onChange={(event) => setSearch(event.target.value)}
                    />
                  </label>
                  <p className="muted" role="status">
                    {visibleSettings.length} 项参数
                  </p>
                  <div className="table-scroll world-settings">
                    <table>
                      <thead>
                        <tr>
                          <th>参数</th>
                          <th>当前值</th>
                          <th>默认值</th>
                        </tr>
                      </thead>
                      <tbody>
                        {visibleSettings.map((s) => (
                          <tr key={s.key}>
                            <td>
                              {s.label}
                              <br />
                              <code>{s.key}</code>
                            </td>
                            <td>{s.observed === '' ? '空' : (s.observed ?? '未知')}</td>
                            <td>{defaultValue(s.key)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                  {visibleSettings.length === 0 && (
                    <p>没有匹配的参数，请尝试其他中文名或字段名。</p>
                  )}
                </section>
              )}
              {tab === 'backups' && (
                <section className="card">
                  <h2 id="world-backups">世界备份</h2>
                  <p className="muted">
                    下载包含世界和玩家存档；不包含管理配置。此处只下载已有离线快照，不创建或恢复备份。
                  </p>
                  {data.backups.length === 0 ? (
                    <p>暂无同世界备份。</p>
                  ) : (
                    <div className="table-scroll">
                      <table>
                        <thead>
                          <tr>
                            <th>建立时间</th>
                            <th>原备份大小</th>
                            <th>下载</th>
                          </tr>
                        </thead>
                        <tbody>
                          {data.backups.map((b) => (
                            <tr key={b.id}>
                              <td>{time(b.createdUtc)}</td>
                              <td>{(b.bytes / 1024 ** 2).toFixed(1)} MiB</td>
                              <td>
                                <a
                                  href={`/api/v1/world-view/${data.worldId}/backups/${b.id}`}
                                  download
                                >
                                  下载世界存档 ZIP
                                </a>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </section>
              )}
              {tab === 'logs' && (
                <section className="card">
                  <h2 id="world-logs">最近日志</h2>
                  <p className="muted">
                    最多 200 行，已隐藏密码、玩家标识和内部路径。采样：
                    {time(logs?.updatedUtc || null)}
                  </p>
                  {logError && <p role="alert">{logError}</p>}
                  <pre className="logs">{logs?.text || '暂无日志。'}</pre>
                </section>
              )}
            </div>
          </>
        </main>
      </div>
    </div>
  );
}
