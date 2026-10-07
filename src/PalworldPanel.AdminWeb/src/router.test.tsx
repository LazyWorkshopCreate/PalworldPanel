import { afterEach, expect, it } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {
  instancePath,
  navigate,
  parseRoute,
  RouteLink,
  safeReturnPath,
  usePanelRoute,
} from './router';

afterEach(cleanup);

it('实例页签具有可刷新路径，概览别名及尾部斜杠规范化', () => {
  for (const tab of ['overview', 'settings', 'logs', 'backups', 'tasks'] as const) {
    expect(parseRoute(instancePath('synthetic', tab))).toMatchObject({
      page: 'instances',
      instanceId: 'synthetic',
      tab,
    });
  }
  expect(parseRoute('/instances/synthetic/overview/').path).toBe('/instances/synthetic');
  expect(parseRoute('/host/').path).toBe('/host');
  expect(parseRoute('/index.html')).toMatchObject({ page: 'home', path: '/' });
  expect(parseRoute('/instances/synthetic/bad').page).toBe('not-found');
  expect(parseRoute('/instances/%2Fetc').page).toBe('not-found');
});

it.each([
  'https://example.com',
  '//example.com',
  '/\\example.com',
  '/login',
  '/setup',
  '/missing',
  '/instances/id?password=secret',
])('登录回跳过滤非管理目标和查询信息 %s', (value) => {
  expect(safeReturnPath(value)).toBe(
    value.startsWith('/instances/id?') ? '/instances/id' : '/instances',
  );
});

it('真实链接支持客户端导航和浏览器历史恢复', async () => {
  function Page() {
    const { route } = usePanelRoute();
    return (
      <>
        <p>{route.path}</p>
        <RouteLink href="/host">主机</RouteLink>
      </>
    );
  }
  render(<Page />);
  expect(screen.getByRole('link', { name: '主机' })).toHaveAttribute('href', '/host');
  await userEvent.click(screen.getByRole('link', { name: '主机' }));
  expect(window.location.pathname).toBe('/host');
  expect(screen.getByText('/host')).toBeInTheDocument();
  window.history.back();
  await waitFor(() => expect(screen.getByText('/')).toBeInTheDocument());
  window.history.forward();
  await waitFor(() => expect(screen.getByText('/host')).toBeInTheDocument());
  expect(() => navigate('//example.com')).toThrow();
});
