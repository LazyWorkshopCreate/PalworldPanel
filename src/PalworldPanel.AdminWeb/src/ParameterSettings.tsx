import React, { useRef, useState } from 'react';
import { Modal } from './Modal';
import catalog from '../../PalworldPanel.Contracts/game-settings.json';

type Rules = {
  name: string;
  description: string;
  maxPlayers: number;
  deathPenalty: string;
  offlinePenalty: boolean;
  deteriorationRate: number;
  attackDamageRate: number;
  cpu: number;
  memoryMiB: number;
  additional?: Record<string, string> | null;
};
type Field = {
  key: string;
  label: string;
  type: string;
  defaultValue: string | null;
  ruleKey?: keyof Omit<Rules, 'additional'> | null;
  editable: boolean;
  secret?: boolean;
  unit?: string;
};
const fields: Field[] = [
  ...(catalog as Field[]).filter((field) => !field.secret),
  {
    key: 'cpu',
    ruleKey: 'cpu',
    label: 'CPU 配额',
    type: 'number',
    defaultValue: '4',
    editable: true,
    unit: 'CPU',
  },
  {
    key: 'memoryMiB',
    ruleKey: 'memoryMiB',
    label: '内存上限',
    type: 'number',
    defaultValue: '16384',
    editable: true,
    unit: 'MiB',
  },
];
const choices: Record<string, Record<string, string>> = {
  DeathPenalty: { None: '不掉落', Item: '物品', ItemAndEquipment: '物品与装备', All: '全部' },
  Difficulty: { None: '未指定', Easy: '简单', Normal: '普通', Hard: '困难' },
  RandomizerType: { None: '关闭', Region: '按区域随机', All: '全部随机' },
  LogFormatType: { Text: '文本', Json: 'JSON' },
};
function format(field: Field, value: unknown): string {
  if (value === undefined || value === null) return '未知';
  if (field.type === 'boolean') return String(value).toLowerCase() === 'true' ? '开启' : '关闭';
  if (value === '') return '空';
  if (choices[field.key]) return choices[field.key][String(value)] || String(value);
  return `${field.type === 'number' ? Number(value) : value}${field.unit ? ` ${field.unit}` : ''}`;
}
export function ParameterSettings({
  value,
  current,
  applied,
  onChange,
  disabled,
  search,
  onSearchChange,
}: {
  value: Rules;
  current: Partial<Rules> | null;
  applied?: Partial<Rules> | null;
  onChange: (value: Rules) => void;
  disabled: boolean;
  search?: string;
  onSearchChange?: (search: string) => void;
}) {
  const [localSearch, setLocalSearch] = useState('');
  const query = (search ?? localSearch).trim().toLowerCase();
  const visible = fields.filter((field) =>
    `${field.label} ${field.key}`.toLowerCase().includes(query),
  );
  const [editing, setEditing] = useState<Field | null>(null);
  const [input, setInput] = useState('');
  const opener = useRef<HTMLElement | null>(null);
  const actual = (field: Field) =>
    field.ruleKey ? current?.[field.ruleKey] : current?.additional?.[field.key];
  const draft = (field: Field) =>
    field.ruleKey ? value[field.ruleKey] : (value.additional?.[field.key] ?? actual(field));
  const pending = (field: Field) => {
    const explicit = field.ruleKey ? value[field.ruleKey] : value.additional?.[field.key];
    const baseline = field.ruleKey
      ? (applied ?? current)?.[field.ruleKey]
      : (applied?.additional?.[field.key] ?? current?.additional?.[field.key]);
    return (
      field.editable &&
      explicit !== undefined &&
      baseline !== undefined &&
      format(field, explicit) !== format(field, baseline)
    );
  };
  return (
    <>
      <label className="parameter-search">
        搜索参数
        <input
          type="search"
          placeholder="搜索中文名或字段，如经验 / ExpRate"
          value={search ?? localSearch}
          onChange={(event) => (onSearchChange ?? setLocalSearch)(event.target.value)}
        />
      </label>
      <p className="muted">{visible.length} 项参数 · 修改后保存草稿，再应用生效。</p>
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
            {visible.map((field) => (
              <tr key={field.key}>
                <th scope="row">
                  {field.label}
                  <code className="parameter-field">{field.key}</code>
                </th>
                <td>{format(field, actual(field))}</td>
                <td>
                  {field.key === 'ServerName' ? '创建时填写' : format(field, field.defaultValue)}
                </td>
                <td className={pending(field) ? 'draft-pending' : undefined}>
                  {format(field, draft(field))}
                  {pending(field) && <small className="draft-indicator">待应用</small>}
                </td>
                <td>
                  <button
                    className="ghost"
                    disabled={disabled || !field.editable}
                    aria-label={`修改${field.label}`}
                    onClick={(event) => {
                      opener.current = event.currentTarget;
                      const starting = draft(field) ?? field.defaultValue ?? '';
                      setInput(
                        field.type === 'boolean'
                          ? String(starting).toLowerCase() === 'true'
                            ? 'true'
                            : 'false'
                          : String(starting),
                      );
                      setEditing(field);
                    }}
                  >
                    {field.editable ? '修改' : '面板管理'}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {visible.length === 0 && <p className="muted">没有匹配的常规参数</p>}
      <Modal
        open={editing !== null}
        onOpenChange={(open) => {
          if (!open) setEditing(null);
        }}
        title={`修改${editing?.label || '参数'}`}
        description="保存到本页草稿；取消或按 Esc 不保留此次编辑。"
        returnFocus={opener}
      >
        {editing && (
          <form
            onSubmit={(event) => {
              event.preventDefault();
              if (editing.ruleKey) {
                const next =
                  editing.type === 'boolean'
                    ? input === 'true'
                    : editing.type === 'number'
                      ? Number(input)
                      : input;
                onChange({ ...value, [editing.ruleKey]: next });
              } else
                onChange({ ...value, additional: { ...value.additional, [editing.key]: input } });
              setEditing(null);
            }}
          >
            <code className="parameter-field">{editing.key}</code>
            <p>当前值：{format(editing, actual(editing))}</p>
            <p>
              默认值：
              {editing.key === 'ServerName' ? '创建时填写' : format(editing, editing.defaultValue)}
            </p>
            <label>
              {editing.label}
              {choices[editing.key] ? (
                <select value={input} onChange={(event) => setInput(event.target.value)}>
                  {Object.entries(choices[editing.key]).map(([key, label]) => (
                    <option key={key} value={key}>
                      {label}
                    </option>
                  ))}
                </select>
              ) : editing.type === 'boolean' ? (
                <select value={input} onChange={(event) => setInput(event.target.value)}>
                  <option value="false">关闭</option>
                  <option value="true">开启</option>
                </select>
              ) : (
                <input
                  type={editing.type === 'number' ? 'number' : 'text'}
                  value={input}
                  onChange={(event) => setInput(event.target.value)}
                  min={
                    editing.key === 'PhysicsActiveDropItemMaxNum'
                      ? -1
                      : editing.key === 'cpu'
                        ? 0.5
                        : editing.key === 'memoryMiB'
                          ? 512
                          : editing.key === 'ServerPlayerMaxNum'
                            ? 1
                            : 0
                  }
                  max={editing.key === 'ServerPlayerMaxNum' ? 32 : undefined}
                  step="any"
                  maxLength={
                    editing.key === 'ServerName'
                      ? 64
                      : editing.key === 'ServerDescription'
                        ? 256
                        : 4096
                  }
                  required={editing.type === 'number' || editing.key === 'ServerName'}
                />
              )}
            </label>
            <div className="actions">
              <button type="button" className="ghost" onClick={() => setEditing(null)}>
                取消
              </button>
              {editing.key !== 'ServerName' && editing.defaultValue !== null && (
                <button
                  type="button"
                  className="ghost"
                  onClick={() =>
                    setInput(
                      editing.type === 'boolean'
                        ? String(editing.defaultValue).toLowerCase()
                        : String(editing.defaultValue),
                    )
                  }
                >
                  使用默认值
                </button>
              )}
              <button className="primary" disabled={disabled}>
                保存到草稿
              </button>
            </div>
          </form>
        )}
      </Modal>
    </>
  );
}
