import { expect, it } from 'vitest';
import catalog from '../../PalworldPanel.Contracts/game-settings.json';
import { validateParameter, type ParameterDefinition } from './parameterValidation';

const definitions = catalog as ParameterDefinition[];

it.each([
  ['BaseCampMaxNumInGuild', '0'],
  ['BaseCampMaxNumInGuild', '11'],
  ['BaseCampMaxNumInGuild', '1.5'],
  ['BaseCampWorkerMaxNum', '51'],
  ['ServerReplicatePawnCullDistance', '4999'],
  ['FishingDifficultyRate', '1.1'],
  ['ServerPlayerMaxNum', ''],
  ['ServerPlayerMaxNum', '16.5'],
  ['PublicPort', '65536'],
  ['ExpRate', 'Infinity'],
  ['ExpRate', '0x10'],
  ['Difficulty', 'Unknown'],
  ['CrossplayPlatforms', '(Steam,Other)'],
  ['DenyTechnologyList', 'PALBOX,evil=setting'],
  ['BanListURL', 'https://user:pass@example.com'],
  ['ServerName', '   '],
  ['ServerName', 'a'.repeat(65)],
  ['ServerDescription', 'a\nb'],
])('阻止无效参数 %s=%s', (key, input) => {
  const field = definitions.find((item) => item.key === key)!;
  expect(validateParameter(field, input)).not.toBe('');
});
it.each([
  ['BaseCampMaxNumInGuild', '1'],
  ['BaseCampMaxNumInGuild', '10'],
  ['BaseCampWorkerMaxNum', '50'],
  ['ServerReplicatePawnCullDistance', '5000'],
  ['FishingDifficultyRate', '0.1'],
  ['ExpRate', '2.5'],
  ['PhysicsActiveDropItemMaxNum', '-1'],
  ['CrossplayPlatforms', '(Steam,Xbox)'],
  ['CrossplayPlatforms', '()'],
  ['DenyTechnologyList', 'PALBOX,RepairBench'],
  ['BanListURL', ''],
])('允许合法边界和列表 %s=%s', (key, input) => {
  expect(
    validateParameter(
      definitions.find((item) => item.key === key)!,
      input,
    ),
  ).toBe('');
});
