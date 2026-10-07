import "../payment/PaymentPage.css";
import { useLocation } from "react-router-dom";
import { useEffect, useState } from "react";
import SiteLayout from "../../layouts/SiteLayout";
import { paymentApi } from "../../services/paymentApi";
import { useAuth } from "../../contexts/AuthState";

function PaymentPage() {
    const location = useLocation();
    const payment = location.state;

    const { token } = useAuth();

    const [paymentStatus, setPaymentStatus] =
        useState("CHO_XU_LY");

    // =========================
    // POLLING KIỂM TRA THANH TOÁN
    // =========================
    useEffect(() => {
        if (!payment || !token) return;

        const interval = setInterval(async () => {
            try {
                const data = await paymentApi.getStatus(
                    payment.maGiaoDich,
                    token
                );

                console.log(
                    "Trạng thái thanh toán:",
                    data.trangThai
                );

                setPaymentStatus(data.trangThai);

                // Giao dịch kết thúc
                if (
                    data.trangThai === "THANH_CONG" ||
                    data.trangThai === "THAT_BAI" ||
                    data.trangThai === "HET_HAN"
                ) {
                    clearInterval(interval);
                }

            } catch (error) {
                console.error(
                    "Lỗi kiểm tra trạng thái:",
                    error
                );
            }
        }, 3000);

        // Khi rời khỏi trang → dừng polling
        return () => {
            clearInterval(interval);
        };
    }, [payment, token]);

    // =========================
    // KHÔNG CÓ THÔNG TIN PAYMENT
    // =========================
    if (!payment) {
        return (
            <SiteLayout>
                <div className="payment-page">
                    Không tìm thấy thông tin thanh toán.
                </div>
            </SiteLayout>
        );
    }

    return (
        <SiteLayout>
            <div className="payment-page">
                <div className="payment-card">

                    {/* =========================
                        ĐANG CHỜ THANH TOÁN
                    ========================== */}
                    {paymentStatus === "CHO_XU_LY" && (
                        <>
                            <h2>Thanh toán gói học</h2>

                            <div className="payment-info">
                                <h3>{payment.tenGoiHoc}</h3>

                                <p>
                                    Số tiền:
                                    <strong>
                                        {" "}
                                        {payment.soTien.toLocaleString(
                                            "vi-VN"
                                        )}{" "}
                                        VNĐ
                                    </strong>
                                </p>

                                <p>
                                    Mã giao dịch:
                                    <strong>
                                        {" "}
                                        #{payment.maGiaoDich}
                                    </strong>
                                </p>
                            </div>

                            <div className="qr-section">
                                <img
                                    src={payment.qrCode}
                                    alt="QR thanh toán"
                                    className="qr-code"
                                />

                                <p>
                                    Dùng điện thoại quét mã QR
                                    để thanh toán
                                </p>
                            </div>

                            <div className="payment-status">
                                <span className="status-dot"></span>

                                <span>
                                    Đang chờ thanh toán...
                                </span>
                            </div>
                        </>
                    )}

                    {/* =========================
                        THANH TOÁN THÀNH CÔNG
                    ========================== */}
                    {paymentStatus === "THANH_CONG" && (
                        <div className="payment-success">
                            <h2>
                                🎉 Thanh toán thành công
                            </h2>

                            <p>
                                Gói học của bạn đã được
                                kích hoạt.
                            </p>
                        </div>
                    )}

                    {/* =========================
                        THANH TOÁN THẤT BẠI
                    ========================== */}
                    {paymentStatus === "THAT_BAI" && (
                        <div className="payment-failed">
                            <h2>
                                ❌ Thanh toán thất bại
                            </h2>

                            <p>
                                Giao dịch không thành công.
                            </p>
                        </div>
                    )}

                    {/* =========================
                        GIAO DỊCH HẾT HẠN
                    ========================== */}
                    {paymentStatus === "HET_HAN" && (
                        <div className="payment-expired">
                            <h2>
                                ⏰ Giao dịch đã hết hạn
                            </h2>

                            <p>
                                Mã thanh toán đã hết hạn.
                                Vui lòng tạo giao dịch mới
                                để tiếp tục thanh toán.
                            </p>
                        </div>
                    )}

                </div>
            </div>
        </SiteLayout>
    );
}

export default PaymentPage;