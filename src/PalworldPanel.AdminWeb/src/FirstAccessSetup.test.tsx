import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FirstAccessSetup } from './App';
import { api } from './api';
vi.mock('./api', () => ({ api: vi.fn(), setCsrf: vi.fn(), commandKey: vi.fn() }));
afterEach(() => {
  cleanup();
  vi.resetAllMocks();
});
it('密码不一致时不发送初始化请求', async () => {
  const done = vi.fn();
  render(<FirstAccessSetup onComplete={done} />);
  await userEvent.type(screen.getByLabelText('管理员密码'), 'synthetic-password-one');
  await userEvent.type(screen.getByLabelText('确认密码'), 'synthetic-password-two');
  await userEvent.click(screen.getByRole('button', { name: '设置密码' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('不一致');
  expect(api).not.toHaveBeenCalled();
  expect(done).not.toHaveBeenCalled();
});
it('成功后清除密码并退出首次设置', async () => {
  vi.mocked(api)
    .mockResolvedValueOnce({ required: true, token: 'synthetic-token' })
    .mockResolvedValueOnce(undefined);
  const done = vi.fn();
  render(<FirstAccessSetup onComplete={done} />);
  await userEvent.type(screen.getByLabelText('管理员密码'), 'synthetic-password-one');
  await userEvent.type(screen.getByLabelText('确认密码'), 'synthetic-password-one');
  await userEvent.click(screen.getByRole('button', { name: '设置密码' }));
  await waitFor(() => expect(done).toHaveBeenCalledOnce());
  expect(screen.getByLabelText('管理员密码')).toHaveValue('');
  expect(screen.getByLabelText('确认密码')).toHaveValue('');
});
