import { readFileSync } from "node:fs";

const routesSource = readFileSync(new URL("../src/routes/AppRoutes.jsx", import.meta.url), "utf8");
const features = JSON.parse(readFileSync(new URL("../src/data/websiteFeatures.json", import.meta.url), "utf8"));
const adminStart = routesSource.indexOf('<Route path="/admin"');
if (adminStart < 0) throw new Error("Không tìm thấy ranh giới route Admin.");
const learnerRoutes = routesSource.slice(0, adminStart);
const routeLines = [...learnerRoutes.matchAll(/<Route path="([^"]+)"[^\r\n]*/g)];
const actualRoutes = new Set(routeLines.map(match => match[1]));
const registeredRoutes = new Set(features.map(feature => feature.route));
const missing = [...actualRoutes].filter(route => !registeredRoutes.has(route));
const obsolete = [...registeredRoutes].filter(route => !actualRoutes.has(route));
if (missing.length || obsolete.length || registeredRoutes.size !== features.length)
  throw new Error(`Bộ đăng ký tính năng lệch route. Thiếu: ${missing.join(", ")}; thừa: ${obsolete.join(", ")}`);

for (const feature of features) {
  const { route, label, roles, workflow, aliases, link } = feature;
  if (!label?.trim() || !workflow?.trim() || !Array.isArray(aliases) ||
      !Array.isArray(roles) || roles.length === 0 ||
      roles.some(role => !["PUBLIC", "HOC_VIEN"].includes(role)) ||
      !registeredRoutes.has(link) || link.includes(":") || route.startsWith("/admin"))
    throw new Error(`Thông tin tính năng không hợp lệ: ${route}`);
  const routeLine = routeLines.find(match => match[1] === route)?.[0] ?? "";
  if (routeLine.includes("element={<ProtectedRoute>") &&
      (roles.length !== 1 || roles[0] !== "HOC_VIEN"))
    throw new Error(`Quyền truy cập tính năng không khớp route: ${route}`);
}
console.log(`Bộ đăng ký tính năng: ${features.length} route hợp lệ.`);
