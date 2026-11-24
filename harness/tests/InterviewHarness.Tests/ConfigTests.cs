using InterviewHarness.Prompting;
using Xunit;

namespace InterviewHarness.Tests;

/// <summary>La configurazione versionata è coerente (si rompe se qualcuno modifica male un file YAML).</summary>
public class ConfigTests
{
    [Fact]
    public void Config_SiCaricaConFasiNellOrdineAtteso()
    {
        var ids = TestData.Config.Phases.Select(p => p.Id).ToArray();
        Assert.Equal(new[] { "accoglienza", "motivazionale", "tecnica", "approfondimento", "chiusura" }, ids);
    }

    [Fact]
    public void Settings_UsaGeminiConValoriValidi()
    {
        var s = TestData.Config.Settings;
        Assert.Equal("gemini", s.Provider);
        Assert.StartsWith("gemini-", s.Model);
        Assert.Equal("LOW", Llm.GeminiLlmClient.ThinkingLevel(s.TurnEffort));
        Assert.True(s.MaxGuardRetries >= 1);
        Assert.True(s.TurnMaxTokens > 0 && s.FeedbackMaxTokens > s.TurnMaxTokens);
    }

    [Fact]
    public void Fasi_HannoLimitiValidi()
    {
        foreach (var p in TestData.Config.Phases)
        {
            Assert.True(p.MinQuestions >= 1, p.Id);
            Assert.True(p.MaxQuestions >= p.MinQuestions, p.Id);
        }
    }

    [Fact]
    public void GrigliaDiValutazione_PesiSommanoAUno()
    {
        double sum = TestData.Config.Evaluation.Criteria.Sum(c => c.Weight);
        Assert.Equal(1.0, sum, 3);
    }

    [Fact]
    public void GrigliaDiValutazione_ScalaDa1a5()
    {
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, TestData.Config.Evaluation.Scale.Keys.OrderBy(k => k).ToArray());
    }

    [Fact]
    public void OgniRecruiterEOgniLivello_SonoCompleti()
    {
        Assert.NotEmpty(TestData.Config.Recruiters.Personas);
        Assert.All(TestData.Config.Recruiters.Personas, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
            Assert.False(string.IsNullOrWhiteSpace(p.Style));
        });
        Assert.Contains(TestData.Config.Recruiters.DifficultyLevels, d => d.Id == "facile");
        Assert.Contains(TestData.Config.Recruiters.DifficultyLevels, d => d.Id == "difficile");
    }

    [Fact]
    public void PromptDelRecruiter_NonLasciaSegnapostoNonRisolti_InNessunaFase()
    {
        var builder = new PromptBuilder(TestData.Config);
        var setup = TestData.Setup();
        for (int i = 0; i < TestData.Config.Phases.Count; i++)
        {
            string prompt = builder.BuildRecruiterSystem(setup, "CV", new PhaseState { PhaseIndex = i, MinQuestions = 1, MaxQuestions = 2 }, new List<string>());
            Assert.DoesNotContain("{{", prompt);
            Assert.Contains(TestData.Config.Phases[i].Goal, prompt);
        }
    }

    [Fact]
    public void PromptDelFeedback_ContieneTuttiICriteri()
    {
        var builder = new PromptBuilder(TestData.Config);
        string prompt = builder.BuildFeedbackSystem(TestData.Setup(), "Recruiter: ciao\nCandidato: ciao");
        Assert.DoesNotContain("{{", prompt);
        foreach (var c in TestData.Config.Evaluation.Criteria) Assert.Contains($"`{c.Id}`", prompt);
    }

    [Fact]
    public void DifficoltaDifficile_AumentaLeDomandeMassime()
    {
        var tecnica = TestData.Config.Phases.First(p => p.Id == "tecnica");
        var (_, maxMedio) = PromptBuilder.PhaseLimits(tecnica, TestData.Config.GetDifficulty("medio"));
        var (_, maxDifficile) = PromptBuilder.PhaseLimits(tecnica, TestData.Config.GetDifficulty("difficile"));
        Assert.True(maxDifficile > maxMedio);
    }

    [Fact]
    public void IdInesistente_DaErroreChiaro()
    {
        var ex = Assert.Throws<HarnessException>(() => TestData.Config.GetPersona("non-esiste"));
        Assert.Contains("non-esiste", ex.Message);
    }
}
