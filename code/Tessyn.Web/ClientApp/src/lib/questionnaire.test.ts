import { describe, expect, it } from 'vitest';
import type { AttentionQuestionDto } from '../api/types';
import { canReset, isChoiceRecommended, isRecommended, recommendedValue, recommendedValues, restoreValues, toAnswers, toggleChoice } from './questionnaire';

const yn = (number: number, recommended?: string): AttentionQuestionDto => ({ number, type: 'yesNo', text: `yn${number}`, choices: [], recommended, recommendedChoices: [] });
const single: AttentionQuestionDto = { number: 2, type: 'single', text: 's', choices: ['Alpha', 'Beta', 'Gamma'], recommended: 'beta', recommendedChoices: [] };
const multi: AttentionQuestionDto = { number: 3, type: 'multi', text: 'm', choices: ['One', 'Two', 'Three'], recommendedChoices: ['three', 'One'] };
const text: AttentionQuestionDto = { number: 4, type: 'text', text: 't', choices: [], recommended: 'Keep it', recommendedChoices: [] };
const all = [yn(1, 'no'), single, multi, text, yn(5)];

describe('questionnaire values', () => {
	it('pre-fills from the recommendations using the stored spelling and choice order', () => {
		const v = recommendedValues(all);
		expect(v[1]).toEqual({ value: 'no' });
		expect(v[2]).toEqual({ value: 'Beta' });
		expect(v[3]).toEqual({ choices: ['One', 'Three'] });
		expect(v[4]).toEqual({ value: 'Keep it' });
		expect(v[5]).toEqual({});
	});

	it('marks the recommended choice and detects changed answers', () => {
		expect(isChoiceRecommended(single, 'Beta')).toBe(true);
		expect(isChoiceRecommended(single, 'Alpha')).toBe(false);
		expect(isChoiceRecommended(multi, 'Three')).toBe(true);
		expect(canReset(single, recommendedValue(single))).toBe(false);
		expect(canReset(single, { value: 'Alpha' })).toBe(true);
		expect(canReset(single, {})).toBe(true);
		expect(canReset(multi, { choices: ['Three', 'One'] })).toBe(false);
		expect(canReset(multi, { choices: [] })).toBe(true);
		expect(canReset(text, { value: 'Other' })).toBe(true);
		expect(isRecommended(text, { value: 'Keep it' })).toBe(true);
	});

	it('never offers Reset for a question without a recommendation', () => {
		expect(canReset(yn(5), { value: 'yes' })).toBe(false);
	});

	it('toggles: yes/no and single behave as a radio group, multi independently, single clears on a second click', () => {
		expect(toggleChoice(yn(1), { value: 'no' }, 'yes')).toEqual({ value: 'yes' });
		expect(toggleChoice(yn(1), { value: 'yes' }, 'yes')).toEqual({ value: 'yes' });
		expect(toggleChoice(single, { value: 'Beta' }, 'Alpha')).toEqual({ value: 'Alpha' });
		expect(toggleChoice(single, { value: 'Beta' }, 'Beta')).toEqual({});
		expect(toggleChoice(multi, { choices: ['Three'] }, 'One')).toEqual({ choices: ['One', 'Three'] });
		expect(toggleChoice(multi, { choices: ['One', 'Three'] }, 'Three')).toEqual({ choices: ['One'] });
		expect(toggleChoice(multi, { choices: ['One'] }, 'One')).toEqual({ choices: [] });
	});

	it('builds one answer per question: missing value = no answer, empty choices = none selected', () => {
		const a = toAnswers(all, { 1: { value: 'no' }, 2: {}, 3: { choices: [] }, 4: { value: '   ' } });
		expect(a).toEqual([{ number: 1, value: 'no' }, { number: 2 }, { number: 3, choices: [] }, { number: 4 }, { number: 5 }]);
	});
});

describe('questionnaire drafts', () => {
	it('round-trips a draft', () => {
		const values = { 1: { value: 'yes' }, 2: { value: 'Gamma' }, 3: { choices: ['Two'] }, 4: { value: 'Changed' }, 5: {} };
		expect(restoreValues(all, { questions: toAnswers(all, values) })).toEqual(values);
	});

	it('drops a draft that no longer fits the questionnaire', () => {
		const good = toAnswers(all, recommendedValues(all));
		expect(restoreValues(all, null)).toBeNull();
		expect(restoreValues(all, { questions: good.slice(1) })).toBeNull();
		expect(restoreValues(all, { questions: good.map((a) => (a.number === 2 ? { number: 2, value: 'Zeta' } : a)) })).toBeNull();
		expect(restoreValues(all, { questions: good.map((a) => (a.number === 3 ? { number: 3, choices: ['Nope'] } : a)) })).toBeNull();
		expect(restoreValues(all, { questions: good.map((a) => (a.number === 1 ? { number: 1, value: 'maybe' } : a)) })).toBeNull();
		expect(restoreValues([{ ...single, choices: ['X', 'Y'] }], { questions: [{ number: 2, value: 'Alpha' }] })).toBeNull();
	});
});
