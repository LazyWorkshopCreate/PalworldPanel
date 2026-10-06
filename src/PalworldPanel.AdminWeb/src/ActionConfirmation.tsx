import React, { useEffect, useState } from 'react';
import { Modal } from './Modal';
import { api, ApiError, commandKey } from './api';

export type ActionRequest = {
  instance: { id: string; name: string; revision: number; worldGuid: string | null };
  kind: string;
  arguments?: Record<string, unknown>;
};
export type SubmittedAction = {
  id: string;
  instanceId: string;
  kind: string;
  state: string;
  phase: string;
};
const names: Record<string, string> = {
  start: '启动',
  stop: '停止',
  restart: '重启',
  save: '保存',
  backup: '离线备份',
  upgrade: '升级',
  adopt: '启用受控写管理',
  'retain-data': '移除容器保留数据',
  purge: '隔离清理',
  unmanage: '取消接管',
  'undo-quarantine': '撤销隔离清理',
  'finalize-purge': '永久清空',
  'apply-config': '应用配置',
  restore: '恢复备份',
  import: '导入存档',
};
const impacts: Record<string, string> = {
  start: '启动此实例，玩家随后可连接。',
  stop: '保存游戏并停止此实例，当前玩家连接将中断。',
  restart: '保存游戏后重启此实例，当前玩家连接将中断。',
  save: '立即保存此实例的游戏进度，不停止游戏。',
  backup: '保存游戏并停服创建完整备份，期间玩家无法连接。',
  upgrade: '停止此实例并备份，再按所选范围升级，当前玩家连接将中断。',
  adopt: '停服备份后由面板接管此实例的配置、备份与更新管理。',
  'retain-data': '停止并移除此实例容器，保留存档和备份。',
  purge: '停止此实例并将数据隔离，至少保留 7 天后才允许永久清空。',
  unmanage: '取消面板对该实例的管理，容器和数据保留。',
  'undo-quarantine': '撤销隔离并恢复实例数据目录，不自动启动游戏。',
  'finalize-purge': '永久清空已隔离的数据目录，独立最终备份保留。',
  'apply-config': '保存游戏、停服并备份后应用配置，当前玩家连接将中断。',
  restore: '备份当前存档并停服，然后恢复选定恢复点。',
  import: '备份当前存档并停服，然后导入选定世界。',
};

export function ActionConfirmation({
  request,
  onClose,
  onComplete,
  onReauthenticate,
  onSubmitted,
  returnFocus,
}: {
  request: ActionRequest;
  onClose: () => void;
  onComplete: () => Promise<void>;
  onReauthenticate: (password: string) => Promise<void>;
  onSubmitted?: (task: SubmittedAction) => void;
  returnFocus: React.RefObject<HTMLElement | null>;
}) {
  const [preview, setPreview] = useState<{ token: string; hash: string } | null>(null);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const submittingRef = React.useRef(false);
  const submissionKey = React.useRef<string | null>(null);
  const [error, setError] = useState('');
  const [reauthenticate, setReauthenticate] = useState(false);
  const [password, setPassword] = useState('');
  const [typedName, setTypedName] = useState('');
  const [retry, setRetry] = useState(0);
  const force = request.arguments?.force === true;
  const name = force ? '强制停止' : names[request.kind] || request.kind;
  const destructive = ['purge', 'finalize-purge'].includes(request.kind);
  const handleError = (failure: unknown) => {
    setPreview(null);
    setError(failure instanceof Error ? failure.message : '操作失败，请重试。');
    if (failure instanceof ApiError && failure.code === 'ReauthenticationRequired') {
      setReauthenticate(true);
      setPreview(null);
    }
  };
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setPreview(null);
    setError('');
    api<{ token: string; hash: string }>(`/instances/${request.instance.id}/previews`, {
      method: 'POST',
      body: JSON.stringify({ kind: request.kind, arguments: request.arguments }),
      signal: controller.signal,
    })
      .then((result) => {
        if (!controller.signal.aborted) setPreview(result);
      })
      .catch((failure) => {
        if (!controller.signal.aborted) handleError(failure);
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [request, retry]);
  return (
    <Modal
      open
      onOpenChange={(open) => {
        if (!open && !submitting) onClose();
      }}
      title={`确认${name} · ${request.instance.name}`}
      description={
        force
          ? '立即终止容器，跳过保存，可能丢失未保存进度。'
          : impacts[request.kind] || '执行此实例操作前，请确认目标和影响。'
      }
      returnFocus={returnFocus}
    >
      <p>实例：{request.instance.name}</p>
      <p className="muted">世界：{request.instance.worldGuid || '未核实'}</p>
      {request.arguments && (
        <dl className="facts">
          {Object.entries(request.arguments)
            .filter(([key]) => key !== 'force')
            .map(([key, value]) => (
              <div key={key}>
                <dt>
                  {(
                    {
                      image: '目标镜像',
                      expectedGameBuild: '预期游戏版本',
                      mode: '升级范围',
                      backupId: '恢复点',
                      world: '目标世界',
                    } as Record<string, string>
                  )[key] || key}
                </dt>
                <dd>{String(value)}</dd>
              </div>
            ))}
        </dl>
      )}
      {destructive && (
        <label>
          输入完整实例名确认清理
          <input
            value={typedName}
            onChange={(event) => setTypedName(event.target.value)}
            autoComplete="off"
          />
        </label>
      )}
      {loading && <p role="status">正在核验操作条件…</p>}
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      {reauthenticate && (
        <form
          className="password-editor"
          onSubmit={async (event) => {
            event.preventDefault();
            if (!password || loading) return;
            setLoading(true);
            setError('');
            try {
              await onReauthenticate(password);
              setReauthenticate(false);
              setRetry((value) => value + 1);
            } catch (failure) {
              handleError(failure);
            } finally {
              setPassword('');
              setLoading(false);
            }
          }}
        >
          <label>
            面板管理员登录密码
            <input
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </label>
          <button className="primary" disabled={loading || !password}>
            验证身份
          </button>
        </form>
      )}
      {!loading && !preview && !reauthenticate && (
        <button className="ghost" onClick={() => setRetry((value) => value + 1)}>
          重新预检
        </button>
      )}
      <div className="password-footer">
        <button className="ghost" disabled={submitting} onClick={onClose}>
          取消
        </button>
        <button
          className="primary"
          disabled={
            loading ||
            !preview ||
            reauthenticate ||
            (destructive && typedName !== request.instance.name)
          }
          onClick={async () => {
            if (!preview || submittingRef.current) return;
            submittingRef.current = true;
            setLoading(true);
            setSubmitting(true);
            setError('');
            try {
              const task = await api<SubmittedAction>(`/instances/${request.instance.id}/actions`, {
                method: 'POST',
                headers: {
                  'Idempotency-Key': (submissionKey.current ??= commandKey()),
                  'If-Match': String(request.instance.revision),
                },
                body: JSON.stringify({
                  kind: request.kind,
                  confirmation: preview.token,
                  previewHash: preview.hash,
                  arguments: request.arguments,
                  typedName,
                }),
              });
              onSubmitted?.(task);
              onClose();
              await onComplete();
            } catch (failure) {
              handleError(failure);
            } finally {
              setLoading(false);
              setSubmitting(false);
              submittingRef.current = false;
            }
          }}
        >
          确认{name}
        </button>
      </div>
    </Modal>
  );
}
