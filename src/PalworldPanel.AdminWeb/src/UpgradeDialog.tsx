import React, { useEffect, useState } from 'react';
import { Modal } from './Modal';
import { api } from './api';

type UpdateCheck = {
  status: 'up-to-date' | 'available';
  currentVersion: string;
  targetVersion: string;
  updateToken: string | null;
};

export function UpgradeDialog({
  instanceId,
  onClose,
  onUpgrade,
  returnFocus,
}: {
  instanceId: string;
  onClose: () => void;
  onUpgrade: (arguments_: Record<string, unknown>) => void;
  returnFocus: React.RefObject<HTMLElement | null>;
}) {
  const [result, setResult] = useState<UpdateCheck | null>(null);
  const [error, setError] = useState('');
  const [checking, setChecking] = useState(true);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setChecking(true);
    setResult(null);
    setError('');
    api<UpdateCheck>(`/instances/${instanceId}/update-check`, {
      method: 'POST',
      signal: controller.signal,
    })
      .then((value) => {
        if (!controller.signal.aborted) setResult(value);
      })
      .catch((failure) => {
        if (!controller.signal.aborted)
          setError(failure instanceof Error ? failure.message : '无法检查更新，请重试。');
      })
      .finally(() => {
        if (!controller.signal.aborted) setChecking(false);
      });
    return () => controller.abort();
  }, [instanceId, retry]);
  return (
    <Modal
      open
      onOpenChange={(open) => {
        if (!open) onClose();
      }}
      returnFocus={returnFocus}
      title="检查游戏更新"
      description="检查此实例是否有可用的正式版本。"
    >
      {checking && <p role="status">正在检查更新…</p>}
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      {result?.status === 'up-to-date' && (
        <div>
          <p role="status">当前已是最新版本，不需要更新。</p>
          <p>
            当前版本：<strong>{result.currentVersion}</strong>
          </p>
        </div>
      )}
      {result?.status === 'available' && (
        <div>
          <p role="status">发现可用更新</p>
          <dl className="upgrade-versions">
            <div>
              <dt>当前版本</dt>
              <dd>{result.currentVersion}</dd>
            </div>
            <div>
              <dt>目标版本</dt>
              <dd>{result.targetVersion}</dd>
            </div>
          </dl>
          <p className="muted">
            升级将保存进度、备份并短暂停服。自动检查完成后，请确认玩家可以正常进入游戏。
          </p>
        </div>
      )}
      <div className="actions">
        <button type="button" className="ghost" onClick={onClose}>
          关闭
        </button>
        {!checking && (
          <button type="button" className="ghost" onClick={() => setRetry((value) => value + 1)}>
            重新检查
          </button>
        )}
        {result?.status === 'available' && (
          <button
            type="button"
            className="primary"
            disabled={checking || !result.updateToken}
            onClick={() =>
              onUpgrade({
                updateToken: result.updateToken,
                currentVersion: result.currentVersion,
                targetVersion: result.targetVersion,
              })
            }
          >
            继续升级
          </button>
        )}
      </div>
    </Modal>
  );
}
