using System.Text.RegularExpressions;
using STO123.Services.Ai;

namespace STO123.Tests;

public sealed class AiWebsiteRegistryTests
{
    [Fact]
    public void EveryLearnerRouteIsRegisteredForDeployment()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,
                   "web-learner", "src", "routes", "AppRoutes.jsx"))) root = root.Parent;
        Assert.NotNull(root);
        var routes = File.ReadAllText(Path.Combine(root.FullName,
            "web-learner", "src", "routes", "AppRoutes.jsx"));
        var adminStart = routes.IndexOf("<Route path=\"/admin\"", StringComparison.Ordinal);
        Assert.True(adminStart > 0);
        var learnerRoutes = Regex.Matches(routes[..adminStart], "<Route path=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var registered = AiWebsiteGuide.Features.Select(feature => feature.Route)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(learnerRoutes.Order(), registered.Order());
        Assert.All(AiWebsiteGuide.Features, feature => {
            Assert.True(AiWebsiteGuide.RegisteredLink(feature.Link));
            Assert.NotEmpty(feature.Roles);
            Assert.DoesNotContain("ADMIN", string.Join(',', feature.Roles));
            var routeLine = Regex.Match(routes[..adminStart],
                $"<Route path=\"{Regex.Escape(feature.Route)}\"[^\\r\\n]*").Value;
            Assert.NotEmpty(routeLine);
            if (routeLine.Contains("element={<ProtectedRoute>", StringComparison.Ordinal))
                Assert.Equal(["HOC_VIEN"], feature.Roles);
        });
    }

    [Fact]
    public void WorkflowGuidanceUsesRegisteredRoutesAndVerifiedLabels()
    {
        var (payment, links) = AiWebsiteGuide.Resolve("Tôi thanh toán lại qua VNPay ở đâu?");
        Assert.Contains("Thanh toán lại", payment);
        Assert.Contains(links, x => x.Url == "/courses");
        var (placement, placementLinks) = AiWebsiteGuide.Resolve("KNN phân lớp đầu vào thế nào?");
        Assert.Contains("KNN", placement);
        Assert.Contains(placementLinks, x => x.Url == "/placement");
    }

    [Fact]
    public void ModelCannotSupplyUnverifiedNavigation()
    {
        var answer = AiAnswerComposer.Compose("Xem [đáp án](/admin/content) và https://evil.example/",
            [new AiSource("Bài học", "/courses/1/steps/2"),
             new AiSource("Không hợp lệ", "/admin/content")]);
        Assert.DoesNotContain("evil.example", answer);
        Assert.DoesNotContain("/admin/content", answer);
        Assert.Contains("/courses/1/steps/2", answer);
    }
}
