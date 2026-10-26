using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using STO123.Controllers;
using STO123.DTOs.Exam;

namespace STO123.Tests;

public sealed class PublicMockTestsContractTests
{
    [Fact]
    public void Public_catalog_is_anonymous_get_only()
    {
        Assert.Single(typeof(PublicMockTestsController)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.Empty(typeof(PublicMockTestsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true));

        var actions = typeof(PublicMockTestsController).GetMethods()
            .Where(method => method.DeclaringType == typeof(PublicMockTestsController))
            .ToArray();
        Assert.Single(actions);
        Assert.NotEmpty(actions[0].GetCustomAttributes(typeof(HttpGetAttribute), true));
    }

    [Fact]
    public void Public_DTO_exposes_only_overview_metadata()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "ExamId", "ExamCode", "ExamName", "Duration", "QuestionCount"
        };

        var properties = typeof(PublicMockTestDto).GetProperties();
        Assert.Equal(allowed.Count, properties.Length);
        Assert.All(properties, property => Assert.Contains(property.Name, allowed));
        Assert.DoesNotContain(properties, property => property.Name.Contains("Question", StringComparison.Ordinal) &&
            property.Name != "QuestionCount");
    }

    [Fact]
    public void Attempt_and_learning_controllers_remain_authorized()
    {
        Assert.NotEmpty(typeof(MockTestsController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.NotEmpty(typeof(AttemptsController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.NotEmpty(typeof(PracticeController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.NotEmpty(typeof(PlacementController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
    }
}
