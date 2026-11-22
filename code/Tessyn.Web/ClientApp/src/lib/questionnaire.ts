import type { AttentionQuestionAnswer, AttentionQuestionDto } from '../api/types';

/** Current value of one question: `value` for yesNo/single/text, `choices` for multi (FR.23.36 wire shape). */
export type QuestionValue = { value?: string; choices?: string[] };
export type QuestionValues = Record<number, QuestionValue>;

/** The persisted draft of one questionnaire card (FR.23.37): question values only, the note has its own reply draft. */
export interface QuestionnaireDraft { questions: AttentionQuestionAnswer[] }

const norm = (s: string | undefined) => (s ?? '').trim().toLowerCase();

export function choiceLetter(index: number): string {
	return String.fromCharCode(65 + index);
}

/** The recommendation of a question as a value (FR.23.35: every control starts there). */
export function recommendedValue(q: AttentionQuestionDto): QuestionValue {
	if (q.type === 'multi') return { choices: pickStored(q, q.recommendedChoices ?? []) };
	if (q.recommended === undefined || q.recommended === null) return {};
	if (q.type === 'single') return { value: pickStored(q, [q.recommended])[0] };
	if (q.type === 'yesNo') return { value: norm(q.recommended) === 'no' ? 'no' : 'yes' };
	return { value: q.recommended };
}

/** Choice labels from the question's own list, in its order, matched case-insensitively; unknown labels are dropped. */
function pickStored(q: AttentionQuestionDto, wanted: string[]): string[] {
	const set = new Set(wanted.map(norm));
	return q.choices.filter((c) => set.has(norm(c)));
}

export function recommendedValues(questions: AttentionQuestionDto[]): QuestionValues {
	const r: QuestionValues = {};
	for (const q of questions) r[q.number] = recommendedValue(q);
	return r;
}

export function hasRecommendation(q: AttentionQuestionDto): boolean {
	return q.type === 'multi' ? (q.recommendedChoices?.length ?? 0) > 0 || q.recommended !== undefined : q.recommended !== undefined && q.recommended !== null;
}

function sameValue(q: AttentionQuestionDto, a: QuestionValue, b: QuestionValue): boolean {
	if (q.type === 'multi') {
		const x = new Set((a.choices ?? []).map(norm));
		const y = new Set((b.choices ?? []).map(norm));
		return x.size === y.size && [...x].every((c) => y.has(c));
	}
	if (q.type === 'text') return (a.value ?? '').trim() === (b.value ?? '').trim();
	return norm(a.value) === norm(b.value);
}

/** True while the answer equals the recommendation (also when there is none and nothing is answered). */
export function isRecommended(q: AttentionQuestionDto, v: QuestionValue | undefined): boolean {
	return sameValue(q, v ?? {}, recommendedValue(q));
}

/** Reset shows only for a question with a recommendation whose answer differs from it (FR.23.35). */
export function canReset(q: AttentionQuestionDto, v: QuestionValue | undefined): boolean {
	return hasRecommendation(q) && !isRecommended(q, v);
}

export function isChoiceSelected(q: AttentionQuestionDto, v: QuestionValue | undefined, choice: string): boolean {
	if (q.type === 'multi') return (v?.choices ?? []).some((c) => norm(c) === norm(choice));
	return norm(v?.value) === norm(choice);
}

export function isChoiceRecommended(q: AttentionQuestionDto, choice: string): boolean {
	const r = recommendedValue(q);
	return q.type === 'multi' ? (r.choices ?? []).some((c) => norm(c) === norm(choice)) : r.value !== undefined && norm(r.value) === norm(choice);
}

/** Radio / toggle behaviour of FR.23.35. Single clears when the selected choice is clicked again; yes/no stays a radio. */
export function toggleChoice(q: AttentionQuestionDto, v: QuestionValue | undefined, choice: string): QuestionValue {
	if (q.type === 'multi') {
		const on = isChoiceSelected(q, v, choice);
		const next = on ? (v?.choices ?? []).filter((c) => norm(c) !== norm(choice)) : [...(v?.choices ?? []), choice];
		return { choices: q.choices.filter((c) => next.some((n) => norm(n) === norm(c))) };
	}
	if (q.type === 'single' && isChoiceSelected(q, v, choice)) return {};
	return { value: choice };
}

/** Wire answers for every question (FR.23.36): a missing value means no answer, empty choices means none selected. */
export function toAnswers(questions: AttentionQuestionDto[], values: QuestionValues): AttentionQuestionAnswer[] {
	return questions.map((q) => {
		const v = values[q.number] ?? {};
		if (q.type === 'multi') return { number: q.number, choices: [...(v.choices ?? [])] };
		const value = v.value;
		return value === undefined || (q.type === 'text' && value.trim() === '') ? { number: q.number } : { number: q.number, value };
	});
}

/**
 * Restores values from a stored draft, or returns null when the draft no longer fits the questionnaire
 * (item re-posted with other questions, FR.23.37) so the card starts again from the recommendations.
 */
export function restoreValues(questions: AttentionQuestionDto[], draft: QuestionnaireDraft | null | undefined): QuestionValues | null {
	const list = draft?.questions;
	if (!Array.isArray(list) || list.length !== questions.length) return null;
	const values: QuestionValues = {};
	for (const q of questions) {
		const a = list.find((x) => x && x.number === q.number);
		if (!a) return null;
		if (q.type === 'multi') {
			if (a.value !== undefined || !Array.isArray(a.choices)) return null;
			const stored = pickStored(q, a.choices);
			if (stored.length !== a.choices.length) return null;
			values[q.number] = { choices: stored };
		} else if (a.value === undefined) {
			if (a.choices !== undefined) return null;
			values[q.number] = {};
		} else if (typeof a.value !== 'string' || a.choices !== undefined) {
			return null;
		} else if (q.type === 'text') {
			values[q.number] = { value: a.value };
		} else if (q.type === 'yesNo') {
			if (norm(a.value) !== 'yes' && norm(a.value) !== 'no') return null;
			values[q.number] = { value: norm(a.value) };
		} else {
			const stored = pickStored(q, [a.value]);
			if (stored.length !== 1) return null;
			values[q.number] = { value: stored[0] };
		}
	}
	return values;
}
