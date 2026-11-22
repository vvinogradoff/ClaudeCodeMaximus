import type { AttentionQuestionDto } from '../api/types';
import { canReset, choiceLetter, isChoiceRecommended, isChoiceSelected, isRecommended, recommendedValue, toggleChoice } from '../lib/questionnaire';
import type { QuestionValue, QuestionValues } from '../lib/questionnaire';

interface Props {
	itemId: string;
	questions: AttentionQuestionDto[];
	values: QuestionValues;
	onChange: (number: number, value: QuestionValue) => void;
	onSend: () => void;
}

interface ToggleProps { label: string; on: boolean; recommended: boolean; radio: boolean; onClick: () => void }

function Toggle({ label, on, recommended, radio, onClick }: ToggleProps) {
	return (
		<button type="button" className={`qn-toggle${on ? ' on' : ''}`} role={radio ? 'radio' : 'checkbox'} aria-checked={on} onClick={onClick}>
			<span className="qn-label">{label}</span>
			{recommended && <span className="qn-reco">recommended</span>}
		</button>
	);
}

/** The questions of a Focus card (FR.23.35): toggle groups for yes/no, single and multi, a textarea for text. */
export function QuestionnaireView({ itemId, questions, values, onChange, onSend }: Props) {
	return (
		<div className="questionnaire" data-testid="questionnaire">
			{questions.map((q) => {
				const v = values[q.number];
				const choices = q.type === 'yesNo' ? ['yes', 'no'] : q.choices;
				return (
					<div className="qn-q" key={q.number} data-testid={`question-${q.number}`}>
						<div className="qn-head"><span className="qn-num">Q{q.number}</span><span className="qn-text">{q.text}</span></div>
						{q.type === 'text' ? (
							<div className="qn-text-wrap">
								<textarea rows={2} value={v?.value ?? ''} aria-label={`Answer to Q${q.number}`} id={`${itemId}-q${q.number}`}
									onChange={(e) => onChange(q.number, { value: e.target.value })}
									onKeyDown={(e) => { if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); onSend(); } }} />
								{q.recommended !== undefined && isRecommended(q, v) && <span className="qn-reco">recommended</span>}
							</div>
						) : (
							<div className="qn-group" role={q.type === 'multi' ? 'group' : 'radiogroup'} aria-label={`Q${q.number}`}>
								{choices.map((c, i) => (
									<Toggle key={c} radio={q.type !== 'multi'} on={isChoiceSelected(q, v, c)} recommended={isChoiceRecommended(q, c)}
										label={q.type === 'yesNo' ? (c === 'yes' ? 'Yes' : 'No') : `${choiceLetter(i)}  ${c}`}
										onClick={() => onChange(q.number, toggleChoice(q, v, c))} />
								))}
							</div>
						)}
						{canReset(q, v) && <button type="button" className="link-btn qn-reset" onClick={() => onChange(q.number, recommendedValue(q))}>Reset</button>}
					</div>
				);
			})}
		</div>
	);
}
