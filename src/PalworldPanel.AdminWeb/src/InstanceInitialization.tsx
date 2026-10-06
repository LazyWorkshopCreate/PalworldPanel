const stages = [
  ['Queued', '等待执行'],
  ['Preflight', '检查资源'],
  ['PrepareConfiguration', '准备配置'],
  ['InstallAndStart', '安装并启动'],
  ['ValidateStartup', '验证就绪'],
  ['FinalizeInitialization', '完成初始化'],
] as const;

export type InitializationTask = { state: string; phase: string; message: string | null };

export function InstanceInitialization({ task }: { task: InitializationTask }) {
  const complete = task.state === 'Succeeded';
  const failed = ['Failed', 'NeedsAttention', 'Cancelled'].includes(task.state);
  const phase = task.phase === 'WaitForInstallSlot' ? 'Preflight' : task.phase;
  const index = complete
    ? stages.length - 1
    : Math.max(
        0,
        stages.findIndex(([key]) => key === phase),
      );
  const description =
    task.phase === 'WaitForInstallSlot'
      ? '等待其他安装或备份释放资源'
      : task.phase === 'InstallAndStart'
        ? '正在安装服务器并首次启动，请稍候'
        : task.phase === 'ValidateStartup'
          ? '等待游戏服务就绪并核实世界、配置与资源限制'
          : stages[index][1];
  return (
    <section
      className={`initialization-status ${failed ? 'initialization-failed' : ''}`}
      aria-label="实例初始化"
    >
      <div className="initialization-heading" role="status" aria-live="polite">
        <strong>{complete ? '初始化完成' : failed ? '初始化未完成' : '正在初始化'}</strong>
        <span>{complete ? '已就绪' : `阶段 ${index + 1} / ${stages.length}`}</span>
      </div>
      <p>{failed ? task.message || '初始化已中止，请查看实例任务记录处理。' : description}</p>
      <progress
        aria-label="初始化阶段进度"
        max={stages.length}
        value={complete ? stages.length : index}
      />
      <ol className="initialization-stages">
        {stages.map(([key, label], step) => (
          <li
            key={key}
            className={step < index || complete ? 'done' : step === index ? 'current' : ''}
            aria-current={step === index && !complete ? 'step' : undefined}
          >
            {label}
          </li>
        ))}
      </ol>
    </section>
  );
}
