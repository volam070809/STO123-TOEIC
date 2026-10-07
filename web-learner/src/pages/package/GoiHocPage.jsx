import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import { goiHocApi } from "../../services/goiHocApi";
import "../../pages/package/goiHoc.css";

export default function GoiHocPage() {
  const [goiHocs, setGoiHocs] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    let live = true;

    // Lấy JWT từ sessionStorage
    const token = sessionStorage.getItem("accessToken");

    console.log("TOKEN GOI HOC:", token);

    // Chưa đăng nhập
    if (!token) {
      setError(
        "Bạn chưa đăng nhập hoặc phiên đăng nhập đã hết hạn."
      );
      setLoading(false);
      return;
    }

    // Gọi API lấy danh sách gói học
    goiHocApi
      .getAll(token)
      .then((data) => {
        if (live) {
          console.log("DANH SACH GOI HOC:", data);
          setGoiHocs(data);
        }
      })
      .catch((e) => {
        console.error("Lỗi tải gói học:", e);

        if (live) {
          setError(
            e.data?.message ||
              "Không thể tải danh sách gói học."
          );
        }
      })
      .finally(() => {
        if (live) {
          setLoading(false);
        }
      });

    return () => {
      live = false;
    };
  }, []);

  // Tìm gói hiện đang sử dụng
  const goiDangDung = goiHocs.find(
    (goiHoc) => goiHoc.dangSuDung === true
  );

  return (
    <SiteLayout>
      <div className="site-container package-page">

        {/* ================= HEADER ================= */}
        <div className="package-header">
          <h1>Gói học & thanh toán</h1>

          <p>
            Chọn gói phù hợp với mục tiêu và quyền truy cập
            của bạn.
          </p>
        </div>

        {/* ================= ERROR ================= */}
        {error && (
          <div className="package-error" role="alert">
            {error}
          </div>
        )}

        {/* ================= LOADING ================= */}
        {loading && !error && (
          <div className="package-loading">
            Đang tải danh sách gói học...
          </div>
        )}

        {/* ================= CONTENT ================= */}
        {!loading && !error && (
          <>
            {/* =========================================
                GÓI ĐANG SỬ DỤNG
            ========================================= */}
            {goiDangDung && (
              <section className="current-package">

                <div className="current-package-info">
                  <span className="current-package-badge">
                    ĐANG DÙNG
                  </span>

                  <h2>
                    {goiDangDung.tenGoiHoc}
                  </h2>
                </div>

                <div className="current-package-progress">

                  <div className="current-package-days">
                    Còn{" "}
                    <strong>
                      {goiDangDung.soNgayConLai ??
                        goiDangDung.soNgaySuDung}{" "}
                      ngày
                    </strong>
                  </div>

                  <div className="progress-bar">
                    <div
                      className="progress-value"
                      style={{
                        width: `${
                          goiDangDung.phanTramHoanThanh ??
                          0
                        }%`,
                      }}
                    />
                  </div>

                  <span className="progress-text">
                    {goiDangDung.phanTramHoanThanh ?? 0}
                    % nội dung đã hoàn thành
                  </span>

                </div>

                <Link
                  to={`/goi-hoc/${goiDangDung.maGoiHoc}`}
                  className="current-package-button"
                >
                  Xem tiến độ
                </Link>

              </section>
            )}

            {/* =========================================
                DANH SÁCH GÓI HỌC
            ========================================= */}
            <section className="package-list-section">

              <h2>Chọn gói học</h2>

              {goiHocs.length === 0 ? (
                <div className="package-empty">
                  Hiện chưa có gói học nào.
                </div>
              ) : (
                <div className="package-grid">

                  {goiHocs.map((goiHoc) => {

                    // Gói đang sử dụng
                    const dangSuDung =
                      goiHoc.dangSuDung === true;

                    return (
                      <article
                        key={goiHoc.maGoiHoc}
                        className={`package-card ${
                          dangSuDung
                            ? "package-card-active"
                            : ""
                        }`}
                      >

                        {/* ===== CARD HEADER ===== */}
                        <div className="package-card-top">

                          <span className="package-type">
                            {goiHoc.capDo || "Nền tảng"}
                          </span>

                          {dangSuDung && (
                            <span className="package-active-badge">
                              Đang sử dụng
                            </span>
                          )}

                        </div>

                        {/* ===== CARD BODY ===== */}
                        <div className="package-card-body">

                          <h3>
                            {goiHoc.tenGoiHoc}
                          </h3>

                          <div className="package-price">
                            {goiHoc.gia?.toLocaleString(
                              "vi-VN"
                            )}
                            <span>đ</span>
                          </div>

                          <p className="package-duration">
                            {goiHoc.soNgaySuDung} ngày học
                          </p>

                          <p className="package-description">
                            {goiHoc.moTa ||
                              "Xây nền tảng kiến thức và kỹ năng TOEIC."}
                          </p>

                          {/* =================================
                              NÚT HÀNH ĐỘNG
                              - Đang dùng → Vào học
                              - Không đang dùng → Chọn gói
                          ================================= */}
                          {dangSuDung ? (
                            <Link
                                to={`/khoa-hoc-goi/${goiHoc.maGoiHoc}`}
                                className="package-action package-action-learning"
                            >
                                Vào học
                            </Link>
                          ) : (
                            <Link
                              to={`/goi-hoc/${goiHoc.maGoiHoc}`}
                              className="package-action package-action-choose"
                            >
                              Chọn gói
                            </Link>
                          )}

                        </div>

                      </article>
                    );
                  })}

                </div>
              )}

            </section>

            {/* =========================================
                LỊCH SỬ THANH TOÁN
            ========================================= */}
            <section className="payment-history">

              <p>
                Lịch sử giao dịch và hóa đơn điện tử
                được lưu trong tài khoản của bạn.
              </p>

              <Link to="/lich-su-giao-dich">
                Xem lịch sử →
              </Link>

            </section>

          </>
        )}

      </div>
    </SiteLayout>
  );
}