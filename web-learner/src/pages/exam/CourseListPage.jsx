import { useEffect, useRef, useState } from "react";
import { Link, useLocation } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import SiteLayout from "../../layouts/SiteLayout";
import { placementApi } from "../../services/examApi";
import { paymentApi } from "../../services/paymentApi";
import { checkoutErrorMessage, paymentIdFromSearch, validatedCheckoutUrl } from "../../services/paymentFlow";
import { usePaymentStatus } from "../../hooks/usePaymentStatus";
import { CourseCover } from "./coursePresentation";
import { courseAudienceLabel, courseEntryReference, courseStage, courseTargetLabel } from "./coursePresentationData";
import { publicCourseApi } from "../../services/publicCourseApi";
import { useContentRevision } from "../../services/contentRevisions";
import { useGuestAuthPrompt } from "../../contexts/GuestAuthPromptState";
import { loadCourseCatalog, requestGuestCoursePurchase } from "./coursePublicAccess";
import "../../styles/course.css";

const money = new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND" });
const date = value => {
  if (!value) return "";
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? "" : parsed.toLocaleDateString("vi-VN");
};
const accessLabels = {
  NOT_PURCHASED: "Chưa mua", PAYMENT_PENDING: "Đang chờ thanh toán",
  ACTIVE: "Đang có quyền học", EXPIRED: "Đã hết hạn", PAYMENT_FAILED: "Thanh toán thất bại",
};
const paymentLabels = { CHO_XU_LY: "Đang chờ xác nhận", THANH_CONG: "Thành công", THAT_BAI: "Thất bại", QUA_HAN: "Quá hạn" };

export default function CourseListPage() {
  const { token, user } = useAuth();
  const { requireAuthentication } = useGuestAuthPrompt();
  const isGuest = user?.vaiTro !== "HOC_VIEN";
  const learnerToken = isGuest ? null : token;
  const { search } = useLocation();
  const paymentId = paymentIdFromSearch(search);
  const { view: paymentView, refresh: refreshPayment } = usePaymentStatus(paymentId, learnerToken);
  const terminalPayment = ["success", "failed", "expired"].includes(paymentView.kind) ? paymentView.kind : null;
  const [paymentNow, setPaymentNow] = useState(() => Date.now());
  useEffect(() => {
    if (paymentView.kind !== "pending") return undefined;
    const timer = window.setInterval(() => setPaymentNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [paymentView.kind]);
  const retrySeconds = paymentView.retryAvailableAt ? Math.max(0,
    Math.ceil((new Date(paymentView.retryAvailableAt).getTime() - paymentNow) / 1000)) : null;
  const [revision, setRevision] = useState(0);
  const contentRevision = useContentRevision("COURSES", "ACCESS", "EXAMS");
  const [view, setView] = useState({ token: null, courses: null, placement: null, history: null, error: "" });
  const [selected, setSelected] = useState([]);
  const [quotation, setQuotation] = useState({ key: "", data: null, error: "" });
  const [quoteRevision, setQuoteRevision] = useState(0);
  const [checkout, setCheckout] = useState({ busy: false, message: "" });
  const busyRef = useRef(false);
  useEffect(() => {
    let live = true;
    const request = loadCourseCatalog(learnerToken, {
      loadLearnerCourses: placementApi.courses,
      loadPlacement: placementApi.summary,
      loadHistory: paymentApi.history,
      loadPublicCourses: publicCourseApi.list,
    });
    request
      .then(([courses, placement, history]) => {
        if (live) setView({ token, courses, placement, history, error: "" });
      }).catch(() => {
        if (live) setView({ token, courses: null, placement: null, history: null,
          error: "Không thể tải thông tin khóa học. Vui lòng thử lại." });
      });
    return () => { live = false; };
  }, [token, learnerToken, revision, terminalPayment, contentRevision]);

  const current = view.token === token ? view : { courses: null, placement: null, history: null, error: "" };
  const { courses, placement, history, error } = current;
  const retryCourses = history?.find(item => item.maGiaoDich === paymentId)?.courseIds || [];
  const recommended = courses?.filter(item => item.recommended) ?? [];
  const owned = courses?.filter(item => ["ACTIVE", "EXPIRED"].includes(item.accessStatus)) ?? [];
  const selectionKey = selected.join(",");
  const quoted = quotation.key === selectionKey ? quotation : { data: null, error: "" };
  useEffect(() => {
    if (!learnerToken || !selectionKey) return;
    let live = true;
    paymentApi.quote(selectionKey.split(",").map(Number), token).then(data => {
      if (live) setQuotation({ key: selectionKey, data, error: "" });
    }).catch(failure => {
      if (live) setQuotation({ key: selectionKey, data: null, error: checkoutErrorMessage(failure) });
    });
    return () => { live = false; };
  }, [token, learnerToken, selectionKey, quoteRevision, contentRevision]);
  function toggle(id) {
    setSelected(current => current.includes(id) ? current.filter(value => value !== id) :
      [...current, id].sort((a, b) => a - b));
    setCheckout({ busy: false, message: "" });
  }
  function prepareRetry(ids) {
    if (!ids?.length) return;
    setSelected([...ids].sort((a, b) => a - b));
    setQuoteRevision(value => value + 1);
    document.getElementById("course-quotation")?.scrollIntoView({ behavior: "smooth" });
  }
  async function purchase() {
    if (!quoted.data || !token || busyRef.current) return;
    busyRef.current = true;
    setCheckout({ busy: true, message: "" });
    try {
      const result = await paymentApi.createCourses(selected, quoted.data.total, token);
      const url = validatedCheckoutUrl(result?.checkoutUrl);
      if (!Number.isSafeInteger(result?.maGiaoDich) || result.maGiaoDich <= 0 || !url)
        throw new Error("INVALID_CHECKOUT_URL");
      window.location.assign(url);
    } catch (failure) {
      setCheckout({ busy: false, message: checkoutErrorMessage(failure) });
      busyRef.current = false;
      setRevision(value => value + 1);
      setQuoteRevision(value => value + 1);
    }
  }
  const paymentMessage = {
    success: "Thanh toán đã được xác nhận. Quyền học đã cập nhật.",
    failed: "Thanh toán thất bại. Bạn có thể thử lại.",
    expired: "Phiên thanh toán đã quá hạn. Bạn có thể tạo lần thanh toán mới; nếu đã bị trừ tiền, hãy liên hệ hỗ trợ để đối soát.",
    pending: "Đang chờ VNPay xác nhận. Nội dung vẫn khóa cho đến khi thanh toán thành công.",
    reconciliation: "Giao dịch cần được đối soát. Quyền học chưa được kích hoạt.",
    "not-found": "Không tìm thấy giao dịch thuộc tài khoản của bạn.",
    "network-error": "Chưa kiểm tra được trạng thái. Vui lòng thử lại.",
    unauthorized: "Vui lòng đăng nhập lại để kiểm tra giao dịch.",
  }[paymentView.kind] || "Đang kiểm tra thanh toán…";
  return <SiteLayout><main className="site-container course-page">
    <header className="course-page-heading"><div><h1>Khóa học TOEIC</h1>
      <p>{isGuest ? "Khám phá chương trình, giai đoạn và mức giá của các khóa học đang mở." :
        "Chọn một hoặc nhiều khóa học, xem giá chính xác và theo dõi quyền học tại đây."}</p></div></header>
    {learnerToken && paymentId && <div className="course-notice" role="status"><strong>Thanh toán #{paymentId}</strong>
      <span>{paymentMessage}{paymentView.kind === "pending" && retrySeconds != null &&
        ` Tự kiểm tra lại; có thể thanh toán lại sau ${Math.floor(retrySeconds / 60)}:${String(retrySeconds % 60).padStart(2, "0")}.`}</span>
      {["expired", "failed"].includes(paymentView.kind) && retryCourses.length > 0 &&
        <button type="button" onClick={() => prepareRetry(retryCourses)}>Thanh toán lại</button>}
      <button type="button" onClick={() => { refreshPayment(); setRevision(v => v + 1); }}>Cập nhật</button></div>}
    {search && !paymentId && <p className="exam-error" role="alert">Mã giao dịch trên liên kết không hợp lệ.</p>}
    {error && <p className="exam-error" role="alert">{error} <button type="button" onClick={() => setRevision(v => v + 1)}>Thử lại</button></p>}
    {!courses && !error && <p role="status">Đang tải khóa học…</p>}
    {courses && <>
      {!isGuest && owned.some(course => course.accessStatus === "ACTIVE") && <section className="course-learning-launch" aria-label="Khóa học đang học"><div><span>KHÔNG GIAN HỌC TẬP</span><h2>Tiếp tục hành trình của bạn</h2><p>Vào khóa học đang có quyền truy cập để xem Unit, bài học và tiến độ được lưu từ lần học gần nhất.</p></div><div className="course-learning-launch-list">{owned.filter(course => course.accessStatus === "ACTIVE").map(course => <Link key={course.courseId} to={`/courses/${course.courseId}`}><strong>{course.name}</strong><span>Tiếp tục học →</span></Link>)}</div></section>}
      {!isGuest && <section className="course-summary"><div><span className="course-kicker">ĐÁNH GIÁ ĐẦU VÀO</span>
        <h2>{placement?.currentStage ? `Giai đoạn khởi đầu ${placement.currentStage}` : "Chưa có kết quả KNN"}</h2>
        <p>{placement?.currentStage ? `Kết quả phân lớp từ bài đầu vào gần nhất. ${placement.targetScore ?
          `Mục tiêu TOEIC của bạn: ${placement.targetScore}.` : "Bạn chưa chọn điểm mục tiêu."}` :
          "Làm bài kiểm tra đầu vào để nhận lộ trình phù hợp."}</p>
        <Link to="/placement">{placement?.currentStage ? "Xem hoặc cập nhật bài đầu vào" : "Làm bài đầu vào"}</Link>
      </div><div><span className="course-kicker">LỘ TRÌNH CỦA BẠN</span>
        <div className="course-roadmap">{[1, 2, 3].map(stage =>
          <div className={recommended.some(course => course.stage === stage) ? "is-recommended" : ""} key={stage}>
            <strong>{stage}. {courseStage[stage].name}</strong><small>{courseEntryReference(stage)}</small>
          </div>)}</div>
        <p>{recommended.length ? `Theo giai đoạn KNN và mục tiêu đã chọn, bắt đầu từ giai đoạn ${recommended[0].stage} rồi học đến giai đoạn ${recommended.at(-1).stage}.` :
          "Xác nhận kết quả đầu vào và điểm mục tiêu để đánh dấu các giai đoạn đề xuất."}</p>
      </div></section>}
      {!isGuest && recommended.length > 0 && <section className="course-section"><h2>Khóa học đề xuất</h2>
        <p>Thứ tự dựa trên giai đoạn KNN và điểm mục tiêu. Bạn vẫn có thể chọn bất kỳ khóa học đang mở.</p>
        <div className="course-recommend-list">{recommended.map(course => <span key={course.courseId}>
          {course.recommendationOrder}. {course.name}</span>)}</div></section>}
      <section className="course-section"><h2>{isGuest ? "Danh sách khóa học" : "Chọn khóa học"}</h2>
        <p>{isGuest ? "Xem thông tin tổng quan; đăng nhập khi bạn muốn mua hoặc truy cập nội dung học." :
          "Mỗi khóa có thời hạn truy cập sau khi thanh toán được xác nhận. Tổng tiền bằng giá của các khóa đã chọn."}</p>
        {courses.length === 0 && <p>Chưa có khóa học đang mở.</p>}
        <div className="course-grid">{courses.map(course => {
          const purchasable = !["ACTIVE", "PAYMENT_PENDING"].includes(course.accessStatus) &&
            Number.isInteger(course.retailPrice) && course.retailPrice > 0;
          return <article className={`course-card ${course.recommended ? "is-recommended" : ""}`} key={course.courseId}>
            <CourseCover course={course} /><div className="course-card-body">
              <div className="course-badges"><span className="course-stage-badge">{courseAudienceLabel(course.stage)}</span>
                {course.recommended && <span className="course-recommend-badge">Đề xuất {course.recommendationOrder}</span>}</div>
              <h3>{course.name}</h3><div className="course-score-summary">
                <p>{courseEntryReference(course.stage)}</p>
                <strong>{courseTargetLabel(course.maxTargetScore)}</strong>
              </div>
              <p className="course-description">{course.description || "Thông tin khóa học đang được cập nhật."}</p>
              <div className="course-card-bottom">{!isGuest && <p className="course-access">{accessLabels[course.accessStatus]}
                {course.accessStatus === "ACTIVE" && course.accessExpiresAt ? ` · Đến ${date(course.accessExpiresAt)}` : ""}</p>}
                <div className="course-offer"><div className="course-price"><strong>{course.retailPrice > 0 ? money.format(course.retailPrice) :
                  "Chưa có giá bán"}</strong><span>{course.accessDays} ngày truy cập</span></div>
                  {!isGuest && purchasable && <label className="course-select"><input type="checkbox"
                    checked={selected.includes(course.courseId)} disabled={checkout.busy}
                    onChange={() => toggle(course.courseId)} /> Chọn khóa học</label>}</div>
                {isGuest ? <div className="course-card-actions">
                  <Link className="outline-button course-cta course-info-cta" to={`/courses/${course.courseId}`}>Xem thông tin</Link>
                  {!user && purchasable && <button type="button" className="primary-button course-cta"
                    onClick={() => requestGuestCoursePurchase(requireAuthentication)}>Mua khóa học</button>}
                </div> : course.accessStatus === "ACTIVE" && <Link className="primary-button course-cta" to={`/courses/${course.courseId}`}>Vào học</Link>}
              </div></div></article>;
        })}</div></section>
      {!isGuest && <section id="course-quotation" className="course-section course-quotation" aria-live="polite"><h2>Báo giá khóa học</h2>
        {!selected.length && <p>Chọn ít nhất một khóa học để xem báo giá từ hệ thống.</p>}
        {selected.length > 0 && !quoted.data && !quoted.error && <p>Đang tính giá…</p>}
        {quoted.error && <p className="exam-error" role="alert">{quoted.error}</p>}
        {quoted.data && <><ul>{quoted.data.courses.map(item => <li key={item.courseId}>
          <span>{item.name}</span><strong>{money.format(item.price)}</strong></li>)}</ul>
          <p>Thời hạn mỗi khóa: <strong>{quoted.data.validDays} ngày</strong></p>
          <p className="course-quote-total">Tổng thanh toán: <strong>{money.format(quoted.data.total)}</strong></p>
          <p>Quyền học chỉ được kích hoạt sau khi VNPay xác nhận thanh toán.</p>
          {checkout.message && <p className="exam-error" role="alert">{checkout.message}</p>}
          <button type="button" className="primary-button course-cta" disabled={checkout.busy} onClick={purchase}>
            {checkout.busy ? "Đang xử lý…" : "Xác nhận và tiếp tục đến VNPay"}</button></>}
      </section>}
      {!isGuest && <section className="course-section"><h2>Khóa học đã mua</h2>
        {owned.length ? <div className="course-owned">{owned.map(course => <div key={course.courseId}>
          <strong>{course.name}</strong><span>{accessLabels[course.accessStatus]}
            {course.accessExpiresAt ? ` · Đến ${date(course.accessExpiresAt)}` : ""}</span>
          {course.accessStatus === "ACTIVE" && <Link to={`/courses/${course.courseId}`}>Vào học</Link>}
        </div>)}</div> : <p>Chưa có khóa học đã thanh toán.</p>}
      </section>}
      {!isGuest && <section className="course-section course-payment-history" aria-label="Lịch sử thanh toán">
        <details><summary className="course-history-toggle"><span className="course-history-heading"><small>GIAO DỊCH CỦA BẠN</small>
          <strong>Lịch sử thanh toán</strong><span>{history?.length ? `${history.length} giao dịch` : "Chưa có giao dịch"}</span></span>
          <span className="course-history-open"><span className="course-history-open-closed">Xem chi tiết</span><span className="course-history-open-expanded">Thu gọn</span><span aria-hidden="true">⌄</span></span></summary>
          {history?.length ? <div className="course-history-list">{history.map(item => <article className="course-history-item" key={item.maGiaoDich}>
            <div className="course-history-item-head"><div><strong>Giao dịch #{item.maGiaoDich}</strong>
              <span>{item.tenGoiHoc || "Khóa học đã chọn"}</span></div>
              <span className="course-payment-status" data-status={item.trangThai}>{paymentLabels[item.trangThai] || item.trangThai || "Chưa xác định"}</span></div>
            <dl className="course-history-facts"><div><dt>Số tiền</dt><dd>{item.soTien != null && Number.isFinite(Number(item.soTien)) ? money.format(item.soTien) : "Chưa có dữ liệu"}</dd></div>
              <div><dt>Ngày tạo</dt><dd>{date(item.ngayTao) || "Chưa có dữ liệu"}</dd></div></dl>
            {["QUA_HAN", "THAT_BAI"].includes(item.trangThai) && item.courseIds?.length > 0 &&
              <button type="button" className="outline-button course-history-retry" onClick={() => prepareRetry(item.courseIds)}>Chọn lại khóa để thanh toán</button>}
          </article>)}</div> : <p className="course-history-empty">Bạn chưa có giao dịch thanh toán nào.</p>}
        </details></section>}
    </>}
  </main></SiteLayout>;
}
