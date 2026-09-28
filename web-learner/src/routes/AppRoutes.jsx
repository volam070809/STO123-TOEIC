import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";

function Placeholder({ title }) {
  return <h1>{title}</h1>;
}

export default function AppRoutes() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Navigate to="/login" replace />} />

        <Route
          path="/login"
          element={<Placeholder title="Đăng nhập" />}
        />

        <Route
          path="/register"
          element={<Placeholder title="Đăng ký" />}
        />

        <Route
          path="/verify-email"
          element={<Placeholder title="Xác thực email" />}
        />

        <Route
          path="/forgot-password"
          element={<Placeholder title="Quên mật khẩu" />}
        />

        <Route
          path="/reset-password"
          element={<Placeholder title="Đặt lại mật khẩu" />}
        />

        <Route
          path="/profile"
          element={<Placeholder title="Hồ sơ" />}
        />

        <Route
          path="/change-password"
          element={<Placeholder title="Đổi mật khẩu" />}
        />

        <Route path="*" element={<Navigate to="/login" replace />} />
      </Routes>
    </BrowserRouter>
  );
}