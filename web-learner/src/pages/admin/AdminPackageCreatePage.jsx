import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../contexts/AuthState";
import { adminPackageApi } from "../../services/adminPackageApi";
import AdminPackageForm from "./AdminPackageForm";

export default function AdminPackageCreatePage(){const{token}=useAuth();const navigate=useNavigate();const[state,setState]=useState({submitting:false,error:"",fieldErrors:{}});async function submit(values){setState({submitting:true,error:"",fieldErrors:{}});try{const item=await adminPackageApi.create(values,token);navigate(`/admin/packages/${item.packageId}`,{replace:true,state:{notice:"Đã tạo gói học thành công."}});}catch(error){setState({submitting:false,error:error.data?.message||"Không thể tạo gói học.",fieldErrors:error.data?.errors||{}});}}return <div className="admin-page admin-form-page"><Link className="admin-back-link" to="/admin/packages">← Danh sách gói học</Link><div className="admin-page-heading"><div><p className="admin-eyebrow">Gói thương mại mới</p><h1>Thêm gói học</h1><p>Thiết lập giá, thời hạn và các khóa học được cung cấp cùng nhau.</p></div></div><section className="admin-panel admin-form-panel"><AdminPackageForm mode="create" submitting={state.submitting} serverError={state.error} serverErrors={state.fieldErrors} onSubmit={submit}/></section></div>;}
