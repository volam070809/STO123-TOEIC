# Figma audit — STO123 learner milestone

Source: https://www.figma.com/design/NRf75PHAZmmyBLJrquYVIV/TOEIC_KNN_STO123-%E2%80%94-Giao-di%E1%BB%87n-nh%C3%B3m-3-th%C3%A0nh-vi%C3%AAn?node-id=0-1&m=dev

The connected file has one page, 0:1, named “STO123 | 3 thành viên”. The relevant frames are in section 1:506, “STO123 · 02 · NGƯỜI 2 — WEB NGƯỜI HỌC · KHÁCH + NGƯỜI DÙNG”. The frame names in the implementation request do not occur in this file's node hierarchy. The table records actual inspected frames, not guessed aliases. All listed page frames are 1440 × 1200.

| Actual frame | Node ID | Purpose | Deep link |
| --- | --- | --- | --- |
| STO123 / Header / Web | 1:50 | Shared desktop navbar, 1440 × 80 | [Open](https://www.figma.com/design/NRf75PHAZmmyBLJrquYVIV/TOEIC_KNN_STO123?node-id=1-50) |
| Web / Khách vãng lai — Trang chủ | 282:1955 | Public home | [Open](https://www.figma.com/design/NRf75PHAZmmyBLJrquYVIV/TOEIC_KNN_STO123?node-id=282-1955) |
| Web / Đăng nhập & đăng ký | 154:1613 | Login card and Google entry | [Open](https://www.figma.com/design/NRf75PHAZmmyBLJrquYVIV/TOEIC_KNN_STO123?node-id=154-1613) |
| Web / Tài khoản người học | 63:596 | Profile/account overview | [Open](https://www.figma.com/design/NRf75PHAZmmyBLJrquYVIV/TOEIC_KNN_STO123?node-id=63-596) |
| Web / Khách vãng lai — Chọn chủ đề | 282:1991 | Guest vocabulary topic choice | [Open](https://www.figma.com/design/NRf75PHAZmmyBLJrquYVIV/TOEIC_KNN_STO123?node-id=282-1991) |
| Web / Khách vãng lai — Chi tiết danh sách từ | 154:1505 | Guest list detail | [Open](https://www.figma.com/design/NRf75PHAZmmyBLJrquYVIV/TOEIC_KNN_STO123?node-id=154-1505) |
| Web / Khách vãng lai — Flashcard từ vựng | 282:2027 | Flashcard front | [Open](https://www.figma.com/design/NRf75PHAZmmyBLJrquYVIV/TOEIC_KNN_STO123?node-id=282-2027) |
| Web / Khách vãng lai — Flashcard từ vựng — Mặt sau | 287:2087 | Flashcard back | [Open](https://www.figma.com/design/NRf75PHAZmmyBLJrquYVIV/TOEIC_KNN_STO123?node-id=287-2087) |
| Web / Khách vãng lai — Kết quả học thử | 282:2063 | Guest result | [Open](https://www.figma.com/design/NRf75PHAZmmyBLJrquYVIV/TOEIC_KNN_STO123?node-id=282-2063) |

## Requested frame names not present

NavbarSTO123 1; 02_01_DangNhap_DangKy; 02_02_QuenMatKhau; 02_03_1_OTP; 02_03_2_CapNhatMatKhau; 10_01_ThongTinCaNhan v2; 10_02_CapNhatThongTinCaNhan v2; 10_03_DoiMatKhau v2; 09_02_01_MTU v2; 09_02_02_KTV v2; 09_02_03_PNVTU v2; 09_02_03_PNVTU - Sol v2.

These names were searched in the complete metadata for page 0:1. The inspected frames above are functional counterparts where they exist; they are not claimed to be the missing named versions. There are no separate OTP, forgot/reset password, update profile, or change password frames in this file. No duplicate versions of the listed guest frames were found under the learner section. The public guest home was selected over “Web / Trang chủ” because it explicitly shows guest access and vocabulary entry.

## Inspected visual system

- FONT STATUS: RESOLVED. Figma design context identifies Inter Regular, Semi Bold, and Bold. The relevant screens use 400, 600, and 700. Navbar links are 14 px, logo text 29 px, home and vocabulary page titles 32 px, login title 28 px, flashcard term 42 px, body text commonly 14–16 px, and small/support text 12–13 px. Most text uses line height 1.4 in generated context; some headings use normal line height. Letter spacing was not explicitly specified in the inspected context.
- Colors from inspected design context and shared palette: brand #35539B; dark brand #203B78; brand light #EDF2FF; primary ink #202837; heading ink on guest screens #141F33; muted #667085; supporting muted #61708A; border #E2E7EF; page background #F7F9FC (login #F6F8FC); white #FFFFFF; orange #F2A348; green #24785B; green light #EAF7F0; purple #7658A5; danger #C84646. Some guest frames use action blue #3857A3, action green #298563, pale blue #E8F0FF, pale green #E5F7F0, and orange accent #F58C29.
- Desktop canvas 1440 × 1200. Shared navbar height 80 px, footer height 250 px. Main content inset 60 px from canvas edge, giving 1320 px width. Navbar logo 175 × 42 px. Login card 600 × 610 px at x=420/y=170 with 60 px inner horizontal padding; input width 480 px and height 48 px; primary/outline button height 44 px.
- Typical navbar item gap 18 px. Home hero uses 1320 × 230 px panel. Home feature cards are 408 × 250 px with 32 px horizontal gaps. Flashcard main/aside panels are 860 × 450 px and 420 × 450 px. Buttons have 8 px radius; auth card 16 px; content cards commonly 12–16 px; avatar 36 px circle. No shadow was identified in the inspected generated context.
- Login design shows one input each for email and password. The file does not specify OTP cell shape or size. No separate form designs exist for password recovery or change password.
- Shared logo is a component with a 34 × 34 px blue square containing “T”, plus “STO” and “123” text. Inspected frames did not provide independent raster illustration or image assets. Reuse this visual construction in code. Footer is shared component 1:91.
- Navbar in this file shows Trang chủ, Lộ trình, Bài giảng, Từ vựng, Luyện tập, Thi thử and an avatar/name. The request lists a different future label set. The inspected Figma file is the visual source; future destinations remain inactive for this milestone.
- The login screenshot visibly includes a Google sign-in control. Guest vocabulary flow shows: public home → topic choice → list detail → flashcard front/back → result. Front says reveal meaning/example after flipping. Back shows “Chưa nhớ” and “Đã nhớ”. Result shows remembered count and a retry action. The result's example duration and score are design sample values, not application data.

## Implementation decisions and limits

[RESPONSIVE ADAPTATION] The inspected web frames are desktop 1440 px wide. Mobile layouts will stack content and collapse the navbar, while preserving desktop dimensions and spacing as closely as practical.

[ACCESSIBILITY ADAPTATION] Inactive future navbar labels will be non-interactive text; form controls and card actions will use semantic buttons/links and keyboard focus styles.

FIGMA DEVIATION: The available file lacks separate OTP, forgot/reset, update profile, and change password designs. Those screens can share the inspected auth typography, colors, form dimensions, and card treatment, but exact frame matching is unavailable.

FIGMA DEVIATION: The profile frame contains target score, progress, planned exam date, and edit action without matching backend data/update API. The implementation will display only data actually returned by GET /api/auth/me and will not fabricate persistence or scores.

VISUAL QA: visually verified for rendered public home, login, vocabulary topic/list, flashcard front/back and result against the inspected Figma screenshots. Chrome device emulation covered 1440, 1280, 768, 390 and 375 CSS px; inspected screens had no horizontal overflow. The requested named v2 frames and live Google button remained unavailable, so no pixel-perfect or full-screen coverage is claimed.
