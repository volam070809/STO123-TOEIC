export const courseStage = {
  1: { name: "Foundation", range: "Dưới 450", description: "Củng cố nền tảng Listening và Reading." },
  2: { name: "Development", range: "450 - 699", description: "Phát triển kỹ năng và cải thiện các Part còn yếu." },
  3: { name: "Advanced", range: "700+", description: "Tối ưu chiến thuật và nâng cao độ chính xác." },
};

export const courseAudienceLabel = stage => `Dành cho học viên Giai đoạn ${stage}`;

export function courseEntryReference(stage) {
  const range = courseStage[stage]?.range;
  return range ? `Mức đầu vào tham khảo: ${range}` : "Mức đầu vào đang cập nhật";
}

export const courseTargetLabel = maxTargetScore =>
  Number.isInteger(maxTargetScore) && maxTargetScore > 0
    ? `Mục tiêu tối đa: ${maxTargetScore} điểm`
    : "Mục tiêu khóa học đang cập nhật";
