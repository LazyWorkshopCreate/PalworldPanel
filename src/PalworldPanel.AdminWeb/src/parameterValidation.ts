export type ParameterDefinition = {
  key: string;
  type: string;
  integer?: boolean;
  min?: number;
  max?: number;
  maxLength?: number;
  choices?: Record<string, string>;
};

export function listValues(value: string): string[] {
  const content = value.trim().replace(/^\(/, '').replace(/\)$/, '');
  return content ? content.split(',').map((part) => part.trim().replace(/^"|"$/g, '')) : [];
}

export function validateParameter(field: ParameterDefinition, value: string): string {
  if (/[\x00-\x1f\x7f-\x9f]/.test(value)) return '不能包含换行或控制字符。';
  if (value.length > (field.maxLength ?? 4096)) return `最多 ${field.maxLength ?? 4096} 个字符。`;
  if (field.key === 'ServerName' && !value.trim()) return '实例名称不能为空。';
  if (field.type === 'boolean') return /^(true|false)$/i.test(value) ? '' : '请选择开启或关闭。';
  if (field.type === 'number') {
    if (
      !value.trim() ||
      !/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?$/i.test(value.trim()) ||
      !Number.isFinite(Number(value))
    )
      return '请输入有效数字。';
    const number = Number(value);
    if (field.integer && !Number.isInteger(number)) return '必须是整数。';
    if (field.key === 'cpu' && number <= 0) return 'CPU 配额必须大于 0。';
    if (field.min !== undefined && number < field.min) return `不能小于 ${field.min}。`;
    if (field.max !== undefined && number > field.max) return `不能大于 ${field.max}。`;
  }
  if (field.type === 'enum' && !field.choices?.[value]) return '请选择有效选项。';
  if (field.type === 'list') {
    const values = listValues(value);
    if (values.length > 256 || values.some((item) => !/^[A-Za-z0-9_-]{1,128}$/.test(item)))
      return '列表包含无效项目，请使用逗号分隔有效 ID。';
    if (field.choices && values.some((item) => !Object.hasOwn(field.choices!, item)))
      return '请选择有效的平台。';
  }
  if (field.key === 'BanListURL' && value) {
    try {
      const url = new URL(value);
      if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password)
        return '请输入不含用户名和密码的 HTTP 或 HTTPS 地址。';
    } catch {
      return '请输入有效的 HTTP 或 HTTPS 地址。';
    }
  }
  return '';
}
