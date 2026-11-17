using InterviewHarness.Config;
using InterviewHarness.Evaluation;
using Xunit;

namespace InterviewHarness.Tests;

public class EvalRunnerTests
{
    [Fact]
    public void CasiDiValutazione_SonoTuttiLeggibiliEConIdCoerente()
    {
        string dir = Path.Combine(TestData.Config.RootDir, "evals", "cases");
        var files = Directory.GetFiles(dir, "*.yaml");
        Assert.NotEmpty(files);
        foreach (var f in files)
        {
            var c = HarnessConfig.ReadYaml<EvalCase>(f);
            Assert.Equal(Path.GetFileNameWithoutExtension(f), c.Id);
            // gli id del setup devono esistere
            TestData.Config.GetPersona(c.Setup.Recruiter);
            TestData.Config.GetDifficulty(c.Setup.Difficulty);
            TestData.Config.LoadStudent(c.Setup.Student);
            TestData.Config.LoadPosition(c.Setup.Position);
        }
    }

    [Fact]
    public async Task Runner_ConModelloFinto_EseguiColloquioEVerificaAspettative()
    {
        var llm = new FakeLlmClient(req => req.Purpose switch
        {
            "feedback" => TestData.Feedback(4),
            _ when req.System.Contains("Fase attuale: Chiusura") => TestData.Turn("Grazie, arrivederci.", goalMet: true),
            _ => TestData.Turn("Parlami del tuo stage in React?", goalMet: true),
        });
        var evalCase = new EvalCase
        {
            Id = "finto",
            Candidate = new EvalCandidate { Mode = "scripted", Answers = { "Ciao", "Risposta" } },
            Expect = new EvalExpectations
            {
                RecruiterMentionsAny = { "React" },
                RecruiterNeverSays = { "intelligenza artificiale" },
                OverallScore = new ScoreRange { Min = 3.5 },
            },
        };

        var result = await new EvalRunner(TestData.Config, llm).RunAsync(evalCase);

        Assert.Null(result.Error);
        Assert.True(result.Passed, string.Join("\n", result.Checks.Where(c => !c.Passed).Select(c => c.Name + ": " + c.Detail)));
        Assert.Equal(4.0, result.Feedback!.OverallScore);
    }
}
