import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PanelNotices } from './PanelNotices';

afterEach(cleanup);

it('操作错误和采样错误同时保留，收起只影响操作错误', async () => {
  const onDismissAction = vi.fn();
  render(
    <PanelNotices
      actionError="恢复未提交。"
      statusError="游戏接口暂不可用。"
      onDismissAction={onDismissAction}
    />,
  );
  expect(screen.getByRole('alert')).toHaveTextContent('恢复未提交。');
  expect(screen.getByRole('status')).toHaveTextContent('采样未更新：游戏接口暂不可用。');
  await userEvent.click(screen.getByRole('button', { name: '收起' }));
  expect(onDismissAction).toHaveBeenCalledOnce();
  expect(screen.getByRole('status')).toBeInTheDocument();
});
