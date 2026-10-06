import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MoreActions } from './MoreActions';

afterEach(cleanup);
describe('更多操作', () => {
  it('默认折叠，选择动作时关闭且先将焦点返回触发按钮', async () => {
    const handler = vi.fn(() => expect(screen.getByRole('button', { name: '更多' })).toHaveFocus());
    render(
      <MoreActions>
        <button onClick={handler}>升级</button>
      </MoreActions>,
    );
    expect(screen.queryByRole('button', { name: '升级' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: '更多' }));
    expect(screen.getByRole('button', { name: '更多' })).toHaveAttribute('aria-expanded', 'true');
    await userEvent.click(screen.getByRole('button', { name: '升级' }));
    expect(handler).toHaveBeenCalledOnce();
    expect(screen.queryByRole('group')).not.toBeInTheDocument();
  });
  it('Esc 或点击外部可折叠，禁用操作不执行也不关闭', async () => {
    const handler = vi.fn();
    render(
      <>
        <MoreActions>
          <button disabled onClick={handler}>
            清理
          </button>
        </MoreActions>
        <button>外部</button>
      </>,
    );
    await userEvent.click(screen.getByRole('button', { name: '更多' }));
    await userEvent.click(screen.getByRole('button', { name: '清理' }));
    expect(screen.getByRole('group')).toBeInTheDocument();
    expect(handler).not.toHaveBeenCalled();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('group')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '更多' })).toHaveFocus();
    await userEvent.click(screen.getByRole('button', { name: '更多' }));
    await userEvent.click(screen.getByRole('button', { name: '外部' }));
    expect(screen.queryByRole('group')).not.toBeInTheDocument();
  });
});
