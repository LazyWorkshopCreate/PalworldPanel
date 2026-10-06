import React, { useEffect, useState } from 'react';
import { Modal } from './Modal';
import { ApiError } from './api';

type PasswordChange = { administratorPassword?: string; gamePassword?: string };

function randomPassword() {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_';
  return Array.from(crypto.getRandomValues(new Uint8Array(24)), (byte) => alphabet[byte & 63]).join(
    '',
  );
}

export function PasswordDialog({
  kind,
  onClose,
  onSave,
  onReauthenticate,
  disabled,
  returnFocus,
}: {
  kind: 'administrator' | 'game' | null;
  onClose: () => void;
  onSave: (change: PasswordChange) => Promise<void>;
  onReauthenticate: (password: string) => Promise<void>;
  disabled: boolean;
  returnFocus?: React.RefObject<HTMLElement | null>;
}) {
  const [password, setPassword] = useState('');
  const [visible, setVisible] = useState(false);
  const [clear, setClear] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [generated, setGenerated] = useState(false);
  const [reauthenticate, setReauthenticate] = useState(false);
  const [panelPassword, setPanelPassword] = useState('');
  const loginInput = React.useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (reauthenticate) loginInput.current?.focus();
  }, [reauthenticate]);
  useEffect(() => {
    setPassword('');
    setVisible(false);
    setClear(false);
    setError('');
    setGenerated(false);
    setReauthenticate(false);
    setPanelPassword('');
  }, [kind]);
  const administrator = kind === 'administrator';
  const label = administrator ? '新管理密码' : '新游戏密码';
  const invalid =
    (!password && !clear) ||
    (administrator && password.length < 16) ||
    /[\x00-\x1f\x7f]/.test(password);
  return (
    <Modal
      open={kind !== null}
      onOpenChange={(open) => {
        if (!open && !saving) onClose();
      }}
      title={administrator ? '修改管理密码' : '修改游戏密码'}
      description="保存到草稿后，需应用并重启实例才能生效。"
      returnFocus={returnFocus}
    >
      <form
        className="password-editor"
        onSubmit={async (event) => {
          event.preventDefault();
          if (invalid || disabled || saving || (reauthenticate && !panelPassword)) return;
          setSaving(true);
          setError('');
          try {
            if (reauthenticate) {
              await onReauthenticate(panelPassword);
              setReauthenticate(false);
            }
            await onSave(
              administrator
                ? { administratorPassword: password }
                : { gamePassword: clear ? '' : password },
            );
            onClose();
          } catch (failure) {
            if (failure instanceof ApiError && failure.code === 'ReauthenticationRequired') {
              setReauthenticate(true);
              setError('需要验证面板管理员身份后才能保存，已保留新密码。');
            } else setError(failure instanceof Error ? failure.message : '保存失败，请重试。');
          } finally {
            setPanelPassword('');
            setSaving(false);
          }
        }}
      >
        <div className="password-field">
          <label htmlFor="instance-new-password">{label}</label>
          <input
            id="instance-new-password"
            type={visible ? 'text' : 'password'}
            autoComplete="new-password"
            value={password}
            onChange={(event) => {
              setPassword(event.target.value);
              setGenerated(false);
            }}
            maxLength={128}
            disabled={disabled || saving || clear}
            aria-describedby="password-help"
          />
          <div className="password-tools">
            <button
              type="button"
              className="ghost"
              disabled={disabled || saving}
              onClick={() => {
                setPassword(randomPassword());
                setClear(false);
                setGenerated(true);
                setError('');
              }}
            >
              随机生成
            </button>
            <button
              type="button"
              className="text-button"
              disabled={saving || clear}
              aria-pressed={visible}
              onClick={() => setVisible(!visible)}
            >
              {visible ? '隐藏密码' : '显示密码'}
            </button>
            <span className="muted" role="status">
              {generated ? '已生成 24 位随机密码' : ''}
            </span>
          </div>
          <p id="password-help" className="muted password-help">
            {administrator ? '请输入 16–128 个字符。' : '最多 128 个字符；留空不修改原密码。'}
            已有密码不会显示。
          </p>
        </div>
        {!administrator && (
          <label className="check password-clear">
            <input
              type="checkbox"
              checked={clear}
              disabled={disabled || saving}
              onChange={(event) => {
                setClear(event.target.checked);
                setPassword('');
                setGenerated(false);
              }}
            />
            清除游戏密码（玩家加入时无需密码）
          </label>
        )}
        {reauthenticate && (
          <div className="password-clear password-field">
            <label htmlFor="panel-verification-password">面板管理员登录密码</label>
            <input
              id="panel-verification-password"
              ref={loginInput}
              type="password"
              autoComplete="current-password"
              value={panelPassword}
              onChange={(event) => setPanelPassword(event.target.value)}
              disabled={saving}
            />
            <p className="muted password-help">输入控制台登录密码，用于验证身份。</p>
          </div>
        )}
        {error && (
          <p role="alert" className="error">
            {error}
          </p>
        )}
        <div className="password-footer">
          <button type="button" className="ghost" disabled={saving} onClick={onClose}>
            取消
          </button>
          <button
            type="submit"
            className="primary"
            disabled={disabled || saving || invalid || (reauthenticate && !panelPassword)}
          >
            {saving ? '保存中…' : reauthenticate ? '验证并保存草稿' : '保存到草稿'}
          </button>
        </div>
      </form>
    </Modal>
  );
}
