export const hasRoadmapContent = course => course?.units?.some(unit =>
  unit.lessons?.some(lesson => lesson.contents?.length > 0)) ?? false;
