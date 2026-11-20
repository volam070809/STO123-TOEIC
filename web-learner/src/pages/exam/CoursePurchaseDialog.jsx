import { useEffect, useRef } from "react";
import { CourseCover } from "./coursePresentation";

const money = new Intl.NumberFormat("vi-VN", { style: "currency", currency: "VND" });

export default function CoursePurchaseDialog({ course, quote, quoteError, error, busy,
  onClose, onConfirm, onRetry }) {
  const closeRef = useRef(null);
  const busyRef = useRef(busy);
  const closeHandler = useRef(onClose);
  busyRef.current = busy;
  closeHandler.current = onClose;
  const validQuote = quote?.courses?.length === 1 &&
    quote.courses[0].courseId === course.courseId && quote.total === quote.courses[0].price &&
    quote.validDays === 30;

  useEffect(() => {
    const previousFocus = document.activeElement;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    closeRef.current?.focus();
    const onKeyDown = event => {
      if (event.key === "Escape" && !busyRef.current) closeHandler.current();
      if (event.key !== "Tab") return;
      const focusable = [...document.querySelectorAll(".course-modal button:not([disabled])")];
      if (!focusable.length) return;
      const first = focusable[0];
      const last = focusable.at(-1);
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault(); last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault(); first.focus();
      }
    };
    document.addEventListener("keydown", onKeyDown);
    return () => { document.removeEventListener("keydown", onKeyDown);
      document.body.style.overflow = previousOverflow; previousFocus?.focus?.(); };
  }, []);

  return <div className="course-modal-backdrop" onMouseDown={event => {
    if (event.target === event.currentTarget && !busy) onClose();
  }}><section className="course-modal course-purchase-modal" role="dialog" aria-modal="true"
    aria-labelledby="course-purchase-title" aria-describedby="course-purchase-note">
    <div className="course-purchase-head"><div><span className="course-kicker">THANH TOÁN AN TOÀN</span>
      <h2 id="course-purchase-title">{course.accessStatus === "EXPIRED" ? "Gia hạn khóa học" : "Xác nhận mua khóa học"}</h2></div>
      <button ref={closeRef} type="button" className="course-modal-close" aria-label="Đóng xác nhận thanh toán"
        disabled={busy} onClick={onClose}>×</button></div>
    <div className="course-purchase-selected"><CourseCover course={course} /><div><small>KHÓA HỌC ĐÃ CHỌN</small>
      <strong>{course.name}</strong><span>Giai đoạn {course.stage} · Mục tiêu {course.maxTargetScore} điểm</span></div></div>
    <dl className="course-purchase-facts"><div><dt>Giá khóa học</dt><dd>{money.format(validQuote ? quote.courses[0].price : course.retailPrice)}</dd></div>
      <div><dt>Thời hạn truy cập</dt><dd>{validQuote ? quote.validDays : course.accessDays} ngày</dd></div>
      <div><dt>Phương thức thanh toán</dt><dd>VNPay</dd></div></dl>
    <div className="course-purchase-total"><span>Tổng thanh toán</span><strong>{validQuote ? money.format(quote.total) : "Đang xác nhận giá…"}</strong></div>
    <p id="course-purchase-note" className="course-purchase-note">Quyền học được kích hoạt sau khi VNPay xác nhận thanh toán.</p>
    {!quote && !quoteError && <p role="status" className="course-purchase-loading">Đang lấy báo giá từ hệ thống…</p>}
    {quoteError && <div className="course-purchase-retry"><p role="alert" className="exam-error">{quoteError}</p>
      <button type="button" className="outline-button" onClick={onRetry}>Thử lại báo giá</button></div>}
    {quote && !validQuote && <p role="alert" className="exam-error">Báo giá không khớp khóa học đã chọn. Vui lòng đóng và thử lại.</p>}
    {error && <p role="alert" className="exam-error">{error}</p>}
    <div className="course-modal-actions"><button type="button" className="outline-button" disabled={busy} onClick={onClose}>Để sau</button>
      <button type="button" className="primary-button" disabled={!validQuote || busy} onClick={onConfirm}>
        {busy ? "Đang chuyển đến VNPay…" : "Tiếp tục thanh toán"}</button></div>
  </section></div>;
}
