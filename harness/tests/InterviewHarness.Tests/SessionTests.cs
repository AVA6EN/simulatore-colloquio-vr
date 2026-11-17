using InterviewHarness.Exchange;
using InterviewHarness.Llm;
using InterviewHarness.Session;
using Newtonsoft.Json.Linq;
using Xunit;

namespace InterviewHarness.Tests;

/// <summary>Flusso del colloquio con un modello finto: fasi, controlli, riserva, fine.</summary>
public class SessionTests
{
    private static InterviewSession NewSession(FakeLlmClient llm, string difficulty = "medio") =>
        new(TestData.Config, llm, TestData.Setup(difficulty));

    [Fact]
    public async Task PrimaBattuta_EAccoglienzaENonPassaDatiPersonali()
    {
        var llm = new FakeLlmClient(TestData.Turn("Ciao Luca, sono Marco. Raccontami di te?", emotion: "friendly", gesture: "handshake"));
        var session = NewSession(llm);

        var turn = await session.StartAsync();

        Assert.Equal("accoglienza", turn.Phase);
        Assert.Equal(1, turn.TurnId);
        Assert.Equal("handshake", turn.Gesture);
        Assert.False(turn.EndOfInterview);
        Assert.Equal("recruiter_turn", llm.Requests[0].Purpose);
        Assert.NotNull(llm.Requests[0].JsonSchema);
    }

    [Fact]
    public async Task ObiettivoRaggiunto_PassaAllaFaseSuccessiva()
    {
        var llm = new FakeLlmClient(
            TestData.Turn("Ciao, presentati?", goalMet: true),
            TestData.Turn("Perché questa posizione?"));
        var session = NewSession(llm);

        await session.StartAsync();
        var turn = await session.RespondAsync("Sono Luca, studio all'ITS.");

        Assert.Equal("motivazionale", turn.Phase);
        Assert.Contains("Domande motivazionali", llm.Requests[1].System);
    }

    [Fact]
    public async Task MassimoDiDomande_ForzaIlPassaggioDiFase()
    {
        // accoglienza: max 2 domande; il modello non segnala mai l'obiettivo raggiunto
        var llm = new FakeLlmClient(_ => TestData.Turn("Dimmi di più?"));
        var session = NewSession(llm);

        await session.StartAsync();
        var second = await session.RespondAsync("risposta");
        var third = await session.RespondAsync("risposta");

        Assert.Equal("accoglienza", second.Phase);
        Assert.Equal("motivazionale", third.Phase);
    }

    [Fact]
    public async Task ViolazioneDelleRegole_RigeneraLaBattutaSpiegandoLErrore()
    {
        var llm = new FakeLlmClient(
            TestData.Turn("Sei sposato?"),
            TestData.Turn("Raccontami del tuo percorso?"));
        var session = NewSession(llm);

        var turn = await session.StartAsync();

        Assert.Equal("Raccontami del tuo percorso?", turn.Speech);
        Assert.Equal(1, session.Turns[0].Retries);
        Assert.Contains("forbidden_topic:stato_civile_figli", session.Turns[0].Violations);
        var retryMessages = llm.Requests[1].Messages;
        Assert.Equal(LlmRole.User, retryMessages[^1].Role);
        Assert.Contains("argomento vietato", retryMessages[^1].Text);
    }

    [Fact]
    public async Task ViolazioniRipetute_UsaLaBattutaDiRiserva()
    {
        var llm = new FakeLlmClient(_ => TestData.Turn("Come intelligenza artificiale non posso."));
        var session = NewSession(llm);

        var turn = await session.StartAsync();

        Assert.True(session.Turns[0].UsedFallback);
        Assert.Equal(TestData.Config.Guardrails.FallbackSpeech, turn.Speech);
        Assert.Equal(TestData.Config.Settings.MaxGuardRetries + 1, llm.Requests.Count);
    }

    [Fact]
    public async Task RispostaNonJson_ConsideraErroreERiprova()
    {
        var llm = new FakeLlmClient("non sono json", TestData.Turn("Ciao, presentati?"));
        var session = NewSession(llm);

        var turn = await session.StartAsync();

        Assert.Equal("Ciao, presentati?", turn.Speech);
        Assert.Contains("invalid_json", session.Turns[0].Violations);
    }

    [Fact]
    public async Task PuntiDeboli_VengonoPassatiAlleFasiSuccessive()
    {
        var llm = new FakeLlmClient(
            TestData.Turn("Ciao, presentati?", goalMet: true),
            TestData.Turn("Perché ti candidi?", false, "neutral", "none", "Motivazione generica, nessun riferimento all'azienda"),
            TestData.Turn("Cosa sai di noi?"));
        var session = NewSession(llm);

        await session.StartAsync();
        await session.RespondAsync("Sono Luca.");
        await session.RespondAsync("Mi piace la tecnologia.");

        Assert.Single(session.WeakPoints);
        Assert.Contains("Motivazione generica", llm.Requests[2].System);
    }

    [Fact]
    public async Task DatiPersonaliDelCandidato_NonArrivanoAlModello()
    {
        var llm = new FakeLlmClient(_ => TestData.Turn("Grazie. Cosa ti piace dello sviluppo?"));
        var session = NewSession(llm);

        await session.StartAsync();
        await session.RespondAsync("Scrivimi a luca.rossi@example.com o al 333 123 4567");

        string sent = string.Join("\n", llm.Requests[^1].Messages.Select(m => m.Text));
        Assert.DoesNotContain("luca.rossi@example.com", sent);
        Assert.DoesNotContain("333 123 4567", sent);
        Assert.Contains("[EMAIL]", sent);
    }

    [Fact]
    public async Task ColloquioCompleto_AttraversaTutteLeFasiETermina()
    {
        // Il modello finto segnala sempre l'obiettivo raggiunto; nell'ultima fase si congeda senza domande.
        var llm = new FakeLlmClient(req =>
            req.System.Contains("Fase attuale: Chiusura")
                ? TestData.Turn("Grazie Luca, ti faremo sapere. Arrivederci!", goalMet: true, emotion: "friendly", gesture: "wave")
                : TestData.Turn("Prossima domanda?", goalMet: true));
        var session = NewSession(llm);

        var turn = await session.StartAsync();
        int guard = 0;
        while (!turn.EndOfInterview && guard++ < 30) turn = await session.RespondAsync("risposta");

        Assert.True(session.IsFinished);
        Assert.Equal("chiusura", turn.Phase);
        Assert.Equal(
            TestData.Config.Phases.Select(p => p.Id),
            session.Turns.Select(t => t.Turn.Phase).Distinct());
        await Assert.ThrowsAsync<HarnessException>(() => session.RespondAsync("ancora?"));
    }

    [Fact]
    public async Task Chiusura_SeIlModelloNonSiCongeda_ForzaLUltimaBattuta()
    {
        var llm = new FakeLlmClient(req =>
            req.System.Contains("ULTIMA battuta")
                ? TestData.Turn("Grazie, arrivederci.", goalMet: true)
                : TestData.Turn("Hai domande?", goalMet: true));
        var session = NewSession(llm);

        var turn = await session.StartAsync();
        int guard = 0;
        while (!turn.EndOfInterview && guard++ < 30) turn = await session.RespondAsync("sì, una domanda");

        Assert.True(turn.EndOfInterview);
        Assert.Equal("Grazie, arrivederci.", turn.Speech);
    }

    [Fact]
    public void RecruiterTurn_UsaINomiDeiCampiDelFormatoDiScambio()
    {
        var json = JObject.Parse(ExchangeSerializer.ToJson(new RecruiterTurn { TurnId = 3, Phase = "tecnica", Speech = "Ciao" }));
        foreach (var field in new[] { "version", "type", "turn_id", "phase", "speech", "emotion", "gesture", "intensity", "end_of_interview" })
            Assert.True(json.ContainsKey(field), field);
        Assert.Equal("recruiter_turn", (string?)json["type"]);
    }
}
