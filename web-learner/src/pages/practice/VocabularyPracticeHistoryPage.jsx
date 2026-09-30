import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import { useAuth } from "../../contexts/AuthState";
import { apiRequest } from "../../services/api";
import "../../styles/vocabulary.css";
import "../../styles/vocabulary-practice.css";

const ROOT = "/api/tu-vung/practice-history";
const dateText = value => value ? new Date(value + (/[zZ]|[+-]\d\d:\d\d$/.test(value) ? "" : "Z"))
  .toLocaleString("vi-VN") : "";
const optionValue = (detail, letter) => detail["phuongAn" + letter] || "";

export default function VocabularyPracticeHistoryPage() {
  const { id } = useParams();
  const { token } = useAuth();
  const [response, setResponse] = useState(null);
  const [reload, setReload] = useState(0);
  const requestKey = `${token}:${id || "list"}:${reload}`;
  const loading = response?.key !== requestKey;
  const data = loading ? null : response.data;
  const error = loading ? "" : response.error;

  useEffect(() => {
    let active = true;
    apiRequest(ROOT + (id ? "/" + id : ""), { token })
      .then(value => { if (active) setResponse({ key: requestKey, data: value, error: "" }); })
      .catch(err => { if (active) setResponse({ key: requestKey, data: null, error: err.status === 404
        ? "Không tìm thấy lượt luyện tập này." : "Không thể tải lịch sử luyện tập." }); });
    return () => { active = false; };
  }, [id, token, reload, requestKey]);

  return <SiteLayout className="vocab-page"><div className="site-container practice-page">
    <div className="page-heading">
      <span>Luyện tập / Lịch sử luyện từ</span>
      <h1>{id ? "Chi tiết lượt luyện" : "Lịch sử luyện từ"}</h1>
    </div>
    <div className="practice-actions">
      <Link className="vocab-text-button" to={id ? "/practice/vocabulary/history" : "/practice/vocabulary"}>
        ← {id ? "Lịch sử luyện từ" : "Luyện từ vựng"}
      </Link>
    </div>
    {loading && <p role="status">Đang tải lịch sử luyện tập...</p>}
    {error && <div className="vocab-error" role="alert"><p>{error}</p>
      <button type="button" className="outline-button" onClick={() => setReload(value => value + 1)}>Thử lại</button>
    </div>}
    {!loading && !error && !id && (data.length ? <div className="practice-history-list">
      {data.map(item => <article className="vocab-panel practice-panel" key={item.maLanLuyen}>
        <h2>{item.tenChuDe}</h2>
        <p>{dateText(item.ngayNopBai)}</p>
        <p><strong>{item.correct}/{item.total} câu đúng{item.percent !== null ? ` · ${item.percent}%` : ""}</strong></p>
        <Link className="outline-button" to={"/practice/vocabulary/history/" + item.maLanLuyen}>Xem chi tiết</Link>
      </article>)}
    </div> : <section className="vocab-panel practice-panel">
      <p>Bạn chưa có lịch sử luyện từ vựng.</p>
      <Link className="primary-button" to="/practice/vocabulary">Bắt đầu luyện</Link>
    </section>)}
    {!loading && !error && id && data && <>
      <section className="vocab-panel practice-panel">
        <h2>{data.tenChuDe}</h2>
        <p>{dateText(data.ngayNopBai)}</p>
        <p className="practice-score">{data.correct}/{data.total} câu đúng{data.percent !== null ? ` · ${data.percent}%` : ""}</p>
        <p>Tổng số câu: {data.total} · Đúng: {data.correct} · Sai: {data.incorrect}</p>
      </section>
      {data.details.map(detail => <section className="vocab-panel practice-panel" key={detail.maTuVung}>
        <p>Câu {detail.thuTu} · <strong>{detail.laDung === true ? "Đúng" : detail.laDung === false ? "Sai" : "Chưa có kết quả"}</strong></p>
        <h3>{detail.word}</h3>
        <p>{detail.noiDungCauHoi}</p>
        {detail.dapAnChon && <p>Đã chọn: {optionValue(detail, detail.dapAnChon)} ({detail.dapAnChon})</p>}
        {detail.phuongAnDung && <p>Đáp án đúng: {optionValue(detail, detail.phuongAnDung)} ({detail.phuongAnDung})</p>}
      </section>)}
    </>}
  </div></SiteLayout>;
}
