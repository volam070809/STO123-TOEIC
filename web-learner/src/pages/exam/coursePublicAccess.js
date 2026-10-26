export async function loadCourseCatalog(token, dependencies) {
  if (token) return Promise.all([
    dependencies.loadLearnerCourses(token),
    dependencies.loadPlacement(token),
    dependencies.loadHistory(token),
  ]);

  const courses = await dependencies.loadPublicCourses();
  return [courses.map(course => ({ ...course,
    accessStatus: "NOT_PURCHASED",
    accessExpiresAt: null,
    recommended: false,
    recommendationOrder: null,
  })), null, null];
}

export function requestGuestCoursePurchase(requireAuthentication) {
  return requireAuthentication("/courses");
}
