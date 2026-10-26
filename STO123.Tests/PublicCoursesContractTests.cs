using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using STO123.Controllers;
using STO123.DTOs.Courses;

namespace STO123.Tests;

public sealed class PublicCoursesContractTests
{
    [Fact]
    public void Public_catalog_is_explicitly_anonymous_and_read_only()
    {
        Assert.Single(typeof(PublicCoursesController)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.Empty(typeof(PublicCoursesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true));

        var actions = typeof(PublicCoursesController).GetMethods()
            .Where(method => method.DeclaringType == typeof(PublicCoursesController))
            .ToArray();
        Assert.All(actions, action => Assert.NotEmpty(action
            .GetCustomAttributes(typeof(HttpGetAttribute), true)));
    }

    [Fact]
    public void Public_DTOs_do_not_expose_learning_or_account_data()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "CourseId", "Name", "Stage", "MaxTargetScore", "Description",
            "RetailPrice", "AccessDays", "CoverImageUrl"
        };

        Assert.All(typeof(PublicCourseSummaryDto).GetProperties(), property =>
            Assert.Contains(property.Name, allowed));
        Assert.All(typeof(PublicCourseDetailDto).GetProperties(), property =>
            Assert.Contains(property.Name, allowed));
        Assert.Equal(allowed.Count, typeof(PublicCourseSummaryDto).GetProperties().Length);
        Assert.Equal(allowed.Count, typeof(PublicCourseDetailDto).GetProperties().Length);
    }
}
