import React, { useEffect, useRef, useState } from 'react';
import { ServerCog } from 'lucide-react';
import { api } from './api';
import { Modal } from './Modal';

type Snapshot = {
  host: {
    cpuPercent: number | null;
    usedMemoryBytes: number | null;
    availableMemoryBytes: number | null;
    updatedUtc: string;
  };
  instances: {
    id: string;
    name: string;
    status: {
      container: string;
      players: number | null;
      fps: number | null;
      cpu: number | null;
      memoryBytes: number | null;
      updatedUtc: string | null;
      stale: boolean;
    };
  }[];
  updatedUtc: string;
};
const time = (value?: string | null) => (value ? new Date(value).toLocaleString('zh-CN') : '未知');
const memory = (value?: number | null) =>
  value == null ? '未知' : `${(value / 1024 ** 3).toFixed(1)} GiB`;
const percent = (value?: number | null) => (value == null ? '未知' : `${value.toFixed(1)}%`);
const states: Record<string, string> = {
  running: '运行',
  stopped: '停止',
  exited: '停止',
  created: '未启动',
  paused: '暂停',
  restarting: '重启中',
  unknown: '未知',
};

export function ReadOnlyDashboard({
  login,
  onLoginRequest,
  management,
}: {
  login: React.ReactNode;
  onLoginRequest?: () => void;
  management?: React.ReactNode;
}) {
  const [data, setData] = useState<Snapshot | null>(null);
  const [error, setError] = useState('');
  const [loginOpen, setLoginOpen] = useState(false);
  const loginButton = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout>;
    const refresh = async () => {
      try {
        const snapshot = await api<Snapshot>('/dashboard', { signal: controller.signal });
        if (!controller.signal.aborted) {
          setData(snapshot);
          setError('');
        }
      } catch (error) {
        if (!controller.signal.aborted)
          setError(error instanceof Error ? error.message : '暂时无法读取仪表盘。');
      } finally {
        if (!controller.signal.aborted) timer = setTimeout(() => void refresh(), 5000);
      }
    };
    void refresh();
    return () => {
      controller.abort();
      clearTimeout(timer);
    };
  }, []);
  return (
    <div className="shell">
      <header className="topbar">
        <div className="brand">
          <ServerCog aria-hidden="true" />
          <span>
            PalworldPanel<small>独立世界 · 受控维护</small>
          </span>
        </div>
        <nav aria-label="主导航">
          <button className="nav-active" aria-current="page">
            仪表盘
          </button>
          {management}
        </nav>
        {!management && (
          <button
            ref={loginButton}
            className="primary"
            onClick={() => (onLoginRequest ? onLoginRequest() : setLoginOpen(true))}
          >
            登录
          </button>
        )}
      </header>
      <div className="page-scroll" role="region" aria-label="页面内容" tabIndex={0}>
        <main className="main">
          <div className="page-intro">
            <div>
              <h1>仪表盘</h1>
              <p className="muted">{management ? '只读状态总览' : '只读查看 · 登录后可管理实例'}</p>
            </div>
            <span className="muted">更新时间：{time(data?.updatedUtc)}</span>
          </div>
          {error && (
            <p role="alert" className="error">
              {error} 已显示的数据可能过期。
            </p>
          )}
          <div className="metrics">
            {[
              ['实例', data ? String(data.instances.length) : '未知'],
              ['CPU 使用率', percent(data?.host.cpuPercent)],
              ['已用内存', memory(data?.host.usedMemoryBytes)],
              ['可用内存', memory(data?.host.availableMemoryBytes)],
            ].map(([label, value]) => (
              <div className="metric" key={label}>
                <div>
                  <span>{label}</span>
                  <strong>{value}</strong>
                </div>
              </div>
            ))}
          </div>
          <section className="card" aria-labelledby="readonly-instances">
            <h2 id="readonly-instances">实例状态</h2>
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>实例</th>
                    <th>状态</th>
                    <th>玩家</th>
                    <th>FPS</th>
                    <th>CPU</th>
                    <th>内存</th>
                    <th>采样时间</th>
                  </tr>
                </thead>
                <tbody>
                  {data?.instances.map(({ id, name, status }) => (
                    <tr key={id}>
                      <th scope="row">{name}</th>
                      <td>
                        {states[status.container] || '未知'}
                        {status.stale ? ' · 过期' : ''}
                      </td>
                      <td>{status.players ?? '未知'}</td>
                      <td>{status.fps == null ? '未知' : status.fps.toFixed(1)}</td>
                      <td>{percent(status.cpu)}</td>
                      <td>{memory(status.memoryBytes)}</td>
                      <td>{time(status.updatedUtc)}</td>
                    </tr>
                  ))}
                  {!data && (
                    <tr>
                      <td colSpan={7}>正在读取实例状态…</td>
                    </tr>
                  )}
                  {data?.instances.length === 0 && (
                    <tr>
                      <td colSpan={7} className="muted">
                        暂无实例
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </section>
        </main>
      </div>
      <Modal
        open={loginOpen}
        onOpenChange={setLoginOpen}
        title="管理员登录"
        description="登录后可进行实例管理和维护操作。"
        returnFocus={loginButton}
      >
        {login}
      </Modal>
    </div>
  );
}
