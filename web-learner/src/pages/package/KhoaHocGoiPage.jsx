import { useEffect, useState } from "react";
import { useParams, Link } from "react-router-dom";
import SiteLayout from "../../layouts/SiteLayout";
import { goiHocApi } from "../../services/goiHocApi";

export default function KhoaHocGoiPage() {
    const { id } = useParams();

    const [khoaHoc, setKhoaHoc] = useState(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState("");

    useEffect(() => {
        const token = sessionStorage.getItem("accessToken");

        if (!token) {
            setError("Bạn chưa đăng nhập.");
            setLoading(false);
            return;
        }

        goiHocApi
            .getKhoaHoc(id, token)
            .then((data) => {
                console.log("KHÓA HỌC CỦA GÓI:", data);
                setKhoaHoc(data);
            })
            .catch((e) => {
                console.error("Lỗi tải khóa học:", e);

                setError(
                    e.data?.message ||
                    "Không thể tải thông tin khóa học."
                );
            })
            .finally(() => {
                setLoading(false);
            });
    }, [id]);

    if (loading) {
        return (
            <SiteLayout>
                <div className="site-container">
                    <p>Đang tải khóa học...</p>
                </div>
            </SiteLayout>
        );
    }

    if (error) {
        return (
            <SiteLayout>
                <div className="site-container">
                    <p>{error}</p>
                    <Link to="/goi-hoc">
                        Quay lại gói học
                    </Link>
                </div>
            </SiteLayout>
        );
    }

    if (!khoaHoc) {
        return null;
    }

    return (
        <SiteLayout>
            <div className="site-container">

                <h1>{khoaHoc.tenKhoaHoc}</h1>

                <p>
                    Giai đoạn: {khoaHoc.giaiDoan}
                </p>

                <p>
                    Điểm mục tiêu tối đa:{" "}
                    {khoaHoc.diemMucTieuToiDa}
                </p>

                <p>
                    {khoaHoc.moTa ||
                        "Chưa có mô tả khóa học."}
                </p>

                <hr />

                <h2>Khóa học của bạn</h2>

                <p>
                    Bạn đã mở khóa khóa học này thông qua
                    gói học.
                </p>

                <Link
                    to={`/courses/${khoaHoc.maKhoaHoc}`}
                >
                    Bắt đầu học
                </Link>

            </div>
        </SiteLayout>
    );
}