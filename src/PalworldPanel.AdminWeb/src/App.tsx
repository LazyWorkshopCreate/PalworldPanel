import React, { useCallback, useEffect, useState } from 'react';
import { Modal } from './Modal';
import { ActionConfirmation, type ActionRequest } from './ActionConfirmation';
import { PasswordDialog } from './PasswordDialog';
import { PasswordStatusTable, type InstancePasswordStates } from './PasswordStatusTable';
import { MoreActions } from './MoreActions';
import { ParameterSettings } from './ParameterSettings';
import { ReadOnlyDashboard } from './ReadOnlyDashboard';
import { InstanceInitialization } from './InstanceInitialization';
import {
  Activity,
  ArrowLeft,
  DatabaseBackup,
  LogOut,
  Plus,
  RefreshCw,
  ServerCog,
  ShieldCheck,
} from 'lucide-react';
import { api, ApiError, commandKey, setCsrf, downloadEncryptedBackup } from './api';
import './styles.css';

type Rules = {
  name: string;
  description: string;
  maxPlayers: number;
  deathPenalty: string;
  offlinePenalty: boolean;
  deteriorationRate: number;
  attackDamageRate: number;
  cpu: number;
  memoryMiB: number;
  additional?: Record<string, string> | null;
};
type Candidate = {
  containerId: string;
  name: string;
  image: string;
  root: string | null;
  registered: boolean;
  capability: string;
  reason: string;
};
type Instance = {
  resourceBudgetKnown: boolean;
  configurationKnown: boolean;
  quarantinedUtc: string | null;
  purgedUtc: string | null;
  image: string;
  backupTime: string;
  retentionDays: number;
  id: string;
  name: string;
  writable: boolean;
  owned: boolean;
  revision: number;
  worldGuid: string | null;
  gameBuild: string | null;
  gamePort: number | null;
  gameAddress?: string | null;
  desiredPower: string;
  desired: Rules;
  applied: Rules | null;
};
type TaskItem = {
  recoveryPoint: string | null;
  id: string;
  instanceId: string;
  kind: string;
  state: string;
  phase: string;
  safeCode: string | null;
  message: string | null;
  createdUtc: string;
};
type Host = {
  systemMemoryBytes?: number | null;
  systemCpuCount?: number | null;
  usedMemoryBytes: number | null;
  availableMemoryBytes: number | null;
  cpuPercent: number | null;
  systemSampleUtc: string;
  unlimitedRunning: number;
  externalMemoryMiB: number;
  externalCpu: number;
  memoryMiB: number;
  cpuCount: number;
  committedMemoryMiB: number;
  committedCpu: number;
  reservedMemoryMiB: number;
};
type Observation = {
  publishedUdp: { address: string; port: string }[] | null;
  containerUdp: string;
  hostUdp: string;
  container: string;
  gameApi: string;
  worldGuid: string | null;
  players: number | null;
  fps: number | null;
  days: number | null;
  bases: number | null;
  updatedUtc: string | null;
  stale: boolean;
  cpu: number | null;
  memoryBytes: number | null;
};
type Backup = {
  id: string;
  createdUtc: string;
  sha256: string;
  bytes: number;
  purpose: string;
  protected: boolean;
  containsInstallation: boolean;
};
type AuditEntry = {
  utc: string;
  user: string;
  instanceId: string | null;
  action: string;
  result: string;
  taskId: string | null;
};
const labels: Record<string, string> = {
  Queued: '排队中',
  Running: '执行中',
  Succeeded: '已完成',
  Failed: '失败',
  NeedsAttention: '需要处理',
  Cancelled: '已取消',
  RolledBack: '已回退',
  running: '运行',
  stopped: '停止',
  exited: '停止',
  healthy: '健康',
  unreachable: '无法访问',
  unauthorized: '凭据异常',
  unknown: '未验证',
  create: '创建世界',
  start: '启动',
  stop: '停止',
  restart: '重启',
  save: '保存',
  backup: '离线备份',
  'apply-config': '应用配置',
  import: '导入世界',
  restore: '恢复',
  upgrade: '升级',
  'retain-data': '移除容器保留数据',
  unmanage: '取消接管',
  paused: '已暂停',
  created: '尚未启动',
  restarting: '重启中',
  removing: '移除中',
  Preflight: '预检',
  Save: '保存',
  RestartHold: '禁止自动重启',
  Stop: '停服',
  OfflineBackup: '离线备份',
  RecoveryPointVerified: '恢复点已校验',
  InstallAndStart: '安装并启动',
  WaitForInstallSlot: '等待安装资源',
  PrepareConfiguration: '准备实例配置',
  ValidateStartup: '验证游戏就绪',
  FinalizeInitialization: '完成初始化',
  Start: '启动验证',
  ApplyConfig: '应用配置',
  StageWorld: '暂存世界',
  SwapPrepared: '准备交换',
  OldMoved: '旧存档已保留',
  NewInstalled: '新存档已安装',
  UpgradeInstallation: '升级安装',
  Complete: '完成',
  PlayerVerification: '等待玩家核验',
  RollbackStop: '回退前停止',
  RollbackComplete: '回退完成',
  Quarantined: '已隔离',
  Reviewed: '已核实解锁',
  manual: '手动备份',
  scheduled: '定时备份',
  'pre-rollback': '回退前快照',
  image: '镜像',
  game: '游戏',
  both: '镜像与游戏',
  purge: '隔离清理',
  adopt: '写管理接管',
  'undo-quarantine': '撤销隔离',
  'finalize-purge': '保留期后清空',
  Prepared: '已准备加密文件',
};
const describe = (value: string) => labels[value] || value;
const date = (value: string) =>
  new Date(value).toLocaleString('zh-CN', { timeZone: 'Asia/Shanghai' });
const errorText = (error: unknown) =>
  error instanceof Error ? error.message : '请求未完成，请检查任务状态。';

export function FirstAccessSetup({ onComplete }: { onComplete: () => void }) {
  const [password, setPassword] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  return (
    <main className="login-page">
      <div className="login-card">
        <div className="brand">
          <ServerCog aria-hidden="true" />
          <span>PalworldPanel</span>
        </div>
        <h1>设置管理员密码</h1>
        <p className="muted">首次使用，请为管理员账号 admin 设置密码。</p>
        <form
          onSubmit={async (event) => {
            event.preventDefault();
            setError('');
            if (password !== confirmation) {
              setError('两次输入的密码不一致。');
              return;
            }
            setBusy(true);
            try {
              const status = await api<{ required: boolean; token: string | null }>('/setup');
              if (status.required)
                await api('/setup', {
                  method: 'POST',
                  body: JSON.stringify({ token: status.token, password, confirmation }),
                });
              setPassword('');
              setConfirmation('');
              onComplete();
            } catch (e) {
              setError(errorText(e));
            } finally {
              setBusy(false);
            }
          }}
        >
          <label>
            管理员账号
            <input value="admin" readOnly autoComplete="username" />
          </label>
          <label>
            管理员密码
            <input
              type="password"
              autoComplete="new-password"
              minLength={16}
              maxLength={128}
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
            />
          </label>
          <label>
            确认密码
            <input
              type="password"
              autoComplete="new-password"
              minLength={16}
              maxLength={128}
              required
              value={confirmation}
              onChange={(e) => setConfirmation(e.target.value)}
            />
          </label>
          <p className="muted">密码长度为 16–128 个字符。</p>
          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}
          <button className="primary" disabled={busy}>
            {busy ? '正在设置…' : '设置密码'}
          </button>
        </form>
      </div>
    </main>
  );
}

export function Login({
  onLogin,
  embedded = false,
}: {
  onLogin: () => Promise<void>;
  embedded?: boolean;
}) {
  const [user, setUser] = useState('admin');
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const Container = embedded ? 'section' : 'main';
  return (
    <Container className={embedded ? 'login-panel' : 'login-page'}>
      <div className="login-card">
        <div className="brand">
          <ServerCog aria-hidden="true" />
          <span>PalworldPanel</span>
        </div>
        <h1>管理你的独立世界</h1>
        <p className="muted">仅允许白名单内网地址访问。登录后管理实例、任务和恢复点。</p>
        <form
          onSubmit={async (event) => {
            event.preventDefault();
            setBusy(true);
            setError('');
            try {
              await api('/session', {
                method: 'POST',
                body: JSON.stringify({ userName: user, password }),
              });
              setPassword('');
              await onLogin();
            } catch (e) {
              setError(errorText(e));
            } finally {
              setBusy(false);
            }
          }}
        >
          <label>
            管理员账号
            <input
              autoComplete="username"
              value={user}
              onChange={(e) => setUser(e.target.value)}
              required
            />
          </label>
          <label>
            管理员密码
            <input
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
          </label>
          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}
          <button className="primary" disabled={busy}>
            {busy ? '正在登录…' : '登录面板'}
          </button>
        </form>
        <p className="security-note">
          <ShieldCheck size={16} aria-hidden="true" /> 内网访问 · 受控操作 · 独立恢复点
        </p>
      </div>
    </Container>
  );
}

function Badge({ state }: { state: string }) {
  return (
    <span
      className={`badge ${['Succeeded', 'healthy', 'running'].includes(state) ? 'success' : ['NeedsAttention', 'Failed', 'unauthorized'].includes(state) ? 'warning' : 'neutral'}`}
    >
      {describe(state)}
    </span>
  );
}

export function App() {
  const actionOpener = React.useRef<HTMLElement | null>(null);
  const recoveryOpener = React.useRef<HTMLElement | null>(null);
  const [session, setSession] = useState<string | null>(null);
  const [initializing, setInitializing] = useState(true);
  const [setupRequired, setSetupRequired] = useState(false);
  const [setupFinished, setSetupFinished] = useState(false);
  const [setupLoadFailed, setSetupLoadFailed] = useState(false);
  const [instances, setInstances] = useState<Instance[]>([]);
  const [tasks, setTasks] = useState<TaskItem[]>([]);
  const [pendingApplications, setPendingApplications] = useState<Record<string, string>>({});
  const [host, setHost] = useState<Host | null>(null);
  const [audit, setAudit] = useState<AuditEntry[]>([]);
  const [error, setError] = useState('');
  const [page, setPage] = useState('instances');
  const [selected, setSelected] = useState<string | null>(null);
  const [tab, setTab] = useState('overview');
  const [observed, setObserved] = useState<Observation | null>(null);
  const [backups, setBackups] = useState<Backup[]>([]);
  const [allBackups, setAllBackups] = useState<(Backup & { instanceId: string })[]>([]);
  const [settingsObserved, setSettingsObserved] = useState<{
    instanceId: string;
    desired: Rules;
    applied: Rules | null;
    observed: Partial<Rules> | null;
    configured: Record<string, string> | null;
    passwordStates?: InstancePasswordStates;
    status: string;
    updatedUtc: string;
  } | null>(null);
  const [logs, setLogs] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [creationPreview, setCreationPreview] = useState<{
    plan: {
      id: string;
      root: string;
      backupRoot: string;
      gamePort: number;
      restPort: number;
      queryPort: number;
      backupTime: string;
      rules: Rules;
    };
    token: string;
    hash: string;
  } | null>(null);
  const creationKey = React.useRef('');
  React.useEffect(() => {
    if (!createOpen) setCreationPreview(null);
  }, [createOpen]);
  const [cloneId, setCloneId] = useState<string | null>(null);
  const [upgradeOpen, setUpgradeOpen] = useState(false);
  const [approvedImages, setApprovedImages] = useState<string[]>([]);
  const [upgradeImage, setUpgradeImage] = useState('');
  const [upgradeMode, setUpgradeMode] = useState('image');
  const [expectedBuild, setExpectedBuild] = useState('');
  const [adoptOpen, setAdoptOpen] = useState(false);
  const [externalSchedulesConfirmed, setExternalSchedulesConfirmed] = useState(false);
  const [discovered, setDiscovered] = useState<Candidate[] | null>(null);
  const [draft, setDraft] = useState<Rules>({
    name: '',
    description: '',
    maxPlayers: 16,
    deathPenalty: 'None',
    offlinePenalty: false,
    deteriorationRate: 0,
    attackDamageRate: 1,
    cpu: 4,
    memoryMiB: 16384,
  });
  const [busy, setBusy] = useState(false);
  const [confirmation, setConfirmation] = useState<ActionRequest | null>(null);
  const [cloneConfirmation, setCloneConfirmation] = useState<Instance | null>(null);
  const [zipPreview, setZipPreview] = useState<{
    uploadId: string;
    sha256: string;
    worlds: string[];
    expandedBytes: number;
    ignored: string[];
  } | null>(null);
  const [worldSelection, setWorldSelection] = useState('');
  const [recovery, setRecovery] = useState<{
    task: TaskItem;
    resolution: string;
    token: string;
    hash: string;
  } | null>(null);
  const [playersVerified, setPlayersVerified] = useState(false);
  const [exportBackup, setExportBackup] = useState<{ instanceId: string; backupId: string } | null>(
    null,
  );
  const [exportPassphrase, setExportPassphrase] = useState('');
  const [exportError, setExportError] = useState('');
  useEffect(() => {
    setExportError('');
  }, [exportBackup]);
  const [secretEditing, setSecretEditing] = useState<'administrator' | 'game' | null>(null);
  const [settingsSearch, setSettingsSearch] = useState('');
  const secretOpener = React.useRef<HTMLElement | null>(null);
  useEffect(() => {
    setSettingsSearch('');
    setZipPreview(null);
    setWorldSelection('');
  }, [selected]);
  const [lastUpdate, setLastUpdate] = useState<string | null>(null);
  const instance = instances.find((i) => i.id === selected);
  const instanceTaskLocked = tasks.some(
    (task) =>
      task.instanceId === selected && ['Queued', 'Running', 'NeedsAttention'].includes(task.state),
  );
  const applicationTask = tasks.find(
    (task) =>
      task.instanceId === selected &&
      (task.id === pendingApplications[selected || ''] ||
        (task.kind === 'apply-config' &&
          ['Queued', 'Running', 'NeedsAttention'].includes(task.state))),
  );
  const applicationLocked =
    !!instance &&
    (!!pendingApplications[instance.id] ||
      (!!applicationTask &&
        ['Queued', 'Running', 'NeedsAttention'].includes(applicationTask.state)) ||
      (confirmation?.kind === 'apply-config' && confirmation.instance.id === instance.id));
  const initializationTask = (id: string) =>
    tasks.find((task) => task.instanceId === id && task.kind === 'create');
  const loadSession = useCallback(async () => {
    const result = await api<{ userName: string; csrfToken: string }>('/session');
    setCsrf(result.csrfToken);
    setSession(result.userName);
  }, []);
  const refresh = useCallback(async () => {
    try {
      const [items, jobs, capacity, recoveryPoints, auditEntries] = await Promise.all([
        api<Instance[]>('/instances'),
        api<TaskItem[]>('/tasks'),
        api<Host>('/host'),
        api<(Backup & { instanceId: string })[]>('/backups'),
        api<AuditEntry[]>('/audit'),
      ]);
      setInstances(items);
      setTasks(jobs);
      setPendingApplications((previous) =>
        Object.fromEntries(
          Object.entries(previous).filter(
            ([, id]) =>
              !jobs.some(
                (task) =>
                  task.id === id &&
                  ['Succeeded', 'Failed', 'Cancelled', 'RolledBack'].includes(task.state),
              ),
          ),
        ),
      );
      setHost(capacity);
      setAllBackups(recoveryPoints);
      setAudit(auditEntries);
      setLastUpdate(new Date().toISOString());
      setError('');
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        setSession(null);
        setCsrf('');
      } else setError(errorText(e));
    }
  }, []);
  useEffect(() => {
    if (!session || !selected) return;
    const events = new EventSource(`/api/v1/instances/${selected}/events`);
    events.onmessage = () => {
      void refresh();
    };
    return () => events.close();
  }, [session, selected, refresh]);
  useEffect(() => {
    api<{ required: boolean }>('/setup')
      .then((status) => {
        setSetupRequired(status.required);
        if (!status.required) return loadSession();
      })
      .catch((e) => {
        if (!(e instanceof ApiError && e.status === 401)) {
          setError(errorText(e));
          setSetupLoadFailed(true);
        }
      })
      .finally(() => setInitializing(false));
  }, [loadSession]);
  useEffect(() => {
    if (!session) return;
    void refresh();
    const timer = setInterval(() => void refresh(), 5000);
    return () => clearInterval(timer);
  }, [session, refresh]);
  useEffect(() => {
    if (!instance || !session) return;
    const controller = new AbortController();
    const load = async () => {
      try {
        if (tab === 'overview')
          setObserved(
            await api<Observation>(`/instances/${instance.id}/observations`, {
              signal: controller.signal,
            }),
          );
        if (tab === 'backups')
          setBackups(
            await api<Backup[]>(`/instances/${instance.id}/backups`, { signal: controller.signal }),
          );
        if (tab === 'settings') {
          const settings = await api<Omit<NonNullable<typeof settingsObserved>, 'instanceId'>>(
            `/instances/${instance.id}/settings`,
            { signal: controller.signal },
          );
          if (!controller.signal.aborted)
            setSettingsObserved({ ...settings, instanceId: instance.id });
        }
        if (tab === 'logs') {
          const data = await api<{ text: string }>(`/instances/${instance.id}/logs`, {
            signal: controller.signal,
          });
          setLogs(data.text);
        }
      } catch (e) {
        if (!controller.signal.aborted) setError(errorText(e));
      }
    };
    void load();
    const timer = setInterval(() => void load(), 5000);
    return () => {
      clearInterval(timer);
      controller.abort();
    };
  }, [instance?.id, tab, session]);

  const requestAction = async (item: Instance, kind: string, args?: Record<string, unknown>) => {
    if (kind === 'apply-config' && applicationLocked) return;
    actionOpener.current =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;
    setError('');
    setConfirmation({ instance: item, kind, arguments: args });
  };
  const requestRecovery = async (task: TaskItem, resolution: string) => {
    recoveryOpener.current =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;
    setBusy(true);
    try {
      const preview = await api<{ token: string; hash: string }>(
        `/tasks/${task.id}/recovery-preview`,
        {
          method: 'POST',
          body: JSON.stringify({
            resolution,
            originalPlayersVerified: resolution === 'confirm-players',
          }),
        },
      );
      setPlayersVerified(false);
      setRecovery({ task, resolution, token: preview.token, hash: preview.hash });
    } catch (error) {
      setError(errorText(error));
    } finally {
      setBusy(false);
    }
  };
  const cancelTask = async (task: TaskItem) => {
    try {
      await api(`/tasks/${task.id}/cancel`, { method: 'POST' });
      await refresh();
    } catch (error) {
      setError(errorText(error));
    }
  };
  if (initializing)
    return (
      <main className="login-page">
        <p>正在核验会话…</p>
      </main>
    );
  if (setupLoadFailed)
    return (
      <main className="login-page">
        <p role="alert" className="error">
          {error}
        </p>
        <button onClick={() => window.location.reload()}>重试</button>
      </main>
    );
  if (setupRequired)
    return (
      <FirstAccessSetup
        onComplete={() => {
          setSetupRequired(false);
          setSetupFinished(true);
        }}
      />
    );
  if (setupFinished && !session) return <Login onLogin={loadSession} />;
  if (!session) return <ReadOnlyDashboard login={<Login embedded onLogin={loadSession} />} />;
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
          {[
            ['instances', '实例'],
            ['host', '主机与安全'],
          ].map(([key, label]) => (
            <button
              key={key}
              className={page === key ? 'nav-active' : 'nav-button'}
              onClick={() => {
                setPage(key);
                setSelected(null);
              }}
            >
              {label}
            </button>
          ))}
        </nav>
        <button
          className="ghost"
          onClick={async () => {
            try {
              await api('/session', { method: 'DELETE' });
              setSession(null);
              setCsrf('');
            } catch (e) {
              setError(errorText(e));
            }
          }}
        >
          <LogOut size={16} />
          退出
        </button>
      </header>
      <div className="page-scroll" role="region" aria-label="页面内容" tabIndex={0}>
        <main className="main">
          {host && host.committedMemoryMiB > host.memoryMiB - host.reservedMemoryMiB && (
            <p role="alert" className="error">
              可分配内存不足，请调整实例配额后再创建或增加资源。
            </p>
          )}
          <div className="page-intro">
            <div>
              <p className="eyebrow">PALWORLD SERVER MANAGEMENT</p>
              <h1>{instance?.name || (page === 'host' ? '主机与安全' : '你的独立世界')}</h1>
              {instance && (
                <p className="muted">
                  世界 {instance.worldGuid || '待生成'} ·{' '}
                  {instance.writable ? '写管理' : '只读登记'}
                </p>
              )}
            </div>
            <div className="actions">
              <button className="ghost" onClick={() => void refresh()}>
                <RefreshCw size={16} />
                刷新
              </button>
              {page === 'instances' && !instance && (
                <button
                  className="primary"
                  onClick={() => {
                    setDraft({ ...draft, name: '' });
                    setCreateOpen(true);
                    setCloneId(null);
                  }}
                >
                  <Plus size={17} />
                  创建新世界
                </button>
              )}
            </div>
          </div>
          {error && (
            <div className="error-box" role="alert">
              {error}
              <button className="ghost" onClick={() => setError('')}>
                收起
              </button>
            </div>
          )}
          {!instance && (
            <div className="metrics">
              <Metric
                label="已登记实例"
                value={instances.length.toString()}
                detail="独立目录与世界"
              />
              <Metric
                label="已分配内存 / 总内存"
                value={
                  host
                    ? `${(host.committedMemoryMiB / 1024).toFixed(1)} / ${(host.memoryMiB / 1024).toFixed(1)} GiB`
                    : '未获取'
                }
                detail={
                  host ? `系统预留 ${(host.reservedMemoryMiB / 1024).toFixed(1)} GiB` : '未获取'
                }
              />
              <Metric
                label="需要处理的任务"
                value={tasks.filter((t) => t.state === 'NeedsAttention').length.toString()}
                detail="处理后再继续变更"
              />
              <Metric
                label="最后成功更新"
                value={lastUpdate ? new Date(lastUpdate).toLocaleTimeString('zh-CN') : '未获取'}
                detail="更新时间"
              />
            </div>
          )}
          {page === 'instances' && !instance && (
            <section aria-label="实例列表">
              <div className="section-head">
                <h2>
                  实例 <span className="muted">{instances.length}</span>
                </h2>
                <button
                  className="ghost"
                  onClick={() =>
                    void api<Candidate[]>('/discovery')
                      .then(setDiscovered)
                      .catch((e) => setError(errorText(e)))
                  }
                >
                  接管既有实例
                </button>
              </div>
              {instances.length === 0 ? (
                <div className="empty-state">
                  <ServerCog size={40} />
                  <h3>还没有登记的实例</h3>
                  <p>创建独立新世界，或先只读发现已有服务器。登记不会停止游戏。</p>
                </div>
              ) : (
                instances.map((item) => (
                  <article className="instance-card" key={item.id}>
                    {initializationTask(item.id) && !item.applied && (
                      <InstanceInitialization task={initializationTask(item.id)!} />
                    )}
                    <div className="instance-heading">
                      <div>
                        <h3>
                          <button
                            className="text-button"
                            onClick={() => {
                              setSelected(item.id);
                              setTab('overview');
                              setObserved(null);
                            }}
                          >
                            {item.name}
                          </button>{' '}
                          <span className="muted">期望：{describe(item.desiredPower)}</span>
                        </h3>
                        <p className="muted">
                          {item.worldGuid || '世界尚未核实'} · {item.writable ? '写管理' : '只读'}
                        </p>
                      </div>
                      <button
                        className="ghost"
                        onClick={() => {
                          setSelected(item.id);
                          setTab('overview');
                          setObserved(null);
                        }}
                      >
                        查看实例
                      </button>
                    </div>
                    <div className="instance-summary">
                      <span>
                        游戏地址{' '}
                        <strong>
                          {item.gameAddress && item.gamePort != null
                            ? `${item.gameAddress.includes(':') ? `[${item.gameAddress}]` : item.gameAddress}:${item.gamePort} / UDP`
                            : item.gamePort == null
                              ? '未知'
                              : `地址未知 · ${item.gamePort} / UDP`}
                        </strong>
                      </span>
                      <span>
                        游戏版本 <strong>{item.gameBuild || '未核实'}</strong>
                      </span>
                      <span>
                        资源{' '}
                        <strong>
                          {item.resourceBudgetKnown
                            ? `${item.desired.cpu} CPU / ${item.desired.memoryMiB / 1024} GiB`
                            : '资源预算尚未核实'}
                        </strong>
                      </span>
                    </div>
                  </article>
                ))
              )}
            </section>
          )}
          {instance && (
            <section>
              <button className="text-button back" onClick={() => setSelected(null)}>
                <ArrowLeft size={16} /> 返回实例列表
              </button>
              <div className="actions instance-controls" aria-label="实例操作">
                {['start', 'stop', 'backup'].map((kind) => (
                  <button
                    key={kind}
                    className="ghost"
                    disabled={busy || instanceTaskLocked || !instance.writable}
                    onClick={() => void requestAction(instance, kind)}
                  >
                    {describe(kind)}
                  </button>
                ))}
                <MoreActions key={instance.id}>
                  {['restart', 'save'].map((kind) => (
                    <button
                      key={kind}
                      className="ghost"
                      disabled={busy || instanceTaskLocked || !instance.writable}
                      onClick={() => void requestAction(instance, kind)}
                    >
                      {describe(kind)}
                    </button>
                  ))}
                  <button
                    className="ghost"
                    disabled={busy || instanceTaskLocked || !instance.writable}
                    onClick={() => void requestAction(instance, 'stop', { force: true })}
                  >
                    强制停止
                  </button>

                  <button
                    className="ghost"
                    disabled={busy || instanceTaskLocked || !instance.writable}
                    onClick={async () => {
                      actionOpener.current =
                        document.activeElement instanceof HTMLElement
                          ? document.activeElement
                          : null;
                      setError('');
                      setApprovedImages([]);
                      setUpgradeOpen(true);
                      setBusy(true);
                      try {
                        const result = await api<{ approvedImages: string[] }>('/capabilities');
                        setApprovedImages(result.approvedImages);
                        setUpgradeImage(instance.image);
                        setExpectedBuild(instance.gameBuild || '');
                        setUpgradeMode('image');
                      } catch (error) {
                        setError(errorText(error));
                      } finally {
                        setBusy(false);
                      }
                    }}
                  >
                    升级
                  </button>
                  {!instance.writable &&
                    instance.configurationKnown &&
                    !instance.quarantinedUtc && (
                      <button
                        className="primary"
                        onClick={() => {
                          setExternalSchedulesConfirmed(false);
                          setAdoptOpen(true);
                        }}
                      >
                        启用受控写管理
                      </button>
                    )}
                  {instance.quarantinedUtc && !instance.purgedUtc && (
                    <>
                      <button
                        className="ghost"
                        onClick={() => void requestAction(instance, 'undo-quarantine')}
                      >
                        撤销隔离清理
                      </button>
                      <button
                        className="ghost"
                        onClick={() => void requestAction(instance, 'finalize-purge')}
                      >
                        保留期后永久清空
                      </button>
                    </>
                  )}
                  <button
                    className="ghost"
                    disabled={!instance.configurationKnown}
                    onClick={() => {
                      actionOpener.current =
                        document.activeElement instanceof HTMLElement
                          ? document.activeElement
                          : null;
                      setCloneConfirmation(instance);
                    }}
                  >
                    仅克隆规则
                  </button>
                  <button
                    className="ghost"
                    disabled={busy || instanceTaskLocked || !instance.writable}
                    onClick={() => void requestAction(instance, 'retain-data')}
                  >
                    移除容器保留数据
                  </button>
                  <button
                    className="ghost"
                    disabled={busy || instanceTaskLocked || !instance.owned || !instance.writable}
                    onClick={() => void requestAction(instance, 'purge')}
                  >
                    隔离清理
                  </button>
                  <button
                    className="ghost"
                    disabled={busy || instanceTaskLocked}
                    onClick={() => void requestAction(instance, 'unmanage')}
                  >
                    取消接管
                  </button>
                </MoreActions>
              </div>
              {initializationTask(instance.id) && !instance.applied && (
                <InstanceInitialization task={initializationTask(instance.id)!} />
              )}
              <div
                className="tabs"
                role="tablist"
                aria-label="实例详情"
                onKeyDown={(event) => {
                  if (!['ArrowRight', 'ArrowLeft', 'Home', 'End'].includes(event.key)) return;
                  const tabs = Array.from(
                    event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="tab"]'),
                  );
                  const index = tabs.findIndex((element) => element === document.activeElement);
                  const next =
                    event.key === 'Home'
                      ? 0
                      : event.key === 'End'
                        ? tabs.length - 1
                        : (index + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) %
                          tabs.length;
                  event.preventDefault();
                  tabs[next]?.click();
                  tabs[next]?.focus();
                }}
              >
                {[
                  ['overview', '概览'],
                  ['settings', '设置'],
                  ['logs', '日志'],
                  ['backups', '备份与恢复'],
                  ['tasks', '任务'],
                ].map(([key, label]) => (
                  <button
                    key={key}
                    role="tab"
                    id={`tab-${key}`}
                    aria-controls={`panel-${key}`}
                    tabIndex={tab === key ? 0 : -1}
                    aria-selected={tab === key}
                    className={tab === key ? 'active' : ''}
                    onClick={() => {
                      setTab(key);
                      if (key === 'settings') setDraft(instance.desired);
                    }}
                  >
                    {label}
                  </button>
                ))}
              </div>
              <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`} tabIndex={0}>
                {tab === 'overview' && (
                  <div className="card">
                    <h2>实例基本信息</h2>
                    <dl className="facts">
                      <Fact label="世界 GUID" value={instance.worldGuid || '未知'} />
                      <Fact label="游戏版本" value={instance.gameBuild || '未知'} />
                      <Fact label="管理模式" value={instance.writable ? '写管理' : '只读'} />
                      <Fact
                        label="运行状态"
                        value={observed ? describe(observed.container) : '未获取'}
                      />
                      <Fact
                        label="接口状态"
                        value={observed ? describe(observed.gameApi) : '未获取'}
                      />
                      <Fact
                        label="玩家 / FPS"
                        value={
                          observed
                            ? `${observed.players ?? '未知'} / ${observed.fps ?? '未知'}`
                            : '未获取'
                        }
                      />
                      <Fact
                        label="天数 / 据点"
                        value={
                          observed
                            ? `${observed.days ?? '未知'} / ${observed.bases ?? '未知'}`
                            : '未获取'
                        }
                      />
                      <Fact
                        label="本地 UDP 端口"
                        value={instance.gamePort == null ? '未知' : String(instance.gamePort)}
                      />
                      <Fact
                        label="CPU / 内存用量"
                        value={
                          observed
                            ? `${observed.cpu ?? '未知'}% / ${observed.memoryBytes === null ? '未知' : (observed.memoryBytes / 1024 / 1024).toFixed(0) + ' MiB'}`
                            : '未获取'
                        }
                      />
                      <Fact
                        label="采样时间"
                        value={observed?.updatedUtc ? date(observed.updatedUtc) : '未获取'}
                      />
                      <Fact
                        label="数据状态"
                        value={observed?.stale ? '已过期' : observed ? '已更新' : '未知'}
                      />
                    </dl>
                  </div>
                )}
                {tab === 'settings' && (
                  <div className="card">
                    <h2>配置草稿</h2>
                    {!instance.configurationKnown && (
                      <p role="status">源配置尚未核实，表单默认值不代表当前游戏配置；保持只读。</p>
                    )}
                    <p className="muted">
                      保存草稿不停止游戏，应用前将显示维护预览。密码仅写入，不回显。
                    </p>
                    <ParameterSettings
                      key={instance.id}
                      value={draft}
                      applied={instance.applied}
                      current={
                        instance.configurationKnown
                          ? {
                              ...instance.applied,
                              ...(settingsObserved?.instanceId === instance.id
                                ? settingsObserved.observed
                                : null),
                              additional: {
                                ...instance.applied?.additional,
                                ...(settingsObserved?.instanceId === instance.id
                                  ? settingsObserved.configured
                                  : null),
                                ...(settingsObserved?.instanceId === instance.id
                                  ? settingsObserved.observed?.additional
                                  : null),
                              },
                              cpu: instance.resourceBudgetKnown ? instance.applied?.cpu : undefined,
                              memoryMiB: instance.resourceBudgetKnown
                                ? instance.applied?.memoryMiB
                                : undefined,
                            }
                          : null
                      }
                      onChange={setDraft}
                      search={settingsSearch}
                      onSearchChange={setSettingsSearch}
                      disabled={
                        busy ||
                        instanceTaskLocked ||
                        !instance.writable ||
                        !instance.configurationKnown
                      }
                    />
                    <h3 className="settings-section-title">访问密码</h3>
                    <PasswordStatusTable
                      states={
                        settingsObserved?.instanceId === instance.id
                          ? settingsObserved.passwordStates
                          : undefined
                      }
                      search={settingsSearch}
                      disabled={busy || instanceTaskLocked || !instance.writable}
                      onEdit={(kind, opener) => {
                        secretOpener.current = opener;
                        setError('');
                        setSecretEditing(kind);
                      }}
                    />
                    <PasswordDialog
                      kind={secretEditing}
                      onClose={() => setSecretEditing(null)}
                      disabled={busy || instanceTaskLocked || !instance.writable}
                      returnFocus={secretOpener}
                      onReauthenticate={async (password) => {
                        await api('/session', {
                          method: 'POST',
                          body: JSON.stringify({ userName: session, password }),
                        });
                        await loadSession();
                      }}
                      onSave={async (change) => {
                        await api(`/instances/${instance.id}/secrets`, {
                          method: 'PATCH',
                          headers: { 'If-Match': String(instance.revision) },
                          body: JSON.stringify(change),
                        });
                        const settings = await api<
                          Omit<NonNullable<typeof settingsObserved>, 'instanceId'>
                        >(`/instances/${instance.id}/settings`);
                        setSettingsObserved({ ...settings, instanceId: instance.id });
                        await refresh();
                      }}
                    />
                    <div className="actions settings-actions">
                      <button
                        className="primary"
                        disabled={busy || instanceTaskLocked || !instance.writable}
                        onClick={async () => {
                          setBusy(true);
                          try {
                            await api(`/instances/${instance.id}/settings`, {
                              method: 'PATCH',
                              headers: { 'If-Match': String(instance.revision) },
                              body: JSON.stringify(draft),
                            });
                            await refresh();
                          } catch (e) {
                            setError(errorText(e));
                          } finally {
                            setBusy(false);
                          }
                        }}
                      >
                        保存草稿
                      </button>
                      <button
                        className="ghost"
                        disabled={
                          busy || instanceTaskLocked || !instance.writable || applicationLocked
                        }
                        onClick={() => void requestAction(instance, 'apply-config')}
                      >
                        {applicationTask?.state === 'NeedsAttention'
                          ? '应用需处理'
                          : applicationLocked
                            ? confirmation?.kind === 'apply-config'
                              ? '等待确认…'
                              : '应用中…'
                            : '应用并重启'}
                      </button>
                    </div>
                    {applicationLocked && !confirmation && (
                      <p role="status" className="muted">
                        {applicationTask?.state === 'NeedsAttention'
                          ? '应用任务需要处理，处理完成前保持锁定。'
                          : `配置正在应用${applicationTask ? `：${describe(applicationTask.phase)}` : '，正在读取任务状态'}。`}
                        <button className="text-button" onClick={() => setTab('tasks')}>
                          查看任务
                        </button>
                      </p>
                    )}
                  </div>
                )}
                {tab === 'logs' && (
                  <div className="card">
                    <h2>脱敏日志</h2>
                    <p className="muted">最新记录在前，最近 200 行，每 5 秒刷新。</p>
                    <pre className="logs">{logs || '尚无可用日志'}</pre>
                  </div>
                )}
                {tab === 'backups' && (
                  <div className="backup-layout">
                    <section className="card" aria-labelledby="scheduled-backup-title">
                      <h2 id="scheduled-backup-title">定时备份</h2>
                      <p className="muted">每日按北京时间执行离线备份。</p>
                      <form
                        key={instance.id}
                        className="backup-policy-form"
                        onSubmit={async (event) => {
                          event.preventDefault();
                          const data = new FormData(event.currentTarget);
                          setBusy(true);
                          try {
                            await api(`/instances/${instance.id}/backup-policy`, {
                              method: 'PATCH',
                              headers: { 'If-Match': String(instance.revision) },
                              body: JSON.stringify({
                                time: data.get('time'),
                                retentionDays: Number(data.get('retention')),
                              }),
                            });
                            await refresh();
                          } catch (error) {
                            setError(errorText(error));
                          } finally {
                            setBusy(false);
                          }
                        }}
                      >
                        <label>
                          北京时间备份时间
                          <input
                            name="time"
                            type="time"
                            disabled={busy || instanceTaskLocked || !instance.writable}
                            defaultValue={instance.backupTime}
                            required
                          />
                        </label>
                        <label>
                          保留天数
                          <input
                            name="retention"
                            disabled={busy || instanceTaskLocked || !instance.writable}
                            type="number"
                            min={1}
                            max={365}
                            defaultValue={instance.retentionDays}
                            required
                          />
                        </label>
                        <button
                          className="primary"
                          disabled={!instance.writable || busy || instanceTaskLocked}
                        >
                          保存备份策略
                        </button>
                      </form>
                    </section>
                    <section className="card" aria-labelledby="manual-recovery-title">
                      <h2 id="manual-recovery-title">手动恢复</h2>
                      <div className="section-head">
                        <h3>
                          <DatabaseBackup size={20} /> 恢复点
                        </h3>
                        <button
                          className="ghost"
                          disabled={busy || instanceTaskLocked || !instance.writable}
                          onClick={() => void requestAction(instance, 'backup')}
                        >
                          立即离线备份
                        </button>
                      </div>
                      <p className="muted">可信快照先保存并短暂停服；原来停止的实例保持停止。</p>
                      <div className="table-scroll">
                        <table>
                          <thead>
                            <tr>
                              <th>建立时间</th>
                              <th>用途</th>
                              <th>大小</th>
                              <th>保护</th>
                              <th>备份内容</th>
                              <th>操作</th>
                            </tr>
                          </thead>
                          <tbody>
                            {backups.map((b) => (
                              <tr key={b.id}>
                                <td>{date(b.createdUtc)}</td>
                                <td>{describe(b.purpose)}</td>
                                <td>{(b.bytes / 1024 / 1024).toFixed(1)} MiB</td>
                                <td>{b.protected ? '受保护' : '按策略保留'}</td>
                                <td>{b.containsInstallation ? '包含安装文件' : '仅存档与配置'}</td>
                                <td>
                                  <div className="backup-row-actions">
                                    <button
                                      className="ghost"
                                      disabled={busy}
                                      onClick={() => {
                                        setExportPassphrase('');
                                        setExportBackup({
                                          instanceId: instance.id,
                                          backupId: b.id,
                                        });
                                      }}
                                    >
                                      加密导出
                                    </button>
                                    <button
                                      className="ghost"
                                      disabled={busy || instanceTaskLocked || !instance.writable}
                                      onClick={() =>
                                        void requestAction(instance, 'restore', { backupId: b.id })
                                      }
                                    >
                                      恢复此点
                                    </button>
                                  </div>
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                      {backups.length === 0 && <p className="empty-state">尚无可信恢复点</p>}
                      <section className="backup-import" aria-labelledby="zip-import-title">
                        <h3 id="zip-import-title">从 ZIP 导入世界</h3>
                        <p className="muted">
                          仅导入世界和玩家数据，保留目标
                          Config、密码、端口与规则；导入前建立独立恢复点。
                        </p>
                        <label>
                          选择 ZIP 文件
                          <input
                            type="file"
                            accept=".zip,application/zip"
                            disabled={busy || instanceTaskLocked || !instance.writable}
                            onChange={async (event) => {
                              const file = event.target.files?.[0];
                              if (!file) return;
                              setBusy(true);
                              try {
                                const preview = await api<{
                                  uploadId: string;
                                  sha256: string;
                                  worlds: string[];
                                  expandedBytes: number;
                                  ignored: string[];
                                }>(`/instances/${instance.id}/uploads`, {
                                  method: 'POST',
                                  headers: { 'Content-Type': 'application/zip' },
                                  body: file,
                                });
                                setZipPreview(preview);
                                setWorldSelection(preview.worlds[0] || '');
                              } catch (error) {
                                setError(errorText(error));
                              } finally {
                                setBusy(false);
                                event.target.value = '';
                              }
                            }}
                          />
                        </label>
                        {zipPreview && (
                          <div className="import-preview">
                            <label>
                              选择世界
                              <select
                                value={worldSelection}
                                onChange={(event) => setWorldSelection(event.target.value)}
                              >
                                {zipPreview.worlds.map((world) => (
                                  <option key={world}>{world}</option>
                                ))}
                              </select>
                            </label>
                            <p>
                              展开 {(zipPreview.expandedBytes / 1024 / 1024).toFixed(1)} MiB；忽略{' '}
                              {zipPreview.ignored.join('、') || '无'}
                            </p>
                            <button
                              className="primary"
                              disabled={!worldSelection || busy}
                              onClick={() =>
                                void requestAction(instance, 'import', {
                                  uploadId: zipPreview.uploadId,
                                  sha256: zipPreview.sha256,
                                  world: worldSelection,
                                })
                              }
                            >
                              预检并导入
                            </button>
                          </div>
                        )}
                      </section>
                    </section>
                  </div>
                )}
                {tab === 'tasks' && (
                  <TaskTable
                    items={tasks.filter((t) => t.instanceId === instance.id)}
                    onRecover={requestRecovery}
                    onCancel={cancelTask}
                  />
                )}
              </div>
            </section>
          )}
          {page === 'host' && (
            <div className="host-sections">
              <section className="card" aria-labelledby="host-monitoring-title">
                <div className="section-heading">
                  <h2 id="host-monitoring-title">监控指标</h2>
                  <span className="muted">
                    采样时间：{host?.systemSampleUtc ? date(host.systemSampleUtc) : '未知'}
                  </span>
                </div>
                <div className="metrics host-metrics">
                  <Metric
                    label="CPU 使用率"
                    value={host?.cpuPercent == null ? '未知' : `${host.cpuPercent.toFixed(1)}%`}
                    detail={
                      host ? `${host.systemCpuCount ?? host.cpuCount} 个逻辑 CPU` : '等待采样'
                    }
                  />
                  <Metric
                    label="已用内存"
                    value={
                      host?.usedMemoryBytes == null
                        ? '未知'
                        : `${(host.usedMemoryBytes / 1024 ** 3).toFixed(1)} GiB`
                    }
                    detail={
                      host
                        ? `总内存 ${((host.systemMemoryBytes ?? host.memoryMiB * 1024 ** 2) / 1024 ** 3).toFixed(1)} GiB`
                        : '等待采样'
                    }
                  />
                  <Metric
                    label="可用内存"
                    value={
                      host?.availableMemoryBytes == null
                        ? '未知'
                        : `${(host.availableMemoryBytes / 1024 ** 3).toFixed(1)} GiB`
                    }
                    detail={
                      host
                        ? `系统预留 ${(host.reservedMemoryMiB / 1024).toFixed(1)} GiB`
                        : '等待采样'
                    }
                  />
                </div>
              </section>
              <section className="card" aria-labelledby="host-backups-title">
                <div className="section-heading">
                  <h2 id="host-backups-title">备份与灾备</h2>
                  <button
                    className="primary"
                    onClick={() => {
                      setExportPassphrase('');
                      setExportBackup({ instanceId: '', backupId: 'disaster' });
                    }}
                  >
                    加密导出面板灾备
                  </button>
                </div>
                <p className="muted">面板灾备包含配置与管理数据；以下恢复点包含游戏存档。</p>
                <div className="table-wrap">
                  <table>
                    <thead>
                      <tr>
                        <th>实例</th>
                        <th>时间</th>
                        <th>类型</th>
                        <th>操作</th>
                      </tr>
                    </thead>
                    <tbody>
                      {allBackups.length === 0 && (
                        <tr>
                          <td colSpan={4} className="muted">
                            暂无恢复点
                          </td>
                        </tr>
                      )}
                      {allBackups.map((backup) => (
                        <tr key={backup.id}>
                          <td>
                            {instances.find((item) => item.id === backup.instanceId)?.name ||
                              backup.instanceId}
                          </td>
                          <td>{date(backup.createdUtc)}</td>
                          <td>
                            {describe(backup.purpose)}
                            {backup.protected ? ' · 保护' : ''}
                          </td>
                          <td>
                            <button
                              className="ghost"
                              onClick={() => {
                                setExportPassphrase('');
                                setExportBackup({
                                  instanceId: backup.instanceId,
                                  backupId: backup.id,
                                });
                              }}
                            >
                              加密导出
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </section>
              <section className="card" aria-labelledby="host-audit-title">
                <div className="section-heading">
                  <h2 id="host-audit-title">操作记录</h2>
                  <span className="muted">最近 200 条</span>
                </div>
                <div className="table-wrap">
                  <table>
                    <thead>
                      <tr>
                        <th>时间</th>
                        <th>用户</th>
                        <th>实例</th>
                        <th>动作</th>
                        <th>结果</th>
                      </tr>
                    </thead>
                    <tbody>
                      {audit.length === 0 && (
                        <tr>
                          <td colSpan={5} className="muted">
                            暂无操作记录
                          </td>
                        </tr>
                      )}
                      {audit.map((entry, index) => (
                        <tr key={index}>
                          <td>{date(entry.utc)}</td>
                          <td>{entry.user}</td>
                          <td>
                            {instances.find((instance) => instance.id === entry.instanceId)?.name ||
                              entry.instanceId ||
                              '面板'}
                          </td>
                          <td>{describe(entry.action)}</td>
                          <td>{describe(entry.result)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </section>
            </div>
          )}
        </main>
        <footer>PalworldPanel · 单服务器管理</footer>
      </div>
      <Modal
        open={discovered !== null}
        onOpenChange={(open) => {
          if (!open) setDiscovered(null);
        }}
        title="只读登记既有实例"
        description="只读登记保留运行状态与文件；未知配置模板保持只读。"
      >
        {discovered?.length === 0 && <p>没有发现挂载游戏目录的容器。</p>}
        {discovered?.map((candidate) => (
          <article key={candidate.containerId} className="card">
            <h3>{candidate.name}</h3>
            <p>{candidate.reason}</p>
            <p className="muted">{candidate.image}</p>
            <button
              className="primary"
              disabled={!candidate.root || candidate.registered || busy}
              onClick={async () => {
                setBusy(true);
                try {
                  await api('/discovery/register', {
                    method: 'POST',
                    body: JSON.stringify({ containerId: candidate.containerId }),
                  });
                  setDiscovered(null);
                  await refresh();
                } catch (error) {
                  setError(errorText(error));
                } finally {
                  setBusy(false);
                }
              }}
            >
              {candidate.registered ? '已登记' : '登记为只读'}
            </button>
          </article>
        ))}
      </Modal>
      <Modal
        open={createOpen}
        onOpenChange={setCreateOpen}
        title="创建独立新世界"
        description="分配独立目录、端口和密码，不复制其他实例存档。"
      >
        <form
          className="creation-form"
          onSubmit={async (e) => {
            e.preventDefault();
            setBusy(true);
            try {
              if (!creationPreview) {
                setCreationPreview(
                  await api('/creation-previews', {
                    method: 'POST',
                    body: JSON.stringify({ rules: draft, cloneId }),
                  }),
                );
                creationKey.current = commandKey();
                return;
              }
              const created = await api<TaskItem>('/instances', {
                method: 'POST',
                headers: { 'Idempotency-Key': creationKey.current },
                body: JSON.stringify({
                  rules: draft,
                  cloneId,
                  plan: creationPreview.plan,
                  confirmation: creationPreview.token,
                  previewHash: creationPreview.hash,
                }),
              });
              setTasks((previous) => [
                created,
                ...previous.filter((task) => task.id !== created.id),
              ]);
              setCreateOpen(false);
              await refresh();
              setSelected(created.instanceId);
              setTab('overview');
            } catch (error) {
              setError(errorText(error));
            } finally {
              setBusy(false);
            }
          }}
        >
          {creationPreview ? (
            <div className="creation-groups">
              <fieldset className="creation-group">
                <legend>基本信息与资源</legend>
                <dl className="facts">
                  <Fact label="实例名称" value={creationPreview.plan.rules.name} />
                  <Fact label="玩家上限" value={String(creationPreview.plan.rules.maxPlayers)} />
                  <Fact label="CPU 配额" value={`${creationPreview.plan.rules.cpu} CPU`} />
                  <Fact label="内存上限" value={`${creationPreview.plan.rules.memoryMiB} MiB`} />
                </dl>
              </fieldset>
              <fieldset className="creation-group">
                <legend>目录与端口</legend>
                <dl className="facts">
                  <Fact label="实例目录" value={creationPreview.plan.root} />
                  <Fact label="备份目录" value={creationPreview.plan.backupRoot} />
                  <Fact label="游戏端口" value={`${creationPreview.plan.gamePort} / UDP`} />
                  <Fact label="查询端口" value={`${creationPreview.plan.queryPort} / UDP`} />
                  <Fact label="管理端口（回环）" value={`${creationPreview.plan.restPort} / TCP`} />
                  <Fact label="定时备份（北京时间）" value={creationPreview.plan.backupTime} />
                </dl>
              </fieldset>
              <p className="muted">确认后创建独立世界，密码自动生成。</p>
              <button type="button" className="ghost" onClick={() => setCreationPreview(null)}>
                返回编辑
              </button>
            </div>
          ) : (
            <RulesForm value={draft} onChange={setDraft} />
          )}
          <div className="actions creation-footer">
            <button className="ghost" type="button" onClick={() => setCreateOpen(false)}>
              取消
            </button>
            <button className="primary" disabled={busy}>
              {busy ? '正在预检或提交…' : creationPreview ? '确认创建' : '预检目录与端口'}
            </button>
          </div>
        </form>
      </Modal>
      <Modal
        open={upgradeOpen}
        onOpenChange={setUpgradeOpen}
        returnFocus={actionOpener}
        title="明确选择升级范围"
        description="保留配套旧安装、旧存档和配置恢复点。游戏更新只在本次显式升级启动，验证后禁用。"
      >
        <form
          onSubmit={async (event) => {
            event.preventDefault();
            if (!instance) return;
            setUpgradeOpen(false);
            await requestAction(instance, 'upgrade', {
              image: upgradeImage,
              expectedGameBuild: expectedBuild,
              mode: upgradeMode,
            });
          }}
        >
          <label>
            升级范围
            <select value={upgradeMode} onChange={(event) => setUpgradeMode(event.target.value)}>
              <option value="image">仅镜像，保留游戏 build</option>
              <option value="game">仅游戏，保留镜像</option>
              <option value="both">镜像和游戏</option>
            </select>
          </label>
          <label>
            批准的镜像 digest
            <select value={upgradeImage} onChange={(event) => setUpgradeImage(event.target.value)}>
              {approvedImages.map((image) => (
                <option key={image}>{image}</option>
              ))}
            </select>
          </label>
          <label>
            预期游戏 build
            <input
              value={expectedBuild}
              onChange={(event) => setExpectedBuild(event.target.value)}
              required
              maxLength={128}
            />
          </label>
          <p className="muted">镜像变更不等于游戏降级；跨版本回退必须使用配套旧安装和旧存档。</p>
          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}
          <div className="actions">
            <button type="button" className="ghost" onClick={() => setUpgradeOpen(false)}>
              取消
            </button>
            <button className="primary" disabled={busy || !approvedImages.length}>
              预检升级
            </button>
          </div>
        </form>
      </Modal>
      <Modal
        open={adoptOpen}
        onOpenChange={setAdoptOpen}
        title="启用受控写管理"
        description="只支持已验证模板；先保存、停止和建立恢复点，再接管备份与更新策略。未知 Compose 保持只读。"
      >
        <p>当前世界：{instance?.worldGuid || '未知'}</p>
        <label>
          <input
            type="checkbox"
            checked={externalSchedulesConfirmed}
            onChange={(event) => setExternalSchedulesConfirmed(event.target.checked)}
          />
          已核实外部备份、更新和重启任务停用，允许面板成为唯一调度所有者
        </label>
        <div className="actions">
          <button className="ghost" onClick={() => setAdoptOpen(false)}>
            取消
          </button>
          <button
            className="primary"
            disabled={!externalSchedulesConfirmed || busy}
            onClick={async () => {
              if (!instance) return;
              setAdoptOpen(false);
              await requestAction(instance, 'adopt', { externalSchedulesDisabled: true });
            }}
          >
            接管预检
          </button>
        </div>
      </Modal>
      {confirmation && (
        <ActionConfirmation
          request={confirmation}
          onClose={() => setConfirmation(null)}
          onComplete={refresh}
          onSubmitted={(task) => {
            if (confirmation.kind === 'apply-config' && task?.id) {
              setPendingApplications((previous) => ({
                ...previous,
                [confirmation.instance.id]: task.id,
              }));
            }
          }}
          returnFocus={actionOpener}
          onReauthenticate={async (password) => {
            await api('/session', {
              method: 'POST',
              body: JSON.stringify({ userName: session, password }),
            });
            await loadSession();
          }}
        />
      )}
      <Modal
        open={cloneConfirmation !== null}
        onOpenChange={(open) => {
          if (!open) setCloneConfirmation(null);
        }}
        title={`确认仅克隆规则 · ${cloneConfirmation?.name || ''}`}
        description="复制此实例的游戏规则，创建独立新世界；不复制存档和密码，不影响原实例。"
        returnFocus={actionOpener}
      >
        <div className="password-footer">
          <button className="ghost" onClick={() => setCloneConfirmation(null)}>
            取消
          </button>
          <button
            className="primary"
            onClick={() => {
              if (!cloneConfirmation) return;
              setCloneId(cloneConfirmation.id);
              setDraft({
                ...cloneConfirmation.desired,
                name: (cloneConfirmation.name + ' 副本').slice(0, 64),
              });
              setCloneConfirmation(null);
              setCreateOpen(true);
            }}
          >
            确认并配置新实例
          </button>
        </div>
      </Modal>
      <Modal
        open={recovery !== null}
        returnFocus={recoveryOpener}
        onOpenChange={(open) => {
          if (!open) setRecovery(null);
        }}
        title="处理待确认任务"
        description="回退会保存并停止本实例，保持恢复锁。玩家验收必须来自真实入服，自动检查不能代替。"
      >
        <dl className="facts modal-facts">
          <Fact label="任务" value={recovery?.task.id || '未知'} />
          <Fact label="阶段" value={recovery ? describe(recovery.task.phase) : '未知'} />
          <Fact label="恢复点" value={recovery?.task.recoveryPoint || '无'} />
          <Fact label="操作" value={recovery?.resolution || '未知'} />
        </dl>
        {recovery?.resolution === 'force-stop' && (
          <p role="alert">强制停止可能丢失未保存进度；仅在正常保存失败且确认风险后使用。</p>
        )}
        {recovery?.resolution === 'confirm-players' && (
          <label>
            <input
              type="checkbox"
              checked={playersVerified}
              onChange={(event) => setPlayersVerified(event.target.checked)}
            />
            已由原玩家实际入服确认角色、等级、背包、公会与基地一致
          </label>
        )}
        <div className="actions">
          <button className="ghost" onClick={() => setRecovery(null)}>
            取消
          </button>
          <button
            className="primary"
            disabled={busy || (recovery?.resolution === 'confirm-players' && !playersVerified)}
            onClick={async () => {
              if (!recovery) return;
              setBusy(true);
              try {
                await api(`/tasks/${recovery.task.id}/recover`, {
                  method: 'POST',
                  body: JSON.stringify({
                    resolution: recovery.resolution,
                    originalPlayersVerified: recovery.resolution === 'confirm-players',
                    confirmation: recovery.token,
                    previewHash: recovery.hash,
                  }),
                });
                setRecovery(null);
                await refresh();
              } catch (error) {
                setError(errorText(error));
              } finally {
                setBusy(false);
              }
            }}
          >
            确认处理
          </button>
        </div>
      </Modal>
      <Modal
        open={exportBackup !== null}
        onOpenChange={(open) => {
          if (!open) {
            setExportPassphrase('');
            setExportBackup(null);
          }
        }}
        title="加密导出恢复点"
        description="恢复点包含配置和密码，导出文件将使用独立口令加密。"
      >
        <form
          onSubmit={async (event) => {
            event.preventDefault();
            if (!exportBackup) return;
            setBusy(true);
            setExportError('');
            try {
              await downloadEncryptedBackup(
                exportBackup.instanceId
                  ? `/instances/${exportBackup.instanceId}/backups/${exportBackup.backupId}/export`
                  : '/panel-backups/export',
                exportPassphrase,
              );
              setExportBackup(null);
            } catch (error) {
              setExportError(errorText(error));
            } finally {
              setExportPassphrase('');
              setBusy(false);
            }
          }}
        >
          <label>
            独立导出口令
            <input
              type="password"
              autoComplete="new-password"
              minLength={16}
              maxLength={128}
              value={exportPassphrase}
              onChange={(event) => setExportPassphrase(event.target.value)}
              required
              aria-describedby="export-password-help"
            />
          </label>
          <p id="export-password-help" className="muted password-help">
            请输入 16–128 个字符。请单独保管此口令，恢复时需要使用。
          </p>
          {exportError && (
            <p role="alert" className="error">
              {exportError}
            </p>
          )}
          <div className="actions">
            <button
              className="ghost"
              type="button"
              onClick={() => {
                setExportPassphrase('');
                setExportBackup(null);
              }}
            >
              取消
            </button>
            <button className="primary" disabled={busy}>
              加密并下载
            </button>
          </div>
        </form>
      </Modal>
    </div>
  );
}

function Metric({ label, value, detail }: { label: string; value: string; detail: string }) {
  return (
    <div className="metric">
      <div className="metric-icon">
        <Activity size={22} aria-hidden="true" />
      </div>
      <div>
        <span>{label}</span>
        <strong>{value}</strong>
        <small>{detail}</small>
      </div>
    </div>
  );
}
function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}
function TaskTable({
  items,
  onRecover,
  onCancel,
}: {
  items: TaskItem[];
  onRecover: (task: TaskItem, resolution: string) => Promise<void>;
  onCancel: (task: TaskItem) => Promise<void>;
}) {
  return (
    <section className="card">
      <h2>任务与进度</h2>
      <div className="table-scroll">
        <table>
          <thead>
            <tr>
              <th>操作</th>
              <th>状态</th>
              <th>阶段</th>
              <th>建立时间</th>
              <th>说明</th>
              <th>处理</th>
            </tr>
          </thead>
          <tbody>
            {items.map((t) => (
              <tr key={t.id}>
                <td>{describe(t.kind)}</td>
                <td>
                  <Badge state={t.state} />
                </td>
                <td>{describe(t.phase)}</td>
                <td>{date(t.createdUtc)}</td>
                <td>{t.message || t.safeCode || '—'}</td>
                <td>
                  <div className="actions">
                    {t.state === 'Queued' && (
                      <button className="ghost" onClick={() => void onCancel(t)}>
                        取消排队
                      </button>
                    )}
                    {t.state === 'NeedsAttention' && (
                      <>
                        {t.safeCode === 'PlayerVerificationPending' && (
                          <>
                            <button
                              className="ghost"
                              onClick={() => void onRecover(t, 'start-verification')}
                            >
                              启动核验
                            </button>
                            <button
                              className="ghost"
                              onClick={() => void onRecover(t, 'confirm-players')}
                            >
                              原玩家已核验
                            </button>
                          </>
                        )}
                        {t.recoveryPoint && (
                          <button className="ghost" onClick={() => void onRecover(t, 'rollback')}>
                            回退恢复点
                          </button>
                        )}
                        <button className="ghost" onClick={() => void onRecover(t, 'force-stop')}>
                          强制停止
                        </button>
                        {t.kind !== 'import' && t.kind !== 'restore' && t.kind !== 'upgrade' && (
                          <button
                            className="ghost"
                            onClick={() => void onRecover(t, 'acknowledge-safe')}
                          >
                            核实停止后解锁
                          </button>
                        )}
                      </>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {items.length === 0 && <p className="empty-state">没有后台任务</p>}
    </section>
  );
}
function RulesForm({ value, onChange }: { value: Rules; onChange: (r: Rules) => void }) {
  const field = (key: keyof Rules, next: string | number | boolean) =>
    onChange({ ...value, [key]: next });
  return (
    <div className="creation-groups">
      <fieldset className="creation-group">
        <legend>基本信息</legend>
        <div className="form-grid creation-fields">
          <label>
            实例名称
            <input
              value={value.name}
              onChange={(e) => field('name', e.target.value)}
              maxLength={64}
              required
            />
          </label>
          <label>
            玩家上限
            <input
              type="number"
              value={value.maxPlayers}
              min={1}
              max={32}
              onChange={(e) => field('maxPlayers', Number(e.target.value))}
              required
            />
          </label>
        </div>
      </fieldset>
      <fieldset className="creation-group">
        <legend>资源配置</legend>
        <div className="form-grid creation-fields">
          <label>
            CPU 配额
            <input
              type="number"
              value={value.cpu}
              min={0.5}
              step={0.5}
              onChange={(e) => field('cpu', Number(e.target.value))}
              required
            />
          </label>
          <label>
            内存上限（MiB）
            <input
              type="number"
              value={value.memoryMiB}
              min={512}
              step={512}
              onChange={(e) => field('memoryMiB', Number(e.target.value))}
              required
            />
          </label>
        </div>
      </fieldset>
      <fieldset className="creation-group">
        <legend>游戏规则</legend>
        <div className="form-grid creation-fields">
          <label>
            死亡掉落
            <select
              value={value.deathPenalty}
              onChange={(e) => field('deathPenalty', e.target.value)}
            >
              <option value="None">不掉落</option>
              <option value="Item">物品</option>
              <option value="ItemAndEquipment">物品与装备</option>
              <option value="All">全部</option>
            </select>
          </label>
          <label className="check">
            <input
              type="checkbox"
              checked={value.offlinePenalty}
              onChange={(e) => field('offlinePenalty', e.target.checked)}
            />
            启用离线惩罚
          </label>
          <label>
            建筑自然衰减倍率
            <input
              type="number"
              value={value.deteriorationRate}
              min={0}
              max={100}
              step={0.1}
              onChange={(e) => field('deteriorationRate', Number(e.target.value))}
            />
          </label>
          <label>
            建筑受攻击伤害倍率
            <input
              type="number"
              value={value.attackDamageRate}
              min={0}
              max={100}
              step={0.1}
              onChange={(e) => field('attackDamageRate', Number(e.target.value))}
            />
          </label>
        </div>
      </fieldset>
    </div>
  );
}
