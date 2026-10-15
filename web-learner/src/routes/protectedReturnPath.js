export function protectedReturnPath(value) {
  if (value === "/courses") return value;
  const match = /^\/courses\/([1-9]\d{0,9})$/.exec(value ?? "");
  return match && Number.isSafeInteger(Number(match[1])) ? match[0] : null;
}
