const normalize = value => value.trim().normalize("NFKC").replace(/\s+/g, " ").toLocaleLowerCase("vi");

function shuffle(items, random) {
  const result = [...items];
  for (let i = result.length - 1; i > 0; i -= 1) {
    const j = Math.floor(random() * (i + 1));
    [result[i], result[j]] = [result[j], result[i]];
  }
  return result;
}

function usableWords(words) {
  const ids = new Set();
  return words.filter(item => {
    if (ids.has(item.maTuVung) || !item.word?.trim() || !item.meaning?.trim()) return false;
    ids.add(item.maTuVung);
    return true;
  });
}

function makeQuestion(word, pool, type, random) {
  const promptKey = type === "word-to-meaning" ? "word" : "meaning";
  const answerKey = type === "word-to-meaning" ? "meaning" : "word";
  const prompt = normalize(word[promptKey]);
  const correct = word[answerKey].trim();
  // Synonyms and repeated prompts must not introduce a second valid choice.
  const validAnswers = new Set(pool.filter(item => normalize(item[promptKey]) === prompt)
    .map(item => normalize(item[answerKey])));
  validAnswers.add(normalize(correct));
  const distractors = new Map();
  for (const item of pool) {
    const answer = normalize(item[answerKey]);
    if (item.maTuVung !== word.maTuVung && !validAnswers.has(answer))
      distractors.set(answer, item[answerKey].trim());
  }
  if (!distractors.size) return null;
  const options = shuffle([correct, ...shuffle([...distractors.values()], random).slice(0, 3)], random);
  return { word, type, prompt: word[promptKey].trim(), options, correctIndex: options.indexOf(correct) };
}

export function buildPracticeQuestions(targets, optionPool, random = Math.random) {
  const pool = usableWords(optionPool);
  const poolIds = new Set(pool.map(item => item.maTuVung));
  const words = shuffle(usableWords(targets).filter(item => poolIds.has(item.maTuVung)), random);
  return words.flatMap((word, index) => {
    const types = index % 2 === 0 ? ["word-to-meaning", "meaning-to-word"] : ["meaning-to-word", "word-to-meaning"];
    const question = makeQuestion(word, pool, types[0], random) || makeQuestion(word, pool, types[1], random);
    return question ? [question] : [];
  });
}

export function practiceReducer(session, action) {
  if (action.type === "start")
    return { questions: action.questions, position: 0, answers: [], finished: false };
  if (action.type === "reset") return null;
  if (!session || session.finished) return session;
  const question = session.questions[session.position];
  if (action.type === "answer") {
    if (session.answers.length !== session.position || !Number.isInteger(action.optionIndex) ||
        action.optionIndex < 0 || action.optionIndex >= question.options.length) return session;
    return { ...session, answers: [...session.answers, {
      optionIndex: action.optionIndex, correct: action.optionIndex === question.correctIndex
    }] };
  }
  if (action.type === "next" && session.answers.length > session.position) {
    return session.position + 1 === session.questions.length
      ? { ...session, finished: true } : { ...session, position: session.position + 1 };
  }
  return session;
}

export function practiceResult(session) {
  const correct = session.answers.filter(answer => answer.correct).length;
  const wrongWords = session.questions.filter((_, index) => session.answers[index]?.correct === false)
    .map(question => question.word);
  return { total: session.questions.length, correct, incorrect: wrongWords.length, wrongWords,
    percent: session.questions.length ? Math.round(correct / session.questions.length * 100) : 0 };
}
