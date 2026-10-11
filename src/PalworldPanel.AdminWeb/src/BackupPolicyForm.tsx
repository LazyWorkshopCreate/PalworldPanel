import { useState } from 'react';
import { api, ApiError, setCsrf } from './api';

export function BackupPolicyForm({
  instanceId,
  revision,
  backupTime,
  retentionDays,
  disabled,
  userName,
  onSaved,
}: {
  instanceId: string;
  revision: number;
  backupTime: string;
  retentionDays: number;
  disabled: boolean;
  userName: string;
  onSaved: () => Promise<void>;
}) {
  const [error, setError] = useState('');
  const [reauthenticate, setReauthenticate] = useState(false);
  const [panelPassword, setPanelPassword] = useState('');
  const [saving, setSaving] = useState(false);
  const save = async (time: string, retention: number) => {
    if (reauthenticate) {
      await api('/session', {
        method: 'POST',
        body: JSON.stringify({ userName, password: panelPassword }),
      });
      const session = await api<{ csrfToken: string }>('/session');
      setCsrf(session.csrfToken);
      setReauthenticate(false);
    }
    await api(`/instances/${instanceId}/backup-policy`, {
      method: 'PATCH',
      headers: { 'If-Match': String(revision) },
      body: JSON.stringify({ time, retentionDays: retention }),
    });
    await onSaved();
  };
  return (
    <form
      onSubmit={async (event) => {
        event.preventDefault();
        if (disabled || saving || (reauthenticate && !panelPassword)) return;
        const data = new FormData(event.currentTarget);
        const time = String(data.get('time') || '');
        const retention = Number(data.get('retention'));
        setSaving(true);
        setError('');
        try {
          await save(time, retention);
        } catch (failure) {
          if (failure instanceof ApiError && failure.code === 'ReauthenticationRequired') {
            setReauthenticate(true);
            setError('需要验证面板管理员身份后才能保存，已保留所填策略。');
          } else setError(failure instanceof Error ? failure.message : '保存备份策略失败。');
        } finally {
          setPanelPassword('');
          setSaving(false);
        }
      }}
    >
      <div className="backup-policy-form">
        <label>
          北京时间备份时间
          <input
            name="time"
            type="time"
            disabled={disabled || saving}
            defaultValue={backupTime}
            required
          />
        </label>
        <label>
          保留天数
          <input
            name="retention"
            disabled={disabled || saving}
            type="number"
            min={1}
            max={365}
            defaultValue={retentionDays}
            required
          />
        </label>
        <button className="primary" disabled={disabled || saving}>
          保存备份策略
        </button>
      </div>
      {reauthenticate && (
        <label>
          面板管理员密码
          <input
            type="password"
            autoComplete="current-password"
            value={panelPassword}
            onChange={(event) => setPanelPassword(event.target.value)}
            required
          />
        </label>
      )}
      {error && (
        <p className="error-box" role="alert">
          {error}
        </p>
      )}
    </form>
  );
}
