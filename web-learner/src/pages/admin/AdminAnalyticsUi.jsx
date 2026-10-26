import { useState } from "react";
import { formatDate, formatMoney } from "./adminFormat";
import { buildTrendChartModel } from "./adminChartModel";
import { ADMIN_ANALYTICS_MODE, ADMIN_DATE_FILTER_STATUS, ADMIN_DATE_INVALID_DETAIL, ADMIN_DATE_INVALID_TITLE,
  ADMIN_DATE_MAX, ADMIN_DATE_MIN, adminDateDraftStatus, analyticsMonthPeriod, analyticsYearPeriod,
  applyAdminDateDraft, inferAnalyticsPeriodMode } from "./adminDateRange";

export function DateRangeFilter({ from, to, maxDays, onApply, onReset, onDraftStateChange,
  allowAll = false, appliedMode = null }) {
  const [today] = useState(() => new Date());
  const initial = inferAnalyticsPeriodMode({ from, to }, today, { allowAll, preferredMode: appliedMode });
  const currentYear = today.getFullYear(); const currentMonth = today.getMonth() + 1;
  const yearOptions = Array.from({ length: currentYear - 1753 + 1 }, (_, index) => currentYear - index);
  const [values, setValues] = useState({ from, to });
  const [mode, setMode] = useState(initial.mode);
  const [year, setYear] = useState(initial.year);
  const [month, setMonth] = useState(initial.month);
  const [error, setError] = useState("");
  const appliedValues = { from, to };
  function publishStatus(nextValues, validate = false) {
    const status = adminDateDraftStatus(nextValues, appliedValues, maxDays, validate);
    onDraftStateChange?.(status);
    return status;
  }
  function change(name, value) { const next = { ...values, [name]: value }; setValues(next); setError(""); publishStatus(next); }
  function changeMode(nextMode) {
    setMode(nextMode); setError("");
    const next = nextMode === ADMIN_ANALYTICS_MODE.ALL ? { from: "", to: "" } :
      nextMode === ADMIN_ANALYTICS_MODE.YEAR ? analyticsYearPeriod(year, today) :
      nextMode === ADMIN_ANALYTICS_MODE.MONTH ? analyticsMonthPeriod(year, month, today) : values;
    if (next) { setValues(next); publishStatus(next); }
  }
  function changeYear(value) {
    const nextYear = Number(value); setYear(nextYear); setError("");
    const nextMonth = nextYear === currentYear && month > currentMonth ? currentMonth : month;
    if (nextMonth !== month) setMonth(nextMonth);
    const next = mode === ADMIN_ANALYTICS_MODE.YEAR ? analyticsYearPeriod(nextYear, today) :
      mode === ADMIN_ANALYTICS_MODE.MONTH ? analyticsMonthPeriod(nextYear, nextMonth, today) : null;
    if (next) { setValues(next); publishStatus(next); }
  }
  function changeMonth(value) {
    const nextMonth = Number(value); setMonth(nextMonth); setError("");
    const next = analyticsMonthPeriod(year, nextMonth, today);
    if (next) { setValues(next); publishStatus(next); }
  }
  function submit(event) { event.preventDefault(); const result = applyAdminDateDraft(values, maxDays, next => {
    publishStatus(next); onApply({ ...next, mode }); });
    setError(result.valid ? "" : `${ADMIN_DATE_INVALID_TITLE} ${ADMIN_DATE_INVALID_DETAIL}`);
    if (!result.valid) onDraftStateChange?.(ADMIN_DATE_FILTER_STATUS.INVALID_FILTER); }
  function reset() { const now = new Date(); const resetMode = allowAll ? ADMIN_ANALYTICS_MODE.ALL : ADMIN_ANALYTICS_MODE.YEAR;
    const next = allowAll ? { from: "", to: "" } : analyticsYearPeriod(now.getFullYear(), now);
    setMode(resetMode); setYear(now.getFullYear()); setMonth(now.getMonth() + 1);
    setValues(next); setError(""); publishStatus(next); (onReset || onApply)(next); }
  return <form className="admin-date-filter" onSubmit={submit}>
    <label><span>Chế độ</span><select value={mode} onChange={event => changeMode(event.target.value)}>
      {allowAll && <option value={ADMIN_ANALYTICS_MODE.ALL}>Toàn bộ</option>}<option value={ADMIN_ANALYTICS_MODE.YEAR}>Theo năm</option><option value={ADMIN_ANALYTICS_MODE.MONTH}>Theo tháng</option><option value={ADMIN_ANALYTICS_MODE.CUSTOM}>Tùy chọn</option>
    </select></label>
    {mode !== ADMIN_ANALYTICS_MODE.ALL && mode !== ADMIN_ANALYTICS_MODE.CUSTOM && <label><span>Năm</span><select value={year} onChange={event => changeYear(event.target.value)}>{yearOptions.map(value => <option key={value} value={value}>{value}</option>)}</select></label>}
    {mode === ADMIN_ANALYTICS_MODE.MONTH && <label><span>Tháng</span><select value={month} onChange={event => changeMonth(event.target.value)}>{Array.from({ length: 12 }, (_, index) => index + 1).map(value => <option key={value} value={value} disabled={year === currentYear && value > currentMonth}>Tháng {value}</option>)}</select></label>}
    {mode === ADMIN_ANALYTICS_MODE.CUSTOM && <><label><span>Từ ngày</span><input type="date" min={ADMIN_DATE_MIN} max={ADMIN_DATE_MAX} value={values.from} onChange={event => change("from", event.target.value)}/></label>
      <label><span>Đến ngày</span><input type="date" min={ADMIN_DATE_MIN} max={ADMIN_DATE_MAX} value={values.to} onChange={event => change("to", event.target.value)}/></label></>}
    <div className="admin-filter-actions"><button className="admin-button">Áp dụng</button><button type="button" className="admin-button admin-button-secondary" onClick={reset}>Đặt lại</button></div><small>{mode === ADMIN_ANALYTICS_MODE.ALL ? "Toàn bộ dữ liệu đến hiện tại" : mode === ADMIN_ANALYTICS_MODE.CUSTOM ? `Tối đa ${maxDays} ngày` : `${values.from} – ${values.to}`}</small>
    {error && <p className="admin-date-error" role="alert">{error}</p>}
  </form>;
}

export function Kpi({ label, value, note, href, LinkComponent }) {
  const body = <><span>{label}</span><strong>{value}</strong>{note && <small>{note}</small>}</>;
  return href && LinkComponent ? <LinkComponent className="admin-stat admin-stat-link" to={href}>{body}<em>Xem chi tiết →</em></LinkComponent> : <div className="admin-stat">{body}</div>;
}

export function TrendChart({ title, data, valueKey = "count", money = false, average = false }) {
  const [active, setActive] = useState(null); const model = buildTrendChartModel(data, valueKey, money);
  const monthly = isMonthlyTrend(data);
  const point = active === null ? null : model.points[active];
  return <section className="admin-chart-card"><div className="admin-chart-title"><div><h2>{title}</h2><span>{money ? `Đơn vị: VND · theo ${monthly ? "tháng" : "ngày"}` : average ? `Điểm trung bình theo ${monthly ? "tháng" : "ngày"}` : `Số lượt theo ${monthly ? "tháng" : "ngày"}`}</span></div><strong>{money ? formatMoney(model.total) : average ? `${model.last.toFixed(1)} điểm` : model.total}</strong></div>
    {model.hasData ? <div className="admin-chart-plot" tabIndex="0" aria-label={`${title}. Dùng phím mũi tên để xem từng ${monthly ? "tháng" : "ngày"}.`} onFocus={() => setActive(current => current ?? firstPoint(model.points))} onBlur={() => setActive(null)} onKeyDown={event => { const next = keyboardPoint(model.points, active, event.key); if (next !== null) { event.preventDefault(); setActive(next); } }} onMouseLeave={() => setActive(null)}><svg className="admin-line-chart" viewBox={`0 0 ${model.width} ${model.height}`} role="img" aria-label={title}>
      {model.yTicks.map(tick => <g className="admin-chart-gridline" key={tick.value}><line x1={model.margin.left} y1={tick.y} x2={model.width - model.margin.right} y2={tick.y}/><text x={model.margin.left - 10} y={tick.y + 4}>{axisValue(tick.value, money)}</text></g>)}
      {model.areaPath && <path className="admin-chart-area" d={model.areaPath}/>}<path className="admin-chart-line" d={model.linePath}/>
      {model.xLabels.map(label => <text className="admin-chart-x-label" textAnchor={label.index === 0 ? "start" : label.index === data.length - 1 ? "end" : "middle"} key={label.index} x={label.x} y={model.height - 9}>{trendDate(label.value, monthly)}</text>)}
      {model.points.map((item, index) => item.value === null ? null : <rect className="admin-chart-hit" key={index} x={item.x - Math.max(8, 280 / Math.max(1, data.length))} y={model.margin.top} width={Math.max(16, 560 / Math.max(1, data.length))} height={model.baseline - model.margin.top} onMouseEnter={() => setActive(index)}/>)}
      {point?.value !== null && point && <><line className="admin-chart-guide" x1={point.x} y1={model.margin.top} x2={point.x} y2={model.baseline}/><circle className="admin-chart-active-point" cx={point.x} cy={point.y} r="4"/></>}
    </svg>{point?.value !== null && point && <div className={`admin-chart-tooltip ${point.x > model.width * .72 ? "is-left" : ""}`} style={{ left: `${point.x / model.width * 100}%`, top: `${point.y / model.height * 100}%` }}><span>{monthly ? trendDate(data[active].date, true) : formatDate(data[active].date)}</span><strong>{money ? formatMoney(point.value) : average ? `${point.value.toFixed(1)} điểm` : point.value}</strong></div>}</div> : <p className="admin-chart-empty">Không có dữ liệu trong kỳ.</p>}
  </section>;
}

export function Bars({ title, items, value = item => item.count, label = item => item.name || item.code,
  money = false, href, LinkComponent }) {
  const max = Math.max(...items.map(value), 1); return <section className="admin-chart-card"><div className="admin-chart-title"><h2>{title}</h2></div>{items.length ? <div className="admin-bars">{items.map((item, index) => {
    const content = <><span title={label(item)}>{label(item)}</span><div><i style={{ width: `${value(item) * 100 / max}%` }}/></div><strong>{money ? formatMoney(value(item)) : value(item)}</strong></>;
    return href && LinkComponent ? <LinkComponent className="admin-bar-row admin-bar-link" to={href(item)} key={`${label(item)}-${index}`}>{content}</LinkComponent> : <div className="admin-bar-row" key={`${label(item)}-${index}`}>{content}</div>;
  })}</div> : <p className="admin-chart-empty">Không có dữ liệu trong kỳ.</p>}</section>;
}

export function LoadingAnalytics({ text = "Đang tổng hợp dữ liệu" }) { return <section className="admin-panel admin-loading"><span className="admin-spinner"/><strong>{text}</strong><span>Các chỉ số được tính trực tiếp từ dữ liệu hệ thống.</span></section>; }
function shortDate(value) { const date = new Date(value); return `${date.getDate()}/${date.getMonth() + 1}`; }
function isMonthlyTrend(data) { if (data.length < 2 || data.length > 12) return false; const first = new Date(data[0]?.date); const last = new Date(data.at(-1)?.date); return data.every(item => new Date(item.date).getDate() === 1) && (last.getFullYear() * 12 + last.getMonth()) - (first.getFullYear() * 12 + first.getMonth()) >= data.length - 1; }
function trendDate(value, monthly) { const date = new Date(value); return monthly ? `T${date.getMonth() + 1}/${date.getFullYear()}` : shortDate(value); }
function axisValue(value, money) {
  if (!money) return new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 1 }).format(value);
  if (value >= 1_000_000_000) return `${formatShort(value / 1_000_000_000)} tỷ`;
  if (value >= 1_000_000) return `${formatShort(value / 1_000_000)} triệu`;
  if (value >= 1_000) return `${formatShort(value / 1_000)} nghìn`;
  return formatShort(value);
}
function formatShort(value) { return new Intl.NumberFormat("vi-VN", { maximumFractionDigits: 1 }).format(value); }
function firstPoint(points) { return points.find(point => point.value !== null)?.index ?? null; }
function keyboardPoint(points, active, key) {
  if (key !== "ArrowLeft" && key !== "ArrowRight") return null;
  const direction = key === "ArrowRight" ? 1 : -1;
  for (let index = (active ?? (direction > 0 ? -1 : points.length)) + direction;
    index >= 0 && index < points.length; index += direction) {
    if (points[index].value !== null) return index;
  }
  return active;
}
