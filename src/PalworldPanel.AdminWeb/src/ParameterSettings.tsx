import React, { useRef, useState } from 'react';
import { Modal } from './Modal';
import catalog from '../../PalworldPanel.Contracts/game-settings.json';
import { listValues, validateParameter, type ParameterDefinition } from './parameterValidation';

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
type Field = ParameterDefinition & {
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
    min: 0,
    max: 1e12,
  },
  {
    key: 'memoryMiB',
    ruleKey: 'memoryMiB',
    label: '内存上限',
    type: 'number',
    defaultValue: '16384',
    editable: true,
    unit: 'MiB',
    min: 512,
    max: Number.MAX_SAFE_INTEGER,
    integer: true,
  },
];
function format(field: Field, value: unknown): string {
  if (value === undefined || value === null) return '未知';
  if (field.type === 'boolean') return String(value).toLowerCase() === 'true' ? '开启' : '关闭';
  if (value === '') return '空';
  if (field.choices && field.type !== 'list') return field.choices[String(value)] || String(value);
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
  onChange: (value: Rules) => void | Promise<void>;
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
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const savingRef = useRef(false);
  const validationError = editing ? validateParameter(editing, input) : '';
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
                    disabled={disabled || saving || !field.editable}
                    aria-label={`修改${field.label}`}
                    onClick={(event) => {
                      opener.current = event.currentTarget;
                      setError('');
                      const starting = draft(field) ?? field.defaultValue ?? '';
                      setInput(
                        field.type === 'boolean'
                          ? String(starting).toLowerCase() === 'true'
                            ? 'true'
                            : 'false'
                          : field.type === 'number'
                            ? String(Number(starting))
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
          if (!open && !savingRef.current) setEditing(null);
        }}
        title={`修改${editing?.label || '参数'}`}
        description="保存后草稿会保留，需另行应用才会影响游戏。取消或按 Esc 不保留此次编辑。"
        returnFocus={opener}
      >
        {editing && (
          <form
            noValidate
            onSubmit={async (event) => {
              event.preventDefault();
              if (disabled || savingRef.current) return;
              if (validationError) {
                setError(validationError);
                return;
              }
              savingRef.current = true;
              setSaving(true);
              setError('');
              const next = editing.ruleKey
                ? {
                    ...value,
                    [editing.ruleKey]:
                      editing.type === 'boolean'
                        ? input === 'true'
                        : editing.type === 'number'
                          ? Number(input)
                          : input,
                  }
                : { ...value, additional: { ...value.additional, [editing.key]: input } };
              try {
                await onChange(next);
                setEditing(null);
              } catch (failure) {
                setError(failure instanceof Error ? failure.message : '保存失败，请重试。');
              } finally {
                savingRef.current = false;
                setSaving(false);
              }
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
              {editing.choices ? (
                <select
                  disabled={disabled || saving}
                  multiple={editing.type === 'list'}
                  value={editing.type === 'list' ? listValues(input) : input}
                  onChange={(event) => {
                    setError('');
                    setInput(
                      editing.type === 'list'
                        ? '(' +
                            Array.from(event.target.selectedOptions, (option) => option.value).join(
                              ',',
                            ) +
                            ')'
                        : event.target.value,
                    );
                  }}
                >
                  {editing.type !== 'list' && !Object.hasOwn(editing.choices, input) && (
                    <option value={input} disabled>
                      当前值无效，请重新选择
                    </option>
                  )}
                  {Object.entries(editing.choices).map(([key, label]) => (
                    <option key={key} value={key}>
                      {label}
                    </option>
                  ))}
                </select>
              ) : editing.type === 'boolean' ? (
                <select
                  disabled={disabled || saving}
                  value={input}
                  onChange={(event) => {
                    setInput(event.target.value);
                    setError('');
                  }}
                >
                  <option value="false">关闭</option>
                  <option value="true">开启</option>
                </select>
              ) : (
                <input
                  type={editing.type === 'number' ? 'number' : 'text'}
                  value={input}
                  disabled={disabled || saving}
                  onChange={(event) => {
                    setInput(event.target.value);
                    setError('');
                  }}
                  min={editing.min}
                  max={editing.max}
                  step={editing.integer ? 1 : 'any'}
                  maxLength={editing.maxLength ?? 4096}
                  required={editing.type === 'number' || editing.key === 'ServerName'}
                />
              )}
            </label>
            {editing.type === 'number' && (
              <p className="muted">
                取值范围：{editing.min}–{editing.max}
                {editing.integer ? '，仅限整数' : ''}
              </p>
            )}
            {editing.type === 'list' && editing.choices && (
              <p className="muted">可选择多个平台（按住 Ctrl 或 Command），也可取消全部选择。</p>
            )}
            {(validationError || error) && <p role="alert">{validationError || error}</p>}
            <div className="actions">
              <button
                type="button"
                className="ghost"
                disabled={saving}
                onClick={() => setEditing(null)}
              >
                取消
              </button>
              {editing.key !== 'ServerName' && editing.defaultValue !== null && (
                <button
                  type="button"
                  className="ghost"
                  disabled={disabled || saving}
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
              <button className="primary" disabled={disabled || saving || !!validationError}>
                {saving ? '保存中…' : '保存到草稿'}
              </button>
            </div>
          </form>
        )}
      </Modal>
    </>
  );
}
