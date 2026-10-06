import React from 'react';
import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { PasswordDialog } from './PasswordDialog';
import { ApiError } from './api';

afterEach(cleanup);

it('近期认证过期时原位验证，保留新密码并按认证后顺序重试保存', async () => {
  const onSave = vi
    .fn()
    .mockRejectedValueOnce(
      new ApiError(403, 'ReauthenticationRequired', '请重新登录后执行危险操作。'),
    )
    .mockResolvedValueOnce(undefined);
  const onReauthenticate = vi
    .fn()
    .mockRejectedValueOnce(new ApiError(401, 'InvalidCredentials', '登录密码不正确。'))
    .mockResolvedValueOnce(undefined);
  const onClose = vi.fn();
  render(
    <PasswordDialog
      kind="game"
      disabled={false}
      onSave={onSave}
      onClose={onClose}
      onReauthenticate={onReauthenticate}
    />,
  );
  const input = screen.getByLabelText('新游戏密码');
  await userEvent.type(input, 'synthetic-game-new');
  await userEvent.click(screen.getByRole('button', { name: '保存到草稿' }));
  expect(input).toHaveValue('synthetic-game-new');
  const verify = screen.getByLabelText('面板管理员登录密码');
  expect(verify).toHaveFocus();
  await userEvent.type(verify, 'synthetic-wrong');
  await userEvent.click(screen.getByRole('button', { name: '验证并保存草稿' }));
  expect(input).toHaveValue('synthetic-game-new');
  expect(verify).toHaveValue('');
  expect(onSave).toHaveBeenCalledTimes(1);
  expect(onClose).not.toHaveBeenCalled();
  await userEvent.type(verify, 'synthetic-valid');
  await userEvent.click(screen.getByRole('button', { name: '验证并保存草稿' }));
  expect(onReauthenticate).toHaveBeenLastCalledWith('synthetic-valid');
  expect(onSave).toHaveBeenLastCalledWith({ gamePassword: 'synthetic-game-new' });
  expect(onClose).toHaveBeenCalledOnce();
});

it('允许连续输入游戏密码，刷新父组件后保留输入，提交仅对应字段', async () => {
  const onSave = vi.fn().mockResolvedValue(undefined);
  const onClose = vi.fn();
  const props = {
    kind: 'game' as const,
    disabled: false,
    onReauthenticate: vi.fn().mockResolvedValue(undefined),
    onSave,
    onClose,
  };
  const view = render(<PasswordDialog {...props} />);
  const input = screen.getByLabelText('新游戏密码');
  await userEvent.type(input, 'synthetic-game-123');
  view.rerender(<PasswordDialog {...props} onSave={onSave} />);
  expect(input).toHaveValue('synthetic-game-123');
  expect(input).toHaveFocus();
  await userEvent.click(screen.getByRole('button', { name: '保存到草稿' }));
  expect(onSave).toHaveBeenCalledExactlyOnceWith({ gamePassword: 'synthetic-game-123' });
  expect(onClose).toHaveBeenCalledOnce();
});

it('生成管理密码可显示并继续编辑，长度校验限制保存', async () => {
  const onSave = vi.fn().mockResolvedValue(undefined);
  render(
    <PasswordDialog
      kind="administrator"
      disabled={false}
      onReauthenticate={vi.fn().mockResolvedValue(undefined)}
      onSave={onSave}
      onClose={vi.fn()}
    />,
  );
  const input = screen.getByLabelText('新管理密码');
  await userEvent.type(input, 'short');
  expect(screen.getByRole('button', { name: '保存到草稿' })).toBeDisabled();
  await userEvent.click(screen.getByRole('button', { name: '随机生成' }));
  expect((input as HTMLInputElement).value).toMatch(/^[A-Za-z0-9_-]{24}$/);
  await userEvent.click(screen.getByRole('button', { name: '显示密码' }));
  expect(input).toHaveAttribute('type', 'text');
  await userEvent.type(input, 'X');
  await userEvent.click(screen.getByRole('button', { name: '保存到草稿' }));
  expect(onSave.mock.calls[0][0].administratorPassword).toHaveLength(25);
});

it('清除必须显式选择，随机生成恢复可编辑，失败保留输入并显示错误', async () => {
  const onSave = vi.fn().mockRejectedValue(new Error('合成保存错误'));
  render(
    <PasswordDialog
      kind="game"
      disabled={false}
      onReauthenticate={vi.fn().mockResolvedValue(undefined)}
      onSave={onSave}
      onClose={vi.fn()}
    />,
  );
  await userEvent.click(screen.getByRole('checkbox'));
  expect(screen.getByLabelText('新游戏密码')).toBeDisabled();
  await userEvent.click(screen.getByRole('button', { name: '保存到草稿' }));
  expect(onSave).toHaveBeenCalledWith({ gamePassword: '' });
  await userEvent.click(screen.getByRole('button', { name: '随机生成' }));
  const input = screen.getByLabelText('新游戏密码');
  expect(input).toBeEnabled();
  const generated = (input as HTMLInputElement).value;
  await userEvent.click(screen.getByRole('button', { name: '保存到草稿' }));
  expect(input).toHaveValue(generated);
  expect(screen.getByRole('alert')).toHaveTextContent('合成保存错误');
});

it('取消并重开不保留密码或显示状态', async () => {
  const props = {
    disabled: false,
    onReauthenticate: vi.fn().mockResolvedValue(undefined),
    onSave: vi.fn(),
    onClose: vi.fn(),
  };
  const view = render(<PasswordDialog {...props} kind="game" />);
  await userEvent.click(screen.getByRole('button', { name: '随机生成' }));
  await userEvent.click(screen.getByRole('button', { name: '显示密码' }));
  await userEvent.click(screen.getByRole('button', { name: '取消' }));
  expect(props.onClose).toHaveBeenCalledOnce();
  view.rerender(<PasswordDialog {...props} kind={null} />);
  view.rerender(<PasswordDialog {...props} kind="game" />);
  expect(screen.getByLabelText('新游戏密码')).toHaveValue('');
  expect(screen.getByLabelText('新游戏密码')).toHaveAttribute('type', 'password');
});
