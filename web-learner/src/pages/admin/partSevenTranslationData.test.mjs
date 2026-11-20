import test from "node:test";
import assert from "node:assert/strict";
import { alignTranslation, parsePart7Translation, serializePart7Translation, updateTranslationStructure,
  translationPrefix } from "./partSevenTranslationData.js";
import { newDocument } from "./partSevenDocumentData.js";

const originals = [
  { type: "TABLE", title: "Prices", headers: ["Item", "Price"], rows: [["Tea", "$2"], ["Cake", "$4"]] },
  { type: "EMAIL", from: "a@example.com", to: "b@example.com", date: "Today", subject: "Sale", body: "Hello" },
  { type: "CHAT", messages: [{ sender: "A", time: "09:00", text: "Hello" },
    { sender: "B", time: "09:01", text: "Thanks" }] },
  { type: "FORM", title: "Order", fields: [{ label: "Name", value: "Lan" }] },
  { type: "TEXT", content: "Line 1\nLine 2" },
  { type: "IMAGE", imagePath: "part7/photo.jpg" }
];

test("Part 7 translations retain each document's structure and order", () => {
  const state = parsePart7Translation("Bản dịch cũ", originals);
  state.documents[0].content.headers = ["Mặt hàng", "Giá"];
  state.documents[0].content.rows = [["Trà", "2 đô"], ["Bánh", "4 đô"]];
  state.documents[1].content.subject = "Giảm giá";
  state.documents[1].content.body = "Xin chào";
  state.documents[2].content.messages[0].text = "Xin chào";
  state.documents[2].content.messages[1].text = "Cảm ơn";
  state.documents[3].content.fields[0] = { label: "Tên", value: "Lan" };
  state.documents[4].content = "Dòng 1\nDòng 2";
  state.documents[5].content = "Ảnh minh họa";
  const stored = serializePart7Translation(state, originals);
  assert.ok(stored.startsWith(translationPrefix));
  const loaded = parsePart7Translation(stored, originals);
  assert.deepEqual(loaded, state);
  assert.equal(loaded.legacyText, "Bản dịch cũ");
  assert.equal(loaded.documents[0].content.rows[1][1], "4 đô");
});

test("plain text remains byte for byte and translated table tracks source columns", () => {
  const legacy = "Dòng cũ\nDòng hai";
  assert.equal(serializePart7Translation(parsePart7Translation(legacy, originals), originals), legacy);
  const current = alignTranslation(originals[0], { type: "TABLE", content: {
    title: "Bảng", headers: ["Tên"], rows: [["Trà"], ["Bánh"]]
  } });
  assert.deepEqual(current.content.headers, ["Tên", ""]);
  assert.deepEqual(current.content.rows, [["Trà", ""], ["Bánh", ""]]);
});

test("malformed structured value is blocked instead of overwritten", () => {
  const loaded = parsePart7Translation(translationPrefix + "{broken", originals);
  assert.equal(loaded.invalid, true);
  assert.throws(() => serializePart7Translation(loaded, originals));
});

test("metadata-only email and chat edits survive while untouched defaults remain empty", () => {
  const empty = parsePart7Translation(null, originals);
  assert.equal(serializePart7Translation(empty, originals), null);
  empty.documents[1].content.from = "Phòng kinh doanh";
  empty.documents[2].content.messages[0].sender = "Nhân viên";
  const stored = serializePart7Translation(empty, originals);
  const documents = JSON.parse(stored.slice(translationPrefix.length)).documents;
  assert.equal(documents[1].content.from, "Phòng kinh doanh");
  assert.equal(documents[2].content.messages[0].sender, "Nhân viên");
  assert.equal(documents[0], null);
});

test("new layouts keep translated paragraphs, table rows and columns aligned after edits", () => {
  const source = { ...newDocument("INVOICE"), title: "Invoice", paragraphs: ["First", "Second"],
    rows: [["Tea", "1", "$2"], ["Cake", "2", "$4"]] };
  let state = parsePart7Translation(null, [source]);
  state.documents[0].content.paragraphs = ["Đầu", "Sau"];
  state.documents[0].content.rows = [["Trà", "1", "2 đô"], ["Bánh", "2", "4 đô"]];
  state = updateTranslationStructure(state, 0, { key: "paragraphs", type: "move", index: 0, direction: 1 });
  assert.deepEqual(state.documents[0].content.paragraphs, ["Sau", "Đầu"]);
  state = updateTranslationStructure(state, 0, { key: "headers", type: "move", index: 0, direction: 1 });
  assert.deepEqual(state.documents[0].content.rows[0], ["1", "Trà", "2 đô"]);
  state = updateTranslationStructure(state, 0, { key: "rows", type: "delete", index: 0 });
  assert.deepEqual(state.documents[0].content.rows, [["2", "Bánh", "4 đô"]]);
  const aligned = alignTranslation({ ...source, paragraphs: ["Second", "First"],
    headers: ["Quantity", "Item", "Amount"], rows: [["2", "Cake", "$4"]] }, state.documents[0]);
  assert.equal(aligned.content.paragraphs[0], "Sau");
  assert.equal(aligned.content.rows[0][1], "Bánh");
});
