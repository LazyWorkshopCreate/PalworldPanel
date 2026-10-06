import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Login } from './App';
import { api } from './api';

vi.mock('./api', () => ({ api: vi.fn(), setCsrf: vi.fn(), commandKey: vi.fn() }));
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});
describe('管理员登录', () => {
  it('成功后清除输入密码并通知会话刷新', async () => {
    vi.mocked(api).mockResolvedValue(undefined);
    const onLogin = vi.fn().mockResolvedValue(undefined);
    render(<Login onLogin={onLogin} />);
    await userEvent.type(screen.getByLabelText('管理员密码'), 'synthetic-test-password');
    await userEvent.click(screen.getByRole('button', { name: '登录面板' }));
    await waitFor(() => expect(onLogin).toHaveBeenCalledOnce());
    expect(screen.getByLabelText('管理员密码')).toHaveValue('');
  });
  it('失败保留在登录页并呈现可访问的错误', async () => {
    vi.mocked(api).mockRejectedValue(new Error('登录尝试过多，请稍后重试。'));
    const onLogin = vi.fn();
    render(<Login onLogin={onLogin} />);
    await userEvent.type(screen.getByLabelText('管理员密码'), 'synthetic-test-password');
    await userEvent.click(screen.getByRole('button', { name: '登录面板' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('登录尝试过多');
    expect(onLogin).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: '登录面板' })).toBeEnabled();
  });
});
