import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PasswordStatusTable } from './PasswordStatusTable';

afterEach(cleanup);
it('显示三列状态，未应用时仅高亮草稿，应用后恢复且不含密码', () => {
  const props = { search: '', disabled: false, onEdit: vi.fn() };
  const states = {
    administrator: { currentConfigured: true, modified: false },
    game: { currentConfigured: true, modified: true },
  };
  const view = render(<PasswordStatusTable {...props} states={states} />);
  for (const title of ['当前值', '默认值', '草稿值'])
    expect(screen.getByRole('columnheader', { name: title })).toBeInTheDocument();
  const game = screen.getByRole('row', { name: /ServerPassword/ });
  expect(within(game).getByText('已设置')).toBeInTheDocument();
  expect(within(game).getByText('已修改').closest('td')).toHaveClass('draft-pending');
  const admin = screen.getByRole('row', { name: /AdminPassword/ });
  expect(within(admin).getByText('未修改').closest('td')).not.toHaveClass('draft-pending');
  view.rerender(
    <PasswordStatusTable
      {...props}
      states={{ ...states, game: { currentConfigured: false, modified: false } }}
    />,
  );
  expect(within(game).getByText('未设置')).toBeInTheDocument();
  expect(within(game).getByText('未修改').closest('td')).not.toHaveClass('draft-pending');
  expect(screen.queryByText('待应用')).not.toBeInTheDocument();
});
it('未知状态不伪装未修改，并支持字段搜索与修改入口', async () => {
  const onEdit = vi.fn();
  render(<PasswordStatusTable search="serverpassword" disabled={false} onEdit={onEdit} />);
  expect(screen.getAllByText('未知')).toHaveLength(2);
  expect(screen.queryByText('AdminPassword')).not.toBeInTheDocument();
  const button = screen.getByRole('button', { name: '修改游戏密码' });
  await userEvent.click(button);
  expect(onEdit).toHaveBeenCalledWith('game', button);
});
