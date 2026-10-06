import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { InstanceInitialization } from './InstanceInitialization';
afterEach(cleanup);

describe('实例初始化提示', () => {
  it('等待服务就绪时显示真实阶段并保持未完成进度', () => {
    const { rerender } = render(
      <InstanceInitialization
        task={{ state: 'Running', phase: 'InstallAndStart', message: null }}
      />,
    );
    expect(screen.getByText('正在初始化')).toBeVisible();
    expect(screen.getByText('阶段 4 / 6')).toBeVisible();
    rerender(
      <InstanceInitialization
        task={{ state: 'Running', phase: 'ValidateStartup', message: null }}
      />,
    );
    expect(screen.getByText('阶段 5 / 6')).toBeVisible();
    expect(screen.getByText('等待游戏服务就绪并核实世界、配置与资源限制')).toBeVisible();
    expect(screen.getByRole('progressbar')).toHaveAttribute('value', '4');
    rerender(
      <InstanceInitialization task={{ state: 'Succeeded', phase: 'Complete', message: null }} />,
    );
    expect(screen.getByText('初始化完成')).toBeVisible();
    expect(screen.getByRole('progressbar')).toHaveAttribute('value', '6');
  });
  it('失败时保留阶段和处理说明，不显示就绪', () => {
    render(
      <InstanceInitialization
        task={{
          state: 'NeedsAttention',
          phase: 'ValidateStartup',
          message: '游戏启动超时，请查看任务记录',
        }}
      />,
    );
    expect(screen.getByText('初始化未完成')).toBeVisible();
    expect(screen.getByText('游戏启动超时，请查看任务记录')).toBeVisible();
    expect(screen.queryByText('已就绪')).not.toBeInTheDocument();
  });
});
