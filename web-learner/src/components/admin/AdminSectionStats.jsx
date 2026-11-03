import { useEffect, useState } from "react";
import { useAuth } from "../../contexts/AuthState";
import { apiRequest } from "../../services/api";
import "../../styles/admin-section-stats.css";

const formatNumber = value => typeof value === "number" ? new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 1 }).format(value) : value ?? "—";
const percent = value => value == null ? "Chưa có dữ liệu" : `${formatNumber(value)}%`;

export default function AdminSectionStats({ endpoint, title = "Thống kê", revision = 0, questionIds }) {
  const { token } = useAuth();
  const [reload, setReload] = useState(0);
  const [state, setState] = useState({ key: null, data: null, error: "" });
  const key = `${token}|${endpoint}|${revision}|${reload}`;
  useEffect(() => {
    let live = true;
    apiRequest(`/api/admin/statistics/${endpoint}`, { token, timeoutMs: 60_000 })
      .then(data => { if (live) setState({ key, data, error: "" }); })
      .catch(error => { if (live) setState({ key, data: null, error: error.message || "Không tải được thống kê." }); });
    return () => { live = false; };
  }, [endpoint, token, key]);

  const { data, error } = state.key === key ? state : { data: null, error: "" };
  const loading = state.key !== key;
  const selectedAnswers = questionIds?.length ? data?.answerByQuestion?.filter(row => questionIds.includes(row.id)) : null;
  return <section className="admin-section-stats" aria-label={title}>
    <div className="admin-section-stats-head"><h2>{title}</h2><button type="button" onClick={() => setReload(value => value + 1)} disabled={loading}>Làm mới</button></div>
    {loading && <p role="status">Đang tải thống kê…</p>}
    {error && <p role="alert">{error} <button type="button" onClick={() => setReload(value => value + 1)}>Thử lại</button></p>}
    {data && <>
      <div className="admin-section-metrics">{data.metrics?.map(item => <div key={item.label} className="admin-section-metric"><span>{item.label}</span><strong>{formatNumber(item.value)}</strong>{item.note && <small>{item.note}</small>}</div>)}</div>
      {data.byPart?.length > 0 && <div className="admin-section-breakdown"><h3>Theo Part</h3><div className="admin-section-table-wrap"><table><thead><tr><th>Part</th><th>Câu</th><th>Nhóm</th><th>Lượt trả lời có dữ liệu</th><th>Tỷ lệ đúng</th></tr></thead><tbody>{data.byPart.map(row => <tr key={row.part}><th>Part {row.part}</th><td>{row.questions}</td><td>{row.groups}</td><td>{row.attempts || "—"}</td><td>{percent(row.accuracy)}</td></tr>)}</tbody></table></div></div>}
      {data.byDifficulty?.length > 0 && <div className="admin-section-inline"><strong>Độ khó:</strong>{data.byDifficulty.map(row => <span key={row.level}>Mức {row.level}: {row.count} câu</span>)}</div>}
      {selectedAnswers?.length > 0 && <div className="admin-section-inline"><strong>Lượt trả lời đã lưu:</strong>{selectedAnswers.map(row => <span key={row.id}>Câu {row.id}: {row.attempts} lượt · đúng {percent(row.accuracy)}</span>)}</div>}
      {data.learners?.length > 0 && <div className="admin-section-breakdown"><h3>Tiến độ học viên</h3><div className="admin-section-table-wrap"><table><thead><tr><th>Học viên</th><th>Tiến độ</th><th>Hoàn thành</th><th>Chưa xong</th></tr></thead><tbody>{data.learners.slice(0, 30).map(row => <tr key={row.learnerId}><th>{row.learnerName}</th><td>{row.percent}%</td><td>{row.completed}</td><td>{row.incomplete}</td></tr>)}</tbody></table></div>{data.learners.length > 30 && <small>Hiển thị 30/{data.learners.length} học viên.</small>}</div>}
      {data.quizResults?.length > 0 && <div className="admin-section-breakdown"><h3>Kết quả bài tập gần đây</h3><div className="admin-section-table-wrap"><table><thead><tr><th>Học viên</th><th>Lần làm</th><th>Trạng thái</th><th>Đúng / đã kiểm tra</th></tr></thead><tbody>{data.quizResults.map((row, index) => <tr key={index}><th>{row.learner}</th><td>{row.attempt}</td><td>{row.status}</td><td>{row.correct}/{row.answered} · {row.total} câu</td></tr>)}</tbody></table></div></div>}
      {data.recentAttempts?.length > 0 && <div className="admin-section-breakdown"><h3>Lượt thi hoàn thành gần đây</h3><div className="admin-section-table-wrap"><table><thead><tr><th>Học viên</th><th>Điểm tổng</th><th>Nghe</th><th>Đọc</th><th>Ngày nộp</th></tr></thead><tbody>{data.recentAttempts.map((row, index) => <tr key={index}><th>{row.learner}</th><td>{row.diemTong}</td><td>{row.diemNghe}</td><td>{row.diemDoc}</td><td>{row.ngayNopBai ? new Date(row.ngayNopBai).toLocaleString("vi-VN") : "—"}</td></tr>)}</tbody></table></div></div>}
      {data.topics?.length > 0 && <div className="admin-section-breakdown"><h3>Từ vựng theo chủ đề</h3><div className="admin-section-table-wrap"><table><thead><tr><th>Chủ đề</th><th>Số từ</th></tr></thead><tbody>{data.topics.slice(0, 20).map(row => <tr key={row.id}><th>{row.name}</th><td>{row.words}</td></tr>)}</tbody></table></div></div>}
      {data.partsOfSpeech?.length > 0 && <div className="admin-section-inline"><strong>Theo loại từ:</strong>{data.partsOfSpeech.map(row => <span key={row.name ?? "none"}>{row.name || "Chưa phân loại"}: {row.count}</span>)}</div>}
      {data.resourceCompletionAvailable === false && <p className="admin-section-note">Tiến độ hiện được lưu theo hoạt động học; chưa có dữ liệu hoàn thành cho từng tệp.</p>}
    </>}
  </section>;
}
