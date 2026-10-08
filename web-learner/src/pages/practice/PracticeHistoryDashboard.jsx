const number = new Intl.NumberFormat("vi-VN");
const dateTime = new Intl.DateTimeFormat("vi-VN", {
  day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit"
});

function formatPracticeDate(value) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "Không rõ ngày" : dateTime.format(date);
}

function Status({ value }) {
  const label = value === "DA_NOP" ? "Hoàn thành" : value || "Không rõ";
  return <span className="practice-history-status">{label}</span>;
}

function Accuracy({ value }) {
  const percent = Math.min(100, Math.max(0, Number(value) || 0));
  return <span className="practice-history-accuracy"><strong>{number.format(percent)}%</strong>
    <span className="practice-history-meter" aria-hidden="true"><span style={{ width: `${percent}%` }} /></span>
  </span>;
}

export default function PracticeHistoryDashboard({ history, loading, error, partFilter, sort, page,
  onPartChange, onSortChange, onPageChange, onReview, onRetry }) {
  const summary = history?.summary;
  const items = history?.items || [];
  const pageCount = Math.max(1, Math.ceil((history?.total || 0) / (history?.pageSize || 8)));
  return <section className="vocab-panel practice-history-dashboard" aria-labelledby="practice-history-title">
    <div className="practice-history-heading"><div><h2 id="practice-history-title">Lịch sử &amp; thống kê</h2>
      <p>Theo dõi các bài luyện đã hoàn thành và mở lại bài gốc khi cần.</p></div></div>
    <div className="practice-history-summary" aria-label="Thống kê tất cả bài luyện đã hoàn thành">
      <div className="practice-history-stat"><span>Bài đã hoàn thành</span><strong>{summary ? number.format(summary.totalCompleted) : "—"}</strong></div>
      <div className="practice-history-stat"><span>Tỷ lệ trả lời đúng</span><strong>{summary ? `${number.format(summary.overallAccuracy)}%` : "—"}</strong>
        <small>Số câu đúng / tổng số câu trong mọi bài đã hoàn thành</small></div>
      <div className="practice-history-stat"><span>Câu đã trả lời</span><strong>{summary ? number.format(summary.totalAnswered) : "—"}</strong>
        <small>Đếm theo lượt xuất hiện của câu hỏi</small></div>
    </div>
    <div className="practice-history-toolbar"><fieldset className="practice-history-parts"><legend>Lọc theo Part</legend>
      {[0, 1, 2, 3, 4, 5, 6, 7].map(value => <button type="button" key={value}
        className={partFilter === value ? "is-selected" : undefined} aria-pressed={partFilter === value}
        onClick={() => onPartChange(value)}>{value === 0 ? "Tất cả" : `P${value}`}</button>)}</fieldset>
      <label className="practice-history-sort">Sắp xếp
        <select value={sort} onChange={event => onSortChange(event.target.value)}>
          <option value="NEWEST">Mới nhất</option><option value="OLDEST">Cũ nhất</option>
        </select></label></div>
    {error ? <div className="practice-history-message" role="alert">Không thể tải lịch sử luyện tập.
      <button type="button" className="outline-button" onClick={onRetry}>Thử lại</button></div> :
      loading || !history ? <p className="practice-history-message" role="status">Đang tải lịch sử luyện tập…</p> :
      items.length === 0 ? <div className="practice-history-message"><p>{partFilter ? `Chưa có bài Part ${partFilter} đã hoàn thành.` : "Chưa có bài luyện nào đã hoàn thành."}</p>
        {partFilter !== 0 && <button type="button" className="outline-button" onClick={() => onPartChange(0)}>Xem tất cả Part</button>}</div> : <>
        <div className="practice-history-table-wrap"><table className="practice-history-table">
          <caption className="sr-only">Các bài luyện tập đã hoàn thành</caption>
          <thead><tr><th scope="col">Ngày hoàn thành</th><th scope="col">Part</th>
            <th scope="col">Đúng / Tổng</th><th scope="col">Đã trả lời / Tổng</th>
            <th scope="col">Tỷ lệ đúng</th><th scope="col">Trạng thái</th><th scope="col">Bài làm</th></tr></thead>
          <tbody>{items.map(row => <tr key={row.attemptId}>
            <td><time dateTime={row.finishedAt}>{formatPracticeDate(row.finishedAt)}</time></td>
            <td><span className="practice-history-part">Part {row.part}</span></td>
            <td><strong>{row.correct}/{row.total}</strong> câu</td>
            <td>{row.attempted}/{row.total} câu</td>
            <td><Accuracy value={row.accuracy} /></td>
            <td><Status value={row.status} /></td>
            <td><button type="button" className="practice-history-review" onClick={() => onReview(row.attemptId)}>Xem lại</button></td>
          </tr>)}</tbody></table></div>
        <div className="practice-history-mobile">{items.map(row => <article className="practice-history-card" key={row.attemptId}>
          <div className="practice-history-card-top"><span className="practice-history-part">Part {row.part}</span>
            <time dateTime={row.finishedAt}>{formatPracticeDate(row.finishedAt)}</time></div>
          <div className="practice-history-card-metrics"><span><strong>{row.correct}/{row.total}</strong> đúng</span>
            <span><strong>{row.attempted}/{row.total}</strong> đã trả lời</span></div>
          <div className="practice-history-card-bottom"><Accuracy value={row.accuracy} /><Status value={row.status} />
            <button type="button" className="practice-history-review" onClick={() => onReview(row.attemptId)}>Xem lại →</button></div>
        </article>)}</div>
        <nav className="practice-history-pagination" aria-label="Phân trang lịch sử">
          <span>{number.format(history.total)} bài · Trang {page}/{pageCount}</span>
          <div><button type="button" className="outline-button" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>← Trước</button>
            <button type="button" className="outline-button" disabled={page >= pageCount} onClick={() => onPageChange(page + 1)}>Sau →</button></div>
        </nav>
      </>}
  </section>;
}
