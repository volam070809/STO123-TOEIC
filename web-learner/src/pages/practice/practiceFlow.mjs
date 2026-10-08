export function restoredFeedback(questions) {
  return Object.fromEntries(questions.filter(q => q.isChecked).map(q =>
    [q.questionOccurrenceId, { checked: true, selectedOption: q.selectedOption,
      isCorrect: q.isCorrect, correctOption: q.correctOption, explanation: q.explanation,
      reveal: q.reveal }]));
}

export function checked(question, feedback) {
  return feedback[question.questionOccurrenceId]?.checked ?? !!question.isChecked;
}

export function canNavigateTo(questions, feedback, index) {
  return index >= 0 && index < questions.length &&
    questions.slice(0, index).every(q => checked(q, feedback) || feedback[q.questionOccurrenceId]?.viewed);
}

export function answerViewed(previous, response) {
  return { ...previous, correctOption: response.correctOption,
    explanation: response.explanation, reveal: response.reveal, viewed: true };
}
