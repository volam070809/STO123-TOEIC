import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { examApi, placementApi } from "../../services/examApi";
import { examNavigation } from "./examNavigation";
import { historicalPlacementAnalysis, shouldLoadPlacementAnalysis, waitForPlacementAnalysis } from "./placementAttemptAnalysis";
import PlacementAnalysisView from "./PlacementAnalysisView";
import "../../styles/exam.css";

export default function ExamResultPage() {
  const { attemptId } = useParams();
  const { token } = useAuth();
  const navigate = useNavigate();
  const [result, setResult] = useState(null);
  const [retrying, setRetrying] = useState(false);
  const [error, setError] = useState("");
  const [analysis, setAnalysis] = useState(null);
  const [analysisRequest, setAnalysisRequest] = useState(0);
  const [courseData, setCourseData] = useState(null);
  const [target, setTarget] = useState("");
  const [savingTarget, setSavingTarget] = useState(false);
  const [analysisActionError, setAnalysisActionError] = useState("");
  useEffect(() => {
    let live = true;
    examApi.result(attemptId, token).then(data => { if (live) setResult(data); })
      .catch(e => { if (live) setError(e.data?.code === "ATTEMPT_NOT_FINALIZED" ?
        "Bài thi chưa kết thúc." : "Không thể tải kết quả."); });
    return () => { live = false; };
  }, [attemptId, token]);
  useEffect(() => {
    if (!shouldLoadPlacementAnalysis(result, attemptId)) return;
    let live = true;
    waitForPlacementAnalysis(attemptId, token, placementApi, { isActive: () => live })
      .then(data => {
        if (!live) return;
        setAnalysis({ attemptId, ...data });
        if (data.status !== "available") return;
        if (data.current) setTarget(data.outcome.targetScore?.toString() ?? "");
        placementApi.courses(token)
          .then(items => { if (live) setCourseData({ attemptId, items }); })
          .catch(() => { if (live) setCourseData({ attemptId, error: true, items: [] }); });
      })
      .catch(() => { if (live) setAnalysis({ attemptId, status: "error" }); });
    return () => { live = false; };
  }, [result, attemptId, token, analysisRequest]);
  const currentResult = String(result?.attemptId) === String(attemptId) ? result : null;
  const tested = currentResult?.parts.filter(p => p.stats.total > 0) || [];
  const partMock = currentResult?.source === "PART";
  const navigation = examNavigation(currentResult);
  async function retry() {
    if (retrying || currentResult?.source !== "PLACEMENT") return;
    setRetrying(true); setError("");
    try {
      const started = await placementApi.start(token);
      navigate(`/exam/${started.attemptId}`);
    } catch { setError("Không thể bắt đầu lại bài thi."); }
    finally { setRetrying(false); }
  }
  async function confirmTarget(event) {
    event.preventDefault();
    if (savingTarget || analysis?.attemptId !== attemptId || !analysis.current) return;
    setSavingTarget(true); setAnalysisActionError("");
    try {
      const saved = await placementApi.target(Number(target), token);
      const items = await placementApi.courses(token);
      setAnalysis(previous => ({ ...previous, outcome: { ...previous.outcome, targetScore: saved.targetScore } }));
      setCourseData({ attemptId, items });
    } catch { setAnalysisActionError("Không thể lưu mục tiêu hoặc tải khóa học đề xuất. Vui lòng thử lại."); }
    finally { setSavingTarget(false); }
  }
  return <SiteLayout><div className="site-container exam-result">
    <div className="page-heading"><span>{currentResult?.source === "PLACEMENT" ? "Lộ trình học" : "Thi thử"} / Kết quả</span>
      <h1>{currentResult?.examName || "Kết quả bài thi"}</h1>
      {currentResult?.examCode && <p>{currentResult.examCode}</p>}</div>
    {error && <p className="exam-error" role="alert">{error}</p>}
    {!currentResult && !error && <p>Đang tải kết quả…</p>}
    {currentResult && <>
      {partMock ? <section className="exam-score"><p>Kết quả Part {tested[0]?.part}</p>
        <strong>{tested[0]?.stats.correct} / {tested[0]?.stats.total}</strong>
        <p>{tested[0]?.stats.percentage}% câu đúng</p></section> :
        <section className="exam-score"><p>Điểm TOEIC ước tính</p>
          <strong>{result.totalScore ?? "—"} <small>/ 990</small></strong>
          <div className="exam-score-sections"><div>Listening <b>{result.listeningScore ?? "—"} / 495</b>
            <small>{result.listening.correct} / {result.listening.total} câu đúng</small></div>
            <div>Reading <b>{result.readingScore ?? "—"} / 495</b>
              <small>{result.reading.correct} / {result.reading.total} câu đúng</small></div></div></section>}
      <section><h2>Kết quả câu hỏi</h2><div className="exam-stat-grid">
        <div><b>{result.overall.correct} / {result.overall.total}</b><span>Đúng</span></div>
        <div><b>{result.overall.incorrect} / {result.overall.total}</b><span>Sai</span></div>
        <div><b>{result.overall.unanswered} / {result.overall.total}</b><span>Chưa trả lời</span></div>
      </div></section>
      <section><h2>Theo từng Part</h2><div className="exam-part-list">
        {tested.map(row => <div key={row.part}><strong>Part {row.part}</strong>
          <span>{row.stats.correct}/{row.stats.total} đúng</span><span>{row.stats.percentage}%</span></div>)}</div></section>
      {result.source === "PLACEMENT" && <section className="placement-result-analysis" aria-labelledby="placement-result-title">
        <div className="placement-result-heading"><div><span className="placement-eyebrow">Đánh giá sau bài kiểm tra</span>
          <h2 id="placement-result-title">Phân tích năng lực bằng KNN</h2></div></div>
        {analysis?.attemptId !== attemptId && <p role="status">Đang phân tích năng lực… Kết quả sẽ tự cập nhật khi sẵn sàng.</p>}
        {analysis?.attemptId === attemptId && ["error", "missing"].includes(analysis.status) && <>
          <p role={analysis.status === "error" ? "alert" : "status"}>
            {analysis.status === "error" ? "Không thể tải phân tích KNN. Kết quả thi vẫn được giữ nguyên." :
              "Lượt làm này chưa có kết quả phân lớp sau khi kiểm tra lại. Kết quả thi vẫn được giữ nguyên."}</p>
          <button type="button" className="outline-button" onClick={() => { setAnalysis(null); setAnalysisRequest(value => value + 1); }}>
            Thử tải lại phân tích</button>
        </>}
        {analysis?.attemptId === attemptId && analysis.status === "available" && <>
          <PlacementAnalysisView
            analysis={analysis.current ? analysis.outcome : historicalPlacementAnalysis(result, analysis.stage)}
            current={analysis.current} courses={courseData?.attemptId === attemptId ? courseData.items : []}
            target={target} onTargetChange={setTarget} onTargetSubmit={confirmTarget} pending={savingTarget}
            coursesLoaded={courseData?.attemptId === attemptId && !courseData.error} />
          {courseData?.attemptId !== attemptId && <p role="status">Đang tải khóa học phù hợp…</p>}
          {courseData?.attemptId === attemptId && courseData.error &&
            <><p role="alert">Không thể tải khóa học hiện có.</p>
              <button type="button" className="outline-button" onClick={() => { setCourseData(null); setAnalysis(null); setAnalysisRequest(value => value + 1); }}>
                Thử tải lại phân tích và khóa học</button></>}
          {analysisActionError && <p role="alert">{analysisActionError}</p>}
        </>}
      </section>}
      <div className="exam-navigation">
        <Link className="outline-button" to={navigation.back}>← Quay lại</Link>
        {result.source === "PLACEMENT" && <Link className="primary-button" to={`/exam/${attemptId}/review`}>Xem lại bài</Link>}
        {result.source === "PLACEMENT" &&
          <button className="outline-button" type="button" disabled={retrying} onClick={retry}>
            Làm lại kiểm tra</button>}
        {navigation.root !== navigation.back && <Link className="outline-button" to={navigation.root}>
          {navigation.rootLabel}</Link>}
      </div>
    </>}
  </div></SiteLayout>;
}
