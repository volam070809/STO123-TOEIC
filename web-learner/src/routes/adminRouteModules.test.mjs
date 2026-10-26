import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { routeKey } from "./adminRouteModules.js";

test("admin navigation preloads only the module matching the destination", () => {
  assert.equal(routeKey("/admin"), "dashboard");
  assert.equal(routeKey("/admin/learners/42"), "learnerDetail");
  assert.equal(routeKey("/admin/courses/7/edit"), "courseEdit");
  assert.equal(routeKey("/admin/reports?type=payments"), "reports");
  assert.equal(routeKey("/courses"), null);
});

test("packages are hidden from Admin navigation while package management routes remain available", async () => {
  const [packagesPage, routes, layout] = await Promise.all([
    readFile(new URL("../pages/admin/AdminPackagesPage.jsx", import.meta.url), "utf8"),
    readFile(new URL("./AppRoutes.jsx", import.meta.url), "utf8"),
    readFile(new URL("../layouts/AdminLayout.jsx", import.meta.url), "utf8"),
  ]);

  assert.match(packagesPage, /to="\/admin\/packages\/new"/);
  assert.match(packagesPage, /Thêm gói học/);
  assert.match(routes, /path="packages\/new"/);
  assert.match(routes, /<AdminPackageCreatePage \/>/);
  assert.doesNotMatch(layout, /\["\/admin\/packages", "Gói học", "packages"\]/);
  assert.match(layout, /\["\/admin\/learners", "Học viên", "learners"\][\s\S]*\["\/admin\/enrollments", "Đăng ký", "enrollments"\]/);
  assert.equal(routeKey("/admin/packages/new"), "packageCreate");
});
