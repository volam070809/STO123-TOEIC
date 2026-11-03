import { useCallback, useEffect, useState } from "react";
import { useAuth } from "../../contexts/AuthState";
import { apiRequest } from "../../services/api";
import "../../styles/contact-support.css";

function validLink(value, host) {
  if (!value.trim()) return true;
  try {
    const url = new URL(value.trim());
    return url.protocol === "https:" && url.hostname.toLowerCase() === host && !url.port &&
      !url.username && !url.password && !url.search && !url.hash && /^\/[^/]+$/.test(url.pathname);
  } catch { return false; }
}

export default function AdminContactSettingsPage() {
  const { token } = useAuth();
  const [form, setForm] = useState(null);
  const [state, setState] = useState({ loading: true, saving: false, error: "", notice: "" });
  const load = useCallback(async () => {
    setState({ loading: true, saving: false, error: "", notice: "" });
    try {
      const data = await apiRequest("/api/admin/contact-support", { token });
      setForm(data); setState({ loading: false, saving: false, error: "", notice: "" });
    } catch (error) { setState({ loading: false, saving: false, error: error.message, notice: "" }); }
  }, [token]);
  useEffect(() => {
    let active = true;
    apiRequest("/api/admin/contact-support", { token })
      .then(data => { if (active) { setForm(data); setState({ loading: false, saving: false, error: "", notice: "" }); } })
      .catch(error => { if (active) setState({ loading: false, saving: false, error: error.message, notice: "" }); });
    return () => { active = false; };
  }, [token]);
  function change(key, value) { setForm(current => ({ ...current, [key]: value })); setState(current => ({ ...current, error: "", notice: "" })); }
  async function save(event) {
    event.preventDefault();
    if (!validLink(form.zaloUrl || "", "zalo.me")) { setState(current => ({ ...current, error: "Liên kết Zalo phải dùng HTTPS và đúng địa chỉ zalo.me." })); return; }
    if (!validLink(form.messengerUrl || "", "m.me")) { setState(current => ({ ...current, error: "Liên kết Messenger phải dùng HTTPS và đúng địa chỉ m.me." })); return; }
    if ((form.zaloEnabled && !form.zaloUrl?.trim()) || (form.messengerEnabled && !form.messengerUrl?.trim())) {
      setState(current => ({ ...current, error: "Hãy nhập liên kết trước khi bật hiển thị." })); return;
    }
    setState(current => ({ ...current, saving: true, error: "", notice: "" }));
    try {
      const saved = await apiRequest("/api/admin/contact-support", { method: "PUT", token, body: form });
      setForm(saved); setState({ loading: false, saving: false, error: "", notice: "Đã lưu cấu hình liên hệ hỗ trợ." });
    } catch (error) { setState(current => ({ ...current, saving: false, error: error.message || "Không lưu được cấu hình." })); }
  }
  return <div className="admin-page contact-settings-page"><div className="admin-page-heading"><div><h1>Cài đặt liên hệ hỗ trợ</h1><p>Liên kết hiển thị trong nút liên hệ ở trang học viên.</p></div></div>
    {state.loading && <section className="admin-panel contact-settings-panel" role="status">Đang tải cấu hình…</section>}
    {state.error && <p className="admin-feedback is-error" role="alert">{state.error} <button type="button" onClick={() => {
      if (!form || window.confirm("Tải lại cấu hình đã lưu? Thay đổi chưa lưu sẽ mất.")) load();
    }}>{form ? "Tải lại cấu hình" : "Thử lại"}</button></p>}
    {state.notice && <p className="admin-feedback is-success" role="status">{state.notice}</p>}
    {form && <form className="admin-panel contact-settings-panel" onSubmit={save} noValidate>
      <section><div className="contact-setting-head"><h2>Zalo</h2><label><input type="checkbox" checked={form.zaloEnabled} onChange={event => change("zaloEnabled", event.target.checked)} /> Hiển thị</label></div><label className="contact-setting-field">Liên kết Zalo<input type="url" inputMode="url" value={form.zaloUrl || ""} onChange={event => change("zaloUrl", event.target.value)} placeholder="https://zalo.me/0342463560" /></label></section>
      <section><div className="contact-setting-head"><h2>Messenger</h2><label><input type="checkbox" checked={form.messengerEnabled} onChange={event => change("messengerEnabled", event.target.checked)} /> Hiển thị</label></div><label className="contact-setting-field">Liên kết Messenger<input type="url" inputMode="url" value={form.messengerUrl || ""} onChange={event => change("messengerUrl", event.target.value)} placeholder="https://m.me/lam.vo.561222" /></label></section>
      <div className="contact-setting-actions"><button className="admin-button" type="submit" disabled={state.saving}>{state.saving ? "Đang lưu…" : "Lưu thay đổi"}</button></div>
    </form>}
  </div>;
}
