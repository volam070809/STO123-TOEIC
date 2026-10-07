import { useContext, useState } from "react";
import { useNavigate } from "react-router-dom";
import { practiceApi } from "../../services/practiceApi";
import { AuthContext } from "../../contexts/AuthState";
import "../../pages/practice/practice-setup.css";
import SiteLayout from "../../layouts/SiteLayout";

export default function PracticeSetupPage() {
  const navigate = useNavigate();
  const { token } = useContext(AuthContext);

  const [maPart, setMaPart] = useState(1);
  const [soCau, setSoCau] = useState(10);

  const [soCauDe, setSoCauDe] = useState(4);
  const [soCauTrungBinh, setSoCauTrungBinh] = useState(4);
  const [soCauKho, setSoCauKho] = useState(2);

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const totalDifficulty =
    Number(soCauDe) +
    Number(soCauTrungBinh) +
    Number(soCauKho);

  const handleStart = async () => {
    setError("");

    if (totalDifficulty !== Number(soCau)) {
      setError("Tổng số câu theo độ khó phải bằng tổng số câu.");
      return;
    }

    if (Number(soCau) <= 0) {
      setError("Số câu phải lớn hơn 0.");
      return;
    }

    if (maPart === 3) {
      if (Number(soCau) % 3 !== 0) {
        setError("Part 3 phải chọn số câu là bội số của 3.");
        return;
      }

      if (
        Number(soCauDe) % 3 !== 0 ||
        Number(soCauTrungBinh) % 3 !== 0 ||
        Number(soCauKho) % 3 !== 0
      ) {
        setError(
          "Số câu theo từng độ khó của Part 3 phải là bội số của 3."
        );
        return;
      }
    }

    try {
      setLoading(true);

      const data = await practiceApi.start(
        {
          maPart,
          soCau: Number(soCau),
          soCauDe: Number(soCauDe),
          soCauTrungBinh: Number(soCauTrungBinh),
          soCauKho: Number(soCauKho),
        },
        token
      );

      navigate(`/practice/toeic/${data.maKetQua}`, {
        state: data,
      });
    } catch (err) {
      setError(err.message || "Không thể bắt đầu luyện tập.");
    } finally {
      setLoading(false);
    }
  };
  return (
    <SiteLayout>
      <div className="practice-setup-page">
        <div className="practice-setup-header">
          <span className="practice-setup-badge">TOEIC PRACTICE</span>

          <h1>🎯 Luyện tập TOEIC</h1>

          <p>
            Tùy chỉnh bài luyện theo Part, số lượng câu hỏi và độ khó
            phù hợp với bạn.
          </p>
        </div>

        <div className="practice-setup-card">

          {/* PART */}
          <section className="setup-section">
            <div className="section-title">
              <span className="section-icon">📚</span>
              <div>
                <h2>Chọn Part</h2>
                <p>Chọn phần TOEIC bạn muốn luyện tập.</p>
              </div>
            </div>

            <div className="part-grid">
              {[1, 2, 3, 4, 5, 6, 7].map(part => (
                <button
                  key={part}
                  type="button"
                  className={`part-option ${
                    maPart === part ? "active" : ""
                  }`}
                  onClick={() => setMaPart(part)}
                >
                  <span>Part</span>
                  <strong>{part}</strong>
                </button>
              ))}
            </div>
          </section>

          {/* SỐ CÂU */}
          <section className="setup-section">
            <div className="section-title">
              <span className="section-icon">📝</span>
              <div>
                <h2>Số câu hỏi</h2>
                <p>Bạn muốn luyện bao nhiêu câu?</p>
              </div>
            </div>

            <div className="question-count">
              <button
                type="button"
                onClick={() =>
                  setSoCau(value => Math.max(1, Number(value) - 1))
                }
              >
              </button>
              <div className="question-count-input">
                <input
                  type="number"
                  min="1"
                  value={soCau}
                  onChange={e => {
                    const value = e.target.value;

                    if (value === "") {
                      setSoCau("");
                      return;
                    }

                    setSoCau(Math.max(1, Number(value)));
                  }}
                />

                <span>câu</span>
              </div>

              <button
                type="button"
                onClick={() =>
                  setSoCau(value => Number(value || 1) + 1)
                }
              >
                +
              </button>
            </div>
          </section>

          {/* ĐỘ KHÓ */}
          <section className="setup-section">
            <div className="section-title">
              <span className="section-icon">⚡</span>
              <div>
                <h2>Phân bố độ khó</h2>
                <p>
                  Chọn số lượng câu dễ, trung bình và khó.
                </p>
              </div>
            </div>

            <div className="difficulty-grid">

              <div className="difficulty-card easy">
                <div className="difficulty-icon">🟢</div>

                <div className="difficulty-info">
                  <strong>Dễ</strong>
                  <span>Cơ bản</span>
                </div>

                <input
                  type="number"
                  min="0"
                  value={soCauDe}
                  onChange={e => setSoCauDe(Number(e.target.value))}
                />
              </div>

              <div className="difficulty-card medium">
                <div className="difficulty-icon">🟡</div>

                <div className="difficulty-info">
                  <strong>Trung bình</strong>
                  <span>Vừa sức</span>
                </div>

                <input
                  type="number"
                  min="0"
                  value={soCauTrungBinh}
                  onChange={e =>
                    setSoCauTrungBinh(Number(e.target.value))
                  }
                />
              </div>

              <div className="difficulty-card hard">
                <div className="difficulty-icon">🔴</div>

                <div className="difficulty-info">
                  <strong>Khó</strong>
                  <span>Thử thách</span>
                </div>

                <input
                  type="number"
                  min="0"
                  value={soCauKho}
                  onChange={e => setSoCauKho(Number(e.target.value))}
                />
              </div>

            </div>

            <div
              className={`difficulty-summary ${
                totalDifficulty === Number(soCau)
                  ? "valid"
                  : "invalid"
              }`}
            >
              <span>
                Đã chọn
              </span>

              <strong>
                {totalDifficulty} / {soCau}
              </strong>

              <span>câu</span>
            </div>
          </section>

          {/* ERROR */}
          {error && (
            <div className="practice-error">
              ⚠️ {error}
            </div>
          )}

          {/* START */}
          <button
            type="button"
            className="start-practice-button"
            onClick={handleStart}
            disabled={loading}
          >
            {loading ? (
              <>
                <span className="loading-spinner" />
                Đang tạo bài...
              </>
            ) : (
              <>
                🚀 Bắt đầu luyện tập
              </>
            )}
          </button>

        </div>
      </div>
    </SiteLayout>
  );
}