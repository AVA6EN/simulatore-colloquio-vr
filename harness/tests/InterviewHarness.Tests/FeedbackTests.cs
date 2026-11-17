using InterviewHarness.Exchange;
using InterviewHarness.Feedback;
using InterviewHarness.Session;
using Xunit;

namespace InterviewHarness.Tests;

public class FeedbackTests
{
    [Fact]
    public void VotoComplessivo_EMediaPesata()
    {
        var criteria = new List<CriterionFeedback>
        {
            new() { Score = 5, Weight = 0.5 },
            new() { Score = 3, Weight = 0.25 },
            new() { Score = 1, Weight = 0.25 },
        };
        Assert.Equal(3.5, FeedbackGenerator.OverallScore(criteria));
    }

    [Fact]
    public void Report_ContieneTuttiICriteriConNomeEPeso()
    {
        var report = FeedbackGenerator.TryBuildReport(TestData.Feedback(4), TestData.Config.Evaluation, out _);

        Assert.NotNull(report);
        Assert.Equal(TestData.Config.Evaluation.Criteria.Count, report!.Criteria.Count);
        Assert.All(report.Criteria, c => Assert.False(string.IsNullOrEmpty(c.Name)));
        Assert.Equal(4.0, report.OverallScore);
        Assert.Equal("Buono", report.OverallLabel);
    }

    [Fact]
    public void CriterioMancante_ReportNonValido()
    {
        var report = FeedbackGenerator.TryBuildReport(TestData.Feedback(3, skipCriterion: "competenze"), TestData.Config.Evaluation, out var error);
        Assert.Null(report);
        Assert.Contains("competenze", error);
    }

    [Fact]
    public void PunteggioFuoriScala_VieneLimitato()
    {
        var report = FeedbackGenerator.TryBuildReport(TestData.Feedback(9), TestData.Config.Evaluation, out _);
        Assert.All(report!.Criteria, c => Assert.Equal(5, c.Score));
    }

    [Fact]
    public async Task Feedback_RiceveLaTrascrizioneERiprovaSeIncompleto()
    {
        var llm = new FakeLlmClient(
            TestData.Turn("Ciao Luca, presentati?"),
            TestData.Feedback(3, skipCriterion: "motivazione"),
            TestData.Feedback(3));
        var session = new InterviewSession(TestData.Config, llm, TestData.Setup());
        await session.StartAsync();

        var end = await session.EndAsync();

        Assert.Equal("interview_end", end.Type);
        Assert.Equal(3.0, end.Feedback.OverallScore);
        Assert.Equal(3, llm.Requests.Count);
        Assert.Contains("Ciao Luca, presentati?", llm.Requests[1].System);
        Assert.Equal("feedback", llm.Requests[1].Purpose);
    }
}
