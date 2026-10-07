import React, { useSyncExternalStore } from 'react';

export const instanceTabs = ['overview', 'settings', 'logs', 'backups', 'tasks'] as const;
export type InstanceTab = (typeof instanceTabs)[number];
export type PanelRoute = {
  page: 'home' | 'dashboard' | 'login' | 'setup' | 'instances' | 'host' | 'not-found';
  instanceId: string | null;
  tab: InstanceTab;
  path: string;
};

export function instancePath(id: string, tab: InstanceTab = 'overview') {
  return `/instances/${encodeURIComponent(id)}${tab === 'overview' ? '' : `/${tab}`}`;
}

export function parseRoute(location: string): PanelRoute {
  const path = location.split(/[?#]/)[0].replace(/\/+$/, '') || '/';
  const base = { instanceId: null, tab: 'overview' as InstanceTab, path };
  if (path === '/' || path === '/index.html') return { ...base, path: '/', page: 'home' };
  for (const page of ['dashboard', 'login', 'setup', 'instances', 'host'] as const) {
    if (path === `/${page}`) return { ...base, page };
  }
  const match =
    /^\/instances\/([A-Za-z0-9_-]{1,128})(?:\/(overview|settings|logs|backups|tasks))?$/.exec(path);
  if (match) {
    const tab = (match[2] || 'overview') as InstanceTab;
    return { page: 'instances', instanceId: match[1], tab, path: instancePath(match[1], tab) };
  }
  return { ...base, page: 'not-found' };
}

export function safeReturnPath(value: string | null): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.includes('\\'))
    return '/instances';
  const route = parseRoute(value);
  return ['instances', 'host', 'dashboard'].includes(route.page) ? route.path : '/instances';
}

export function loginPath(returnTo: string) {
  return `/login?returnTo=${encodeURIComponent(safeReturnPath(returnTo))}`;
}

const currentLocation = () => window.location.pathname + window.location.search;
const eventName = 'panel:navigate';
const subscribe = (listener: () => void) => {
  window.addEventListener('popstate', listener);
  window.addEventListener(eventName, listener);
  return () => {
    window.removeEventListener('popstate', listener);
    window.removeEventListener(eventName, listener);
  };
};

export function navigate(path: string, replace = false) {
  if (!path.startsWith('/') || path.startsWith('//') || path.includes('\\'))
    throw new Error('Invalid local route');
  if (path === currentLocation()) return;
  const scroll = document.querySelector('.page-scroll')?.scrollTop || 0;
  window.history.replaceState({ ...window.history.state, panelScroll: scroll }, '');
  window.history[replace ? 'replaceState' : 'pushState']({ panelScroll: 0 }, '', path);
  window.dispatchEvent(new Event(eventName));
}

export function usePanelRoute() {
  const location = useSyncExternalStore(subscribe, currentLocation);
  return { route: parseRoute(location), location, navigate };
}

export function RouteLink({
  href,
  onClick,
  ...props
}: React.AnchorHTMLAttributes<HTMLAnchorElement> & { href: string }) {
  return (
    <a
      {...props}
      href={href}
      onClick={(event) => {
        onClick?.(event);
        if (
          event.defaultPrevented ||
          event.button !== 0 ||
          event.ctrlKey ||
          event.metaKey ||
          event.shiftKey ||
          event.altKey ||
          props.target ||
          props.download
        )
          return;
        event.preventDefault();
        navigate(href);
      }}
    />
  );
}
