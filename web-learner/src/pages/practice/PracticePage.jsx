import {
    useLocation,
    useNavigate,
    useParams,
} from "react-router-dom";

import { useContext, useMemo, useState } from "react";

import { practiceApi } from "../../services/practiceApi";
import { AuthContext } from "../../contexts/AuthState";

import SiteLayout from "../../layouts/SiteLayout";

import {
    PrivateAudio,
    PrivateImage,
} from "../exam/ExamMedia";

import "../../pages/practice/practice-page.css";


export default function PracticePage() {
    const { maKetQua } = useParams();
    const location = useLocation();
    const navigate = useNavigate();

    const { token } = useContext(AuthContext);

    const data = location.state;

    const [bookmarks, setBookmarks] = useState({});
    const [bookmarkLoading, setBookmarkLoading] = useState(null);

    const [answers, setAnswers] = useState({});
    const [loadingQuestion, setLoadingQuestion] = useState(null);

    const [results, setResults] = useState({});

    const [sourceOpen, setSourceOpen] = useState({});

    const [redoLoading, setRedoLoading] = useState(null);

    const [error, setError] = useState("");


    /*
     * Không có dữ liệu bài luyện
     */
    if (!data) {
        return (
            <SiteLayout>
                <div className="practice-page">
                    <div className="practice-empty">
                        <div className="empty-icon">📚</div>

                        <h1>Không tìm thấy bài luyện</h1>

                        <p>
                            Mã kết quả: {maKetQua}
                        </p>

                        <button
                            type="button"
                            onClick={() =>
                                navigate("/practice/toeic")
                            }
                        >
                            ← Quay lại chọn bài
                        </button>
                    </div>
                </div>
            </SiteLayout>
        );
    }


    const questions = data.questions || [];


    /*
     * Gom câu hỏi theo NhomLuotLam
     *
     * Part 3:
     *
     * Group 1
     *   ├── Q1
     *   ├── Q2
     *   └── Q3
     *
     * Group 2
     *   ├── Q4
     *   ├── Q5
     *   └── Q6
     *
     * Câu độc lập sẽ nằm riêng.
     */
    const questionGroups = useMemo(() => {
        const groups = [];
        const groupedMap = new Map();

        questions.forEach((question) => {
            const groupId = question.maNhomLuotLam;

            if (!groupId) {
                groups.push({
                    type: "single",
                    id: `single-${question.maCauHoiLuotLam}`,
                    questions: [question],
                });

                return;
            }

            if (!groupedMap.has(groupId)) {
                const group = {
                    type: "group",
                    id: `group-${groupId}`,
                    groupId,
                    questions: [],
                };

                groupedMap.set(groupId, group);
                groups.push(group);
            }

            groupedMap
                .get(groupId)
                .questions
                .push(question);
        });

        return groups;
    }, [questions]);


    const answeredCount =
        Object.keys(answers).length;

    const progress =
        questions.length > 0
            ? Math.round(
                (answeredCount / questions.length) * 100
            )
            : 0;


    /*
     * Trả lời câu hỏi
     */
    const handleAnswer = async (
        question,
        answer
    ) => {
        const questionId =
            question.maCauHoiLuotLam;

        try {
            setError("");
            setLoadingQuestion(questionId);

            const result =
                await practiceApi.answer(
                    maKetQua,
                    {
                        maCauHoiLuotLam:
                            questionId,

                        dapAnChon:
                            answer,
                    },
                    token
                );

            setAnswers((prev) => ({
                ...prev,
                [questionId]: answer,
            }));

            setResults((prev) => ({
                ...prev,
                [questionId]: result,
            }));
        }
        catch (err) {
            setError(
                err.message ||
                "Không thể gửi đáp án."
            );
        }
        finally {
            setLoadingQuestion(null);
        }
    };


    /*
     * Bookmark
     */
    const handleBookmark = async (
        question
    ) => {
        const questionId =
            question.maCauHoiLuotLam;

        const danhDau =
            !bookmarks[questionId];

        try {
            setError("");

            setBookmarkLoading(
                questionId
            );

            await practiceApi.bookmark(
                maKetQua,
                questionId,
                danhDau,
                token
            );

            setBookmarks((prev) => ({
                ...prev,
                [questionId]:
                    danhDau,
            }));
        }
        catch (err) {
            setError(
                err.message ||
                "Không thể đánh dấu câu hỏi."
            );
        }
        finally {
            setBookmarkLoading(null);
        }
    };


    /*
     * Làm lại câu hỏi
     *
     * Bản chất:
     * - bỏ feedback hiện tại trên UI
     * - cho phép chọn lại đáp án
     *
     * Sau khi chọn đáp án mới,
     * Answer API sẽ cập nhật lại DapAnChon
     * trong DB.
     */
    const handleRedo = (question) => {
        const questionId =
            question.maCauHoiLuotLam;

        setRedoLoading(questionId);

        setAnswers((prev) => {
            const next = { ...prev };
            delete next[questionId];
            return next;
        });

        setResults((prev) => {
            const next = { ...prev };
            delete next[questionId];
            return next;
        });

        setRedoLoading(null);
    };


    /*
     * Submit
     */
    const handleSubmit = async () => {
        try {
            setError("");

            const result =
                await practiceApi.submit(
                    maKetQua,
                    token
                );

            navigate(
                `/practice/toeic/${maKetQua}/result`,
                {
                    state: result,
                }
            );
        }
        catch (err) {
            setError(
                err.message ||
                "Không thể nộp bài."
            );
        }
    };


    /*
     * Hiển thị resource chung
     */
    const renderSource = (
        question,
        groupId
    ) => {
        const hasSource =
            question.noiDungNguLieu ||
            question.noiDungDich ||
            question.duongDanAudio ||
            question.duongDanAnh;

        if (!hasSource) {
            return null;
        }

        const open =
            sourceOpen[groupId];

        return (
            <div className="practice-source">

                <div className="practice-source-header">

                    <div>
                        <span className="practice-source-label">
                            NGỮ LIỆU
                        </span>

                        <h3>
                            Nội dung câu hỏi
                        </h3>
                    </div>

                    <button
                        type="button"
                        className="outline-button"
                        onClick={() =>
                            setSourceOpen(
                                (prev) => ({
                                    ...prev,
                                    [groupId]:
                                        !prev[groupId],
                                })
                            )
                        }
                    >
                        {open
                            ? "Thu gọn đề"
                            : "Xem đề"}
                    </button>

                </div>


                {open && (
                    <div className="practice-source-content">

                        {question.noiDungNguLieu && (
                            <p className="practice-source-text">
                                {question.noiDungNguLieu}
                            </p>
                        )}


                        {question.noiDungDich && (
                            <p className="practice-source-translation">
                                {question.noiDungDich}
                            </p>
                        )}


                        {question.duongDanAnh && (
                            <div className="practice-source-image">
                                <PrivateImage
                                    endpoint={
                                        question.duongDanAnh
                                    }
                                    token={token}
                                    alt="Hình ảnh câu hỏi"
                                />
                            </div>
                        )}


                        {question.duongDanAudio && (
                            <div className="practice-source-audio">

                                <PrivateAudio
                                    endpoint={
                                        question.duongDanAudio
                                    }
                                    token={token}
                                    mode="PRACTICE"
                                />

                            </div>
                        )}

                    </div>
                )}

            </div>
        );
    };


    /*
     * Render một câu hỏi
     */
    const renderQuestion = (
        question,
        index
    ) => {
        const questionId =
            question.maCauHoiLuotLam;

        const selectedAnswer =
            answers[questionId];

        const result =
            results[questionId];

        const isLoading =
            loadingQuestion === questionId;

        const isRedoLoading =
            redoLoading === questionId;


        return (
            <section
                className="practice-question-card"
                key={questionId}
            >

                {/* HEADER */}
                <div className="question-header">

                    <div className="question-number">
                        Câu {index + 1}
                    </div>


                    <div className="question-actions">

                        <button
                            type="button"
                            className={`bookmark-button ${
                                bookmarks[questionId]
                                    ? "bookmarked"
                                    : ""
                            }`}
                            disabled={
                                bookmarkLoading ===
                                questionId
                            }
                            onClick={() =>
                                handleBookmark(
                                    question
                                )
                            }
                        >
                            {bookmarks[questionId]
                                ? "★ Đã đánh dấu"
                                : "☆ Đánh dấu"}
                        </button>


                        {result && (
                            <button
                                type="button"
                                className="redo-question-button"
                                disabled={
                                    isRedoLoading
                                }
                                onClick={() =>
                                    handleRedo(
                                        question
                                    )
                                }
                            >
                                🔄 Làm lại câu này
                            </button>
                        )}

                    </div>

                </div>


                {/* QUESTION */}
                <div className="question-content">

                    <h2>
                        {question.noiDung}
                    </h2>

                </div>


                {/* OPTIONS */}
                <div className="answer-options">

                    {[
                        ["A", question.phuongAnA],
                        ["B", question.phuongAnB],
                        ["C", question.phuongAnC],
                        ["D", question.phuongAnD],
                    ]
                        .filter(
                            ([, text]) =>
                                text
                        )
                        .map(
                            ([letter, text]) => {

                                let className =
                                    "answer-option";


                                if (
                                    selectedAnswer ===
                                    letter
                                ) {
                                    className +=
                                        " selected";
                                }


                                if (
                                    result &&
                                    result.dapAnDung ===
                                    letter
                                ) {
                                    className +=
                                        " correct";
                                }


                                if (
                                    result &&
                                    selectedAnswer ===
                                    letter &&
                                    !result.dung
                                ) {
                                    className +=
                                        " wrong";
                                }


                                return (
                                    <button
                                        key={letter}
                                        type="button"
                                        className={
                                            className
                                        }
                                        disabled={
                                            isLoading
                                        }
                                        onClick={() =>
                                            handleAnswer(
                                                question,
                                                letter
                                            )
                                        }
                                    >

                                        <span className="answer-letter">
                                            {letter}
                                        </span>

                                        <span className="answer-text">
                                            {text}
                                        </span>


                                        {result &&
                                            result.dapAnDung ===
                                            letter && (
                                                <span className="answer-icon">
                                                    ✓
                                                </span>
                                            )}


                                        {result &&
                                            selectedAnswer ===
                                            letter &&
                                            !result.dung && (
                                                <span className="answer-icon">
                                                    ✕
                                                </span>
                                            )}

                                    </button>
                                );
                            }
                        )}

                </div>


                {/* LOADING */}
                {isLoading && (
                    <div className="answer-loading">
                        <span className="small-spinner" />
                        Đang kiểm tra đáp án...
                    </div>
                )}


                {/* RESULT */}
                {result && (
                    <div
                        className={`answer-result ${
                            result.dung
                                ? "result-correct"
                                : "result-wrong"
                        }`}
                    >

                        <div className="result-title">
                            <span>
                                {result.dung
                                    ? "✓ Chính xác"
                                    : "✕ Chưa chính xác"}
                            </span>
                        </div>


                        <div className="result-answer">

                            <strong>
                                Đáp án đúng:
                            </strong>

                            <span>
                                {result.dapAnDung}
                            </span>

                        </div>


                        {result.giaiThich && (
                            <div className="result-explanation">

                                <strong>
                                    💡 Giải thích
                                </strong>

                                <p>
                                    {result.giaiThich}
                                </p>

                            </div>
                        )}

                    </div>
                )}

            </section>
        );
    };


    return (
        <SiteLayout>

            <div className="practice-page">

                {/* HEADER */}
                <div className="practice-header">

                    <div>

                        <span className="practice-badge">
                            TOEIC PRACTICE
                        </span>

                        <h1>
                            Luyện tập TOEIC
                        </h1>

                        <p>
                            Bài luyện #{data.maKetQua}
                        </p>

                    </div>


                    <div className="practice-total">

                        <strong>
                            {answeredCount}
                        </strong>

                        <span>
                            / {questions.length} câu
                        </span>

                    </div>

                </div>


                {/* PROGRESS */}
                <div className="practice-progress-wrapper">

                    <div className="practice-progress-info">

                        <span>
                            Tiến độ làm bài
                        </span>

                        <strong>
                            {progress}%
                        </strong>

                    </div>


                    <div className="practice-progress">

                        <div
                            className="practice-progress-bar"
                            style={{
                                width:
                                    `${progress}%`,
                            }}
                        />

                    </div>

                </div>


                {/* ERROR */}
                {error && (
                    <div className="practice-error">
                        ⚠️ {error}
                    </div>
                )}


                {/* CONTENT */}
                <div className="practice-questions">

                    {questionGroups.map(
                        (group) => {

                            const firstQuestion =
                                group.questions[0];

                            const firstIndex =
                                questions.findIndex(
                                    q =>
                                        q.maCauHoiLuotLam ===
                                        firstQuestion.maCauHoiLuotLam
                                );


                            return (
                                <div
                                    key={group.id}
                                    className={
                                        group.type === "group"
                                            ? "practice-question-group"
                                            : "practice-question-single"
                                    }
                                >

                                    {/* SOURCE */}
                                    {renderSource(
                                        firstQuestion,
                                        group.id
                                    )}


                                    {/* QUESTIONS */}
                                    {group.questions.map(
                                        (
                                            question,
                                            groupIndex
                                        ) =>
                                            renderQuestion(
                                                question,
                                                firstIndex +
                                                groupIndex
                                            )
                                    )}

                                </div>
                            );
                        }
                    )}

                </div>


                {/* SUBMIT */}
                <div className="practice-submit-area">

                    <div>

                        <strong>
                            Bạn đã trả lời{" "}
                            {answeredCount}/
                            {questions.length} câu
                        </strong>

                        <p>
                            Kiểm tra lại đáp án trước
                            khi nộp bài.
                        </p>

                    </div>


                    <button
                        type="button"
                        className="submit-practice-button"
                        onClick={
                            handleSubmit
                        }
                    >
                        🏁 Nộp bài
                    </button>

                </div>

            </div>

        </SiteLayout>
    );
}