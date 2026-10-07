import rules from '../../PalworldPanel.Contracts/instance-actions.json';

export type ActionKind = keyof typeof rules;
type InstanceState = {
  writable: boolean;
  owned: boolean;
  configurationKnown: boolean;
  quarantinedUtc: string | null;
  purgedUtc: string | null;
  draftPending?: boolean;
};
type Runtime = { container: string; gameApi: string; stale: boolean; containerPresent?: boolean };

export function actionState(
  instance: InstanceState,
  action: ActionKind,
  runtime: Runtime | null,
  locked: boolean,
  now = Date.now(),
) {
  const rule = rules[action];
  let reason = '';
  if (locked) reason = '实例操作已锁定，请先查看任务或完成当前操作。';
  else if (action === 'unmanage') {
    if (instance.quarantinedUtc && !instance.purgedUtc) reason = '请先撤销隔离或完成永久清空。';
  } else if (instance.purgedUtc) reason = '实例数据已永久清空。';
  else if (rule.owned && !instance.owned) reason = '仅面板创建的实例支持此操作。';
  else if (action === 'undo-quarantine' || action === 'finalize-purge') {
    if (!instance.quarantinedUtc) reason = '实例未处于隔离状态。';
    else if (
      action === 'finalize-purge' &&
      now - Date.parse(instance.quarantinedUtc) < 7 * 86400000
    )
      reason = '隔离数据必须保留至少 7 天。';
  } else if (instance.quarantinedUtc) reason = '实例已隔离，请先撤销隔离清理。';
  else if (action === 'adopt' && instance.writable) reason = '实例已启用写管理。';
  else if (rule.writable && !instance.writable) reason = '实例处于只读管理模式。';
  else if (['clone', 'adopt'].includes(action) && !instance.configurationKnown)
    reason = '实例配置尚未核实。';
  else if (action === 'upgrade' && instance.draftPending) reason = '请先应用参数与密码草稿。';
  else if (rule.states) {
    if (!runtime || runtime.stale || runtime.container === 'unknown')
      reason = '运行状态未核实，请刷新后重试。';
    else if (!(rule.states as string[]).includes(runtime.container))
      reason =
        action === 'start' && runtime.container === 'running'
          ? '实例已经运行。'
          : '当前容器状态不支持此操作。';
    else if (rule.containerRequired && runtime.containerPresent === false)
      reason = '实例容器不存在，无需执行此操作。';
    else if (rule.healthy && runtime.container === 'running' && runtime.gameApi !== 'healthy')
      reason = '游戏接口不可用，无法安全保存；需要停服时可选择强制停止。';
  }
  return { disabled: !!reason, title: reason ? `${rule.description} ${reason}` : rule.description };
}

export { rules as instanceActions };
