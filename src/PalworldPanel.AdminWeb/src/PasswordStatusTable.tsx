import React from 'react';

export type InstancePasswordStates = {
  administrator: { currentConfigured: boolean | null; modified: boolean };
  game: { currentConfigured: boolean | null; modified: boolean };
};

export function PasswordStatusTable({
  states,
  search,
  disabled,
  onEdit,
}: {
  states?: InstancePasswordStates;
  search: string;
  disabled: boolean;
  onEdit: (kind: 'administrator' | 'game', opener: HTMLButtonElement) => void;
}) {
  return (
    <div className="table-wrap parameter-table">
      <table>
        <thead>
          <tr>
            <th>参数 / 字段</th>
            <th>当前值</th>
            <th>默认值</th>
            <th>草稿值</th>
            <th>操作</th>
          </tr>
        </thead>
        <tbody>
          {(['administrator', 'game'] as const)
            .filter((kind) =>
              (kind === 'administrator' ? '管理密码 AdminPassword' : '游戏密码 ServerPassword')
                .toLowerCase()
                .includes(search.trim().toLowerCase()),
            )
            .map((kind) => {
              const state = states?.[kind];
              const label = kind === 'administrator' ? '管理密码' : '游戏密码';
              return (
                <tr key={kind}>
                  <th scope="row">
                    {label}
                    <code className="parameter-field">
                      {kind === 'administrator' ? 'AdminPassword' : 'ServerPassword'}
                    </code>
                  </th>
                  <td>
                    {state?.currentConfigured == null
                      ? '未知'
                      : state.currentConfigured
                        ? '已设置'
                        : '未设置'}
                  </td>
                  <td>新实例随机生成</td>
                  <td className={state?.modified ? 'draft-pending' : undefined}>
                    {!state ? '未知' : state.modified ? '已修改' : '未修改'}
                    {state?.modified && <small className="draft-indicator">待应用</small>}
                  </td>
                  <td>
                    <button
                      className="ghost"
                      disabled={disabled}
                      onClick={(event) => onEdit(kind, event.currentTarget)}
                    >
                      修改{label}
                    </button>
                  </td>
                </tr>
              );
            })}
        </tbody>
      </table>
    </div>
  );
}
