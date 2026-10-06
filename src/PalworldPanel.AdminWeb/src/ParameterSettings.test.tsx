import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ParameterSettings } from './ParameterSettings';

afterEach(cleanup);
const value = {
  name: '测试世界',
  description: '',
  maxPlayers: 8,
  deathPenalty: 'None',
  offlinePenalty: false,
  deteriorationRate: 0,
  attackDamageRate: 1,
  cpu: 4,
  memoryMiB: 16384,
};
describe('参数列表编辑', () => {
  it('只高亮未应用的草稿值，已应用后清除高亮，实际观测差异不误报', () => {
    const draft = { ...value, maxPlayers: 12, additional: { ExpRate: '2.0' } };
    const baseline = { ...value, additional: { ExpRate: '2' } };
    const current = { ...baseline, maxPlayers: 4, cpu: 1 };
    const props = { value: draft, current, applied: baseline, onChange: vi.fn(), disabled: false };
    const view = render(<ParameterSettings {...props} />);
    const players = screen.getByRole('row', { name: /ServerPlayerMaxNum/ });
    expect(within(players).getByText('12').closest('td')).toHaveClass('draft-pending');
    expect(
      within(screen.getByRole('row', { name: /CPU 配额/ })).queryByText('待应用'),
    ).not.toBeInTheDocument();
    expect(
      within(screen.getByRole('row', { name: /ExpRate/ })).queryByText('待应用'),
    ).not.toBeInTheDocument();
    view.rerender(<ParameterSettings {...props} applied={draft} />);
    expect(within(players).getByText('12').closest('td')).not.toHaveClass('draft-pending');
    expect(within(players).queryByText('待应用')).not.toBeInTheDocument();
    view.rerender(<ParameterSettings {...props} applied={null} current={null} />);
    expect(within(players).getByText('12').closest('td')).not.toHaveClass('draft-pending');
  });
  it('按中文名和字段名搜索，保存扩展参数保留已有草稿', async () => {
    const onChange = vi.fn();
    render(
      <ParameterSettings
        value={{ ...value, additional: { PalCaptureRate: '1.5' } }}
        current={{ additional: { ExpRate: '2' } }}
        onChange={onChange}
        disabled={false}
      />,
    );
    await userEvent.type(screen.getByLabelText('搜索参数'), '经验');
    expect(screen.getByText('ExpRate')).toBeInTheDocument();
    expect(screen.queryByText('PalCaptureRate')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: '修改经验获取倍率' }));
    await userEvent.clear(screen.getByLabelText('经验获取倍率'));
    await userEvent.type(screen.getByLabelText('经验获取倍率'), '3');
    await userEvent.click(screen.getByRole('button', { name: '保存到草稿' }));
    expect(onChange).toHaveBeenCalledExactlyOnceWith({
      ...value,
      additional: { PalCaptureRate: '1.5', ExpRate: '3' },
    });
    await userEvent.clear(screen.getByLabelText('搜索参数'));
    await userEvent.type(screen.getByLabelText('搜索参数'), 'palcapturerate');
    expect(screen.getByText('帕鲁捕获倍率')).toBeInTheDocument();
    await userEvent.clear(screen.getByLabelText('搜索参数'));
    await userEvent.type(screen.getByLabelText('搜索参数'), '不存在字段');
    expect(screen.getByText('没有匹配的常规参数')).toBeInTheDocument();
  });
  it('分别显示当前、默认和草稿，弹窗确认仅更新指定草稿字段', async () => {
    const onChange = vi.fn();
    render(
      <ParameterSettings
        value={value}
        current={{ ...value, maxPlayers: 4 }}
        onChange={onChange}
        disabled={false}
      />,
    );
    const row = screen.getByRole('row', { name: /ServerPlayerMaxNum/ });
    expect(within(row).getByText('4')).toBeInTheDocument();
    expect(within(row).getByText('16')).toBeInTheDocument();
    expect(within(row).getByText('8')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: '修改玩家上限' }));
    const dialog = screen.getByRole('dialog');
    await userEvent.clear(within(dialog).getByLabelText('玩家上限'));
    await userEvent.type(within(dialog).getByLabelText('玩家上限'), '12');
    await userEvent.click(within(dialog).getByRole('button', { name: '保存到草稿' }));
    expect(onChange).toHaveBeenCalledExactlyOnceWith({ ...value, maxPlayers: 12 });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: '修改玩家上限' })).toHaveFocus();
  });
  it('默认值只在确认后写入，取消和 Esc 不修改草稿', async () => {
    const onChange = vi.fn();
    render(<ParameterSettings value={value} current={null} onChange={onChange} disabled={false} />);
    await userEvent.click(screen.getByRole('button', { name: '修改玩家上限' }));
    await userEvent.click(screen.getByRole('button', { name: '使用默认值' }));
    expect(screen.getByLabelText('玩家上限')).toHaveValue(16);
    await userEvent.click(screen.getByRole('button', { name: '取消' }));
    expect(onChange).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: '修改离线惩罚' }));
    await userEvent.selectOptions(screen.getByLabelText('离线惩罚'), 'true');
    await userEvent.keyboard('{Escape}');
    expect(onChange).not.toHaveBeenCalled();
  });
  it('只读时禁止编辑，未知当前值不伪装成默认值', () => {
    render(<ParameterSettings value={value} current={null} onChange={vi.fn()} disabled />);
    expect(
      within(screen.getByRole('row', { name: /ServerPlayerMaxNum/ })).getByText('未知'),
    ).toBeInTheDocument();
    for (const button of screen.getAllByRole('button')) expect(button).toBeDisabled();
  });
});
