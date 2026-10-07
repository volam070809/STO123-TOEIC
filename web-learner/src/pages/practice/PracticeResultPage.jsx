import { useEffect, useState, useContext } from "react";
import { useParams, useNavigate } from "react-router-dom";
import { practiceApi } from "../../services/practiceApi";
import { AuthContext } from "../../contexts/AuthState";
import "../../pages/practice/practice-result.css";

export default function PracticeResultPage() {
    const { maKetQua } = useParams();
    const navigate = useNavigate();
    const { token } = useContext(AuthContext);

    const [result, setResult] = useState(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState("");

    useEffect(() => {
        async function loadResult() {
            try {
                const data = await practiceApi.result(
                    maKetQua,
                    token
                );

                setResult(data);
            } catch (err) {
                setError(
                    err.message ||
                    "Không thể tải kết quả."
                );
            } finally {
                setLoading(false);
            }
        }

        if (token) {
            loadResult();
        }
    }, [maKetQua, token]);

    if (loading) {
        return (
            <div className="practice-result-page">
                <div className="result-loading">
                    <span className="result-spinner" />
                    <p>Đang tải kết quả...</p>
                </div>
            </div>
        );
    }

    if (error) {
        return (
            <div className="practice-result-page">
                <div className="result-error">
                    ⚠️ {error}
                </div>
            </div>
        );
    }

    if (!result) {
        return (
            <div className="practice-result-page">
                <div className="result-empty">
                    <div>📚</div>
                    <h2>Không tìm thấy kết quả</h2>
                    <button
                        type="button"
                        onClick={() =>
                            navigate("/practice/toeic")
                        }
                    >
                        Quay lại luyện tập
                    </button>
                </div>
            </div>
        );
    }

    const total = Number(result.soCau) || 0;
    const correct = Number(result.soCauDung) || 0;
    const wrong = Number(result.soCauSai) || 0;
    const score = Number(result.diemTong) || 0;

    const percent =
        total > 0
            ? Math.round((correct / total) * 100)
            : 0;

    return (
        <div className="practice-result-page">

            {/* HEADER */}
            <div className="result-header">
                <span className="result-badge">
                    TOEIC PRACTICE
                </span>

                <h1>🎉 Kết quả luyện tập</h1>

                <p>
                    Bạn đã hoàn thành bài luyện TOEIC
                </p>
            </div>

            {/* SCORE */}
            <div className="result-main-card">

                <div className="score-circle">
                    <strong>{score}</strong>
                    <span>điểm</span>
                </div>

                <div className="score-info">
                    <h2>
                        {score >= 80
                            ? "Xuất sắc! 🔥"
                            : score >= 60
                            ? "Làm tốt lắm! 👍"
                            : score >= 40
                            ? "Cố gắng thêm nhé! 💪"
                            : "Hãy tiếp tục luyện tập! 📚"}
                    </h2>

                    <p>
                        Bạn trả lời đúng{" "}
                        <strong>{correct}</strong>
                        {" "}trên{" "}
                        <strong>{total}</strong> câu.
                    </p>

                    <div className="score-progress">
                        <div
                            style={{
                                width: `${percent}%`,
                            }}
                        />
                    </div>

                    <span className="score-percent">
                        {percent}% chính xác
                    </span>
                </div>

            </div>

            {/* STATISTICS */}
            <div className="result-stats">

                <div className="result-stat">
                    <span className="stat-icon">📝</span>

                    <div>
                        <strong>{total}</strong>
                        <span>Tổng số câu</span>
                    </div>
                </div>

                <div className="result-stat correct">
                    <span className="stat-icon">✓</span>

                    <div>
                        <strong>{correct}</strong>
                        <span>Câu đúng</span>
                    </div>
                </div>

                <div className="result-stat wrong">
                    <span className="stat-icon">✕</span>

                    <div>
                        <strong>{wrong}</strong>
                        <span>Câu sai</span>
                    </div>
                </div>

                <div className="result-stat accuracy">
                    <span className="stat-icon">🎯</span>

                    <div>
                        <strong>{percent}%</strong>
                        <span>Độ chính xác</span>
                    </div>
                </div>

            </div>

            {/* INFO */}
            <div className="result-info-card">

                <div>
                    <span>Mã kết quả</span>
                    <strong>#{result.maKetQua}</strong>
                </div>

                <div>
                    <span>Trạng thái</span>
                    <strong className="status-done">
                        ✓ Đã hoàn thành
                    </strong>
                </div>

            </div>

            {/* ACTIONS */}
            <div className="result-actions">

                <button
                    type="button"
                    className="result-primary-button"
                    onClick={() =>
                        navigate("/practice/toeic")
                    }
                >
                    🚀 Luyện tập lại
                </button>

                <button
                    type="button"
                    className="result-outline-button"
                    onClick={() =>
                        navigate("/practice/toeic/history")
                    }
                >
                    📊 Xem lịch sử
                </button>

            </div>

        </div>
    );
}