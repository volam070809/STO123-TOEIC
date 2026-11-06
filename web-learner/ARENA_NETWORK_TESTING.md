# Arena qua hai mạng Internet

Chạy từ thư mục gốc STO123. Dùng một backend và một frontend chung cho cả hai người chơi.
Giữ User Secrets và chuỗi kết nối Azure SQL chỉ ở máy chạy backend.

```powershell
# Cửa sổ 1: backend HTTPS địa phương
dotnet run --project .\STO123\STO123.csproj --launch-profile https

# Cửa sổ 2: lấy URL HTTPS công khai trước khi khởi động Vite
ngrok http 5173

# Cửa sổ 3: thay URL bằng địa chỉ HTTPS ngrok vừa cấp
$env:STO123_PUBLIC_URL='https://YOUR-SUBDOMAIN.ngrok-free.app'
$env:STO123_BACKEND_URL='https://localhost:7232'
$env:VITE_API_BASE_URL=''
$env:VITE_SIGNALR_BASE_URL=''
cd .\web-learner
npm run dev
```

Cả hai học viên mở **cùng** `$env:STO123_PUBLIC_URL` trên hai thiết bị/mạng khác nhau,
đăng nhập bằng hai tài khoản riêng, chọn cùng Part rồi vào `/arena`.
Đăng nhập có mật khẩu hoạt động ngay. Nếu dùng Google, phải thêm URL ngrok vào
danh sách JavaScript origins được phép của Google OAuth client.

Vite chuyển `/api` và WebSocket `/hubs` về backend qua proxy. Không đặt bí mật
Azure SQL, JWT hoặc ngrok token trong `VITE_*` hay mã nguồn. Khi triển khai tách
frontend/backend, đặt `VITE_API_BASE_URL` và `VITE_SIGNALR_BASE_URL` thành URL HTTPS
backend; đặt `Cors__AllowedOrigins__0` trên backend thành URL frontend cụ thể.
Nếu triển khai nhiều backend, cần cơ chế chia sẻ kết nối SignalR và presence;
cấu hình hiện tại dành cho một backend.

VNPay cần `VNPay:ReturnUrl` và `VNPay:FrontendResultUrl` công khai phù hợp với môi
trường triển khai. Thay đổi các URL đó trong cấu hình backend, không sửa mã nghiệp vụ.
