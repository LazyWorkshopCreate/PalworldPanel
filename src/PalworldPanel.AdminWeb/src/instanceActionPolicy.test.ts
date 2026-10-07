import { expect, it } from 'vitest';
import { actionState, instanceActions, type ActionKind } from './instanceActionPolicy';

const instance = {
  writable: true,
  owned: true,
  configurationKnown: true,
  quarantinedUtc: null,
  purgedUtc: null,
};
const running = { container: 'running', gameApi: 'healthy', stale: false };
const stopped = { container: 'exited', gameApi: 'unreachable', stale: false };

it.each(Object.keys(instanceActions) as ActionKind[])(
  '%s has a purpose and respects the operation lock',
  (kind) => {
    expect(instanceActions[kind].description.length).toBeGreaterThan(10);
    expect(actionState(instance, kind, running, true).disabled).toBe(true);
  },
);

it('uses observed state rather than desired power and preserves emergency stop', () => {
  expect(actionState(instance, 'start', running, false).disabled).toBe(true);
  expect(actionState(instance, 'start', stopped, false).disabled).toBe(false);
  for (const kind of ['stop', 'restart', 'save', 'force-stop'] as const)
    expect(actionState(instance, kind, stopped, false).disabled).toBe(true);
  for (const kind of ['stop', 'restart', 'save', 'backup'] as const)
    expect(
      actionState(instance, kind, { ...running, gameApi: 'unreachable' }, false).disabled,
    ).toBe(true);
  expect(
    actionState(instance, 'force-stop', { ...running, gameApi: 'unreachable' }, false).disabled,
  ).toBe(false);
  expect(actionState(instance, 'backup', stopped, false).disabled).toBe(false);
  expect(
    actionState(instance, 'retain-data', { ...stopped, containerPresent: false }, false).disabled,
  ).toBe(true);
  expect(
    actionState(instance, 'start', { ...stopped, containerPresent: false }, false).disabled,
  ).toBe(false);
});

it('disables unknown/stale, pending draft, read-only and quarantined operations', () => {
  expect(actionState(instance, 'start', null, false).disabled).toBe(true);
  expect(actionState(instance, 'stop', { ...running, stale: true }, false).disabled).toBe(true);
  expect(actionState({ ...instance, draftPending: true }, 'upgrade', stopped, false).disabled).toBe(
    true,
  );
  const readOnly = { ...instance, writable: false, owned: false };
  expect(actionState(readOnly, 'start', stopped, false).disabled).toBe(true);
  expect(actionState(readOnly, 'clone', null, false).disabled).toBe(false);
  expect(actionState({ ...readOnly, writable: true }, 'purge', stopped, false).disabled).toBe(true);
  const now = Date.parse('2026-10-07T12:00:00+08:00');
  const isolated = {
    ...instance,
    writable: false,
    quarantinedUtc: new Date(now - 6 * 86400000).toISOString(),
  };
  expect(actionState(isolated, 'clone', null, false).disabled).toBe(true);
  expect(actionState(isolated, 'unmanage', null, false).disabled).toBe(true);
  expect(actionState(isolated, 'undo-quarantine', null, false).disabled).toBe(false);
  expect(actionState(isolated, 'finalize-purge', null, false, now).disabled).toBe(true);
  expect(
    actionState(
      { ...isolated, quarantinedUtc: new Date(now - 7 * 86400000).toISOString() },
      'finalize-purge',
      null,
      false,
      now,
    ).disabled,
  ).toBe(false);
});
