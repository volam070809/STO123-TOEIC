import { useEffect, useState } from "react";
import { useParams, useNavigate } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import { goiHocApi } from "../../services/goiHocApi";
import { paymentApi } from "../../services/paymentApi";
import { useAuth } from "../../contexts/AuthState";
import "./goiHocDetail.css";

export default function GoiHocDetailPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { token } = useAuth();

  const [goiHoc, setGoiHoc] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const [paymentLoading, setPaymentLoading] = useState(false);
  const [paymentError, setPaymentError] = useState("");

  useEffect(() => {
    goiHocApi
      .getById(id, token)
      .then((data) => {
        setGoiHoc(data);
      })
      .catch((err) => {
        console.error("Lỗi lấy chi tiết gói học:", err);
        setError(
          err.message || "Không thể tải thông tin gói học."
        );
      })
      .finally(() => {
        setLoading(false);
      });
  }, [id, token]);

  const handlePayment = async () => {
    try {
      setPaymentLoading(true);
      setPaymentError("");

      const data = await paymentApi.create(
        goiHoc.maGoiHoc,
        token
      );

      navigate("/payment", {
        state: data,
      });
    } catch (err) {
      console.error("Lỗi tạo thanh toán:", err);

      setPaymentError(
        err.message ||
          "Không thể tạo giao dịch thanh toán."
      );
    } finally {
      setPaymentLoading(false);
    }
  };

  return (
    <SiteLayout>
      <div className="site-container package-detail-page">

        {/* HEADER */}
        <div className="package-detail-heading">
          <div>
            <span className="package-detail-breadcrumb">
              Gói học / Chi tiết
            </span>

            <h1>Chi tiết gói học</h1>

            <p>
              Thông tin gói học và quyền truy cập khóa học
              của bạn.
            </p>
          </div>

          <button
            type="button"
            className="package-back-button"
            onClick={() => navigate("/goi-hoc")}
          >
            ← Quay lại
          </button>
        </div>

        {/* LOADING */}
        {loading && (
          <div className="package-detail-loading">
            <div className="loading-spinner" />
            <p>Đang tải thông tin gói học...</p>
          </div>
        )}

        {/* ERROR */}
        {error && (
          <div className="package-detail-error" role="alert">
            <strong>Không thể tải gói học</strong>
            <p>{error}</p>

            <button
              type="button"
              onClick={() => navigate("/goi-hoc")}
            >
              Quay lại danh sách gói
            </button>
          </div>
        )}

        {/* DETAIL */}
        {!loading && !error && goiHoc && (
          <div className="package-detail-layout">

            {/* LEFT */}
            <section className="package-detail-main">

              <div className="package-detail-card">

                <div className="package-detail-card-header">
                  <div>
                    <span className="package-detail-label">
                      GÓI HỌC TOEIC
                    </span>

                    <h2>{goiHoc.tenGoiHoc}</h2>
                  </div>

                  {goiHoc.dangSuDung ? (
                    <span className="package-status active">
                      Đang sử dụng
                    </span>
                  ) : (
                    <span className="package-status">
                      Chưa sử dụng
                    </span>
                  )}
                </div>

                {/* PRICE */}
                <div className="package-detail-price">
                  <span className="price-label">
                    Học phí
                  </span>

                  <strong>
                    {goiHoc.gia?.toLocaleString("vi-VN")}
                    <small> VNĐ</small>
                  </strong>
                </div>

                {/* INFO */}
                <div className="package-info-grid">

                  <div className="package-info-item">
                    <span>Thời hạn</span>
                    <strong>
                      {goiHoc.soNgaySuDung} ngày
                    </strong>
                  </div>

                  <div className="package-info-item">
                    <span>Trạng thái</span>
                    <strong>
                      {goiHoc.dangSuDung
                        ? "Đang học"
                        : "Có thể đăng ký"}
                    </strong>
                  </div>

                  <div className="package-info-item">
                    <span>Ngày tạo</span>
                    <strong>
                      {goiHoc.ngayTao
                        ? new Date(
                            goiHoc.ngayTao
                          ).toLocaleDateString("vi-VN")
                        : "--"}
                    </strong>
                  </div>

                  <div className="package-info-item">
                    <span>Tình trạng mua</span>
                    <strong>
                      {goiHoc.daMua
                        ? "Đã từng mua"
                        : "Chưa mua"}
                    </strong>
                  </div>

                </div>

                {/* DESCRIPTION */}
                <div className="package-description-box">
                  <h3>Giới thiệu gói học</h3>

                  <p>
                    {goiHoc.moTa ||
                      "Gói học cung cấp quyền truy cập nội dung TOEIC phù hợp với mục tiêu học tập của bạn."}
                  </p>
                </div>

                {/* PAYMENT */}
                <div className="package-payment-area">

                  {goiHoc.dangSuDung ? (
                    <>
                      <div className="package-active-message">
                        <span>✓</span>

                        <div>
                          <strong>
                            Bạn đang sử dụng gói học này
                          </strong>

                          {goiHoc.ngayKetThuc && (
                            <p>
                              Có hiệu lực đến{" "}
                              {new Date(
                                goiHoc.ngayKetThuc
                              ).toLocaleDateString("vi-VN")}
                            </p>
                          )}
                        </div>
                      </div>

                      <button
                        type="button"
                        className="package-payment-button disabled"
                        disabled
                      >
                        Đang sử dụng
                      </button>
                    </>
                  ) : (
                    <>
                      <div className="package-payment-title">
                        <div>
                          <strong>
                            {goiHoc.daMua
                              ? "Tiếp tục sử dụng gói học"
                              : "Sẵn sàng bắt đầu học?"}
                          </strong>

                          <p>
                            {goiHoc.daMua
                              ? "Thanh toán để kích hoạt lại gói học."
                              : "Hoàn tất thanh toán để bắt đầu khóa học."}
                          </p>
                        </div>

                        <strong className="payment-total">
                          {goiHoc.gia?.toLocaleString("vi-VN")} VNĐ
                        </strong>
                      </div>

                      <button
                        type="button"
                        className="package-payment-button"
                        onClick={handlePayment}
                        disabled={paymentLoading}
                      >
                        {paymentLoading
                          ? "Đang tạo thanh toán..."
                          : goiHoc.daMua
                            ? "Mua lại gói học"
                            : "Mua ngay"}
                      </button>
                    </>
                  )}

                  {paymentError && (
                    <div
                      className="package-payment-error"
                      role="alert"
                    >
                      {paymentError}
                    </div>
                  )}

                </div>

              </div>

            </section>

            {/* RIGHT */}
            <aside className="package-detail-sidebar">

              <div className="package-benefit-card">

                <h3>Quyền lợi gói học</h3>

                <ul>
                  <li>
                    <span>✓</span>
                    Truy cập nội dung khóa học
                  </li>

                  <li>
                    <span>✓</span>
                    Học theo lộ trình TOEIC
                  </li>

                  <li>
                    <span>✓</span>
                    Luyện tập và kiểm tra kiến thức
                  </li>

                  <li>
                    <span>✓</span>
                    Theo dõi tiến độ học tập
                  </li>
                </ul>

              </div>

              <div className="package-support-card">
                <span className="support-icon">?</span>

                <div>
                  <strong>Cần hỗ trợ?</strong>

                  <p>
                    Nếu gặp vấn đề trong quá trình
                    thanh toán, hãy liên hệ quản trị viên.
                  </p>
                </div>
              </div>

            </aside>

          </div>
        )}

      </div>
    </SiteLayout>
  );
}