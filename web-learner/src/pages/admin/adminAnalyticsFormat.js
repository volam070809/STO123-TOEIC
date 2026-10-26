import { formatDate } from "./adminFormat";

export function rangeLabel(range) {
  return range ? `${formatDate(range.from)} – ${formatDate(range.to)} (${range.days} ngày)` : "";
}
