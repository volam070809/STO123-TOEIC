export function protectedReturnPath(value) {
  if (value === "/courses") return value;
  const coursesPayment = /^\/courses\?paymentId=([1-9]\d{0,9})$/.exec(value ?? "");
  if (coursesPayment && Number.isSafeInteger(Number(coursesPayment[1]))) return coursesPayment[0];
  if (value === "/packages" || value === "/payment/history") return "/courses";
  const match = /^\/courses\/([1-9]\d{0,9})$/.exec(value ?? "");
  if (match && Number.isSafeInteger(Number(match[1]))) return match[0];
  const packageMatch = /^\/packages\/([1-9]\d{0,9})$/.exec(value ?? "");
  if (packageMatch && Number.isSafeInteger(Number(packageMatch[1]))) return "/courses";
  const resultMatch = /^\/payment\/result\?paymentId=([1-9]\d{0,9})$/.exec(value ?? "");
  return resultMatch && Number.isSafeInteger(Number(resultMatch[1])) ?
    `/courses?paymentId=${resultMatch[1]}` : null;
}
