export function validPassword(password) {
  return password.length >= 8 && /[\p{L}]/u.test(password) && /\d/.test(password);
}
