export const toeicParts = [
  { number: 1, section: "listening", english: "Photographs", vietnamese: "Mô tả hình ảnh", questions: 6,
    task: "Nghe bốn câu mô tả và chọn câu phù hợp nhất với bức ảnh.",
    material: "Một ảnh và các câu mô tả được phát qua audio." },
  { number: 2, section: "listening", english: "Question–Response", vietnamese: "Hỏi và đáp", questions: 25,
    task: "Nghe một câu hỏi hoặc lời nói, rồi chọn phản hồi phù hợp nhất trong ba đáp án nghe được.",
    material: "Câu hỏi và phản hồi được phát qua audio, không in thành đoạn đọc." },
  { number: 3, section: "listening", english: "Conversations", vietnamese: "Hội thoại", questions: 39,
    task: "Nghe 13 cuộc hội thoại và trả lời ba câu hỏi cho mỗi cuộc.",
    material: "Hội thoại ngắn trong tình huống thường ngày hoặc công việc; câu hỏi và lựa chọn hiện trên đề." },
  { number: 4, section: "listening", english: "Talks", vietnamese: "Bài nói ngắn", questions: 30,
    task: "Nghe 10 bài nói và trả lời ba câu hỏi cho mỗi bài.",
    material: "Thông báo, hướng dẫn hoặc bài nói ngắn; câu hỏi và lựa chọn hiện trên đề." },
  { number: 5, section: "reading", english: "Incomplete Sentences", vietnamese: "Hoàn thành câu", questions: 30,
    task: "Chọn từ hoặc cụm từ phù hợp để hoàn thành từng câu.",
    material: "Các câu riêng lẻ có một chỗ trống và bốn lựa chọn." },
  { number: 6, section: "reading", english: "Text Completion", vietnamese: "Hoàn thành đoạn văn", questions: 16,
    task: "Đọc văn bản và chọn từ, cụm từ hoặc câu phù hợp cho mỗi chỗ trống.",
    material: "Bốn văn bản ngắn, mỗi văn bản có bốn câu hỏi." },
  { number: 7, section: "reading", english: "Reading Comprehension", vietnamese: "Đọc hiểu", questions: 54,
    task: "Đọc một hoặc nhiều văn bản liên quan, rồi trả lời câu hỏi về thông tin và ý nghĩa.",
    material: "29 câu từ bài đọc đơn; 25 câu từ bộ hai hoặc ba văn bản như email, thông báo và lịch trình." },
];

export const sectionParts = section => toeicParts.filter(part => part.section === section);
export const sectionQuestionCount = section => sectionParts(section).reduce((total, part) => total + part.questions, 0);
