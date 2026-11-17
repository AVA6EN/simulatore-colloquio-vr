using InterviewHarness.Guardrails;
using Xunit;

namespace InterviewHarness.Tests;

public class GuardrailsTests
{
    private readonly OutputGuard _guard = new(TestData.Config.Guardrails);
    private readonly PiiRedactor _redactor = new(TestData.Config.Guardrails.PiiRedaction);

    private List<string> Codes(string speech) =>
        _guard.Check(new RecruiterModelOutput { Speech = speech }).Select(v => v.Code).ToList();

    [Fact]
    public void BattutaCorretta_NessunaViolazione()
    {
        Assert.Empty(Codes("Interessante! Mi racconti come hai gestito il login nell'app delle aule studio?"));
    }

    [Fact]
    public void BattutaTroppoLunga_Bloccata()
    {
        string speech = string.Join(" ", Enumerable.Repeat("parola", TestData.Config.Guardrails.MaxSpeechWords + 1));
        Assert.Contains("too_long", Codes(speech));
    }

    [Fact]
    public void DueDomandeInUnaBattuta_Bloccata()
    {
        Assert.Contains("too_many_questions", Codes("Perché React? E perché non Angular?"));
    }

    [Theory]
    [InlineData("Come intelligenza artificiale non posso valutarti.")]
    [InlineData("Sono Claude, un assistente creato da Anthropic.")]
    public void UscitaDalRuolo_Bloccata(string speech)
    {
        Assert.Contains("role_break", Codes(speech));
    }

    [Theory]
    [InlineData("Sei sposato?", "forbidden_topic:stato_civile_figli")]
    [InlineData("Hai figli o pensi di averne?", "forbidden_topic:stato_civile_figli")]
    [InlineData("Quanti anni hai?", "forbidden_topic:eta")]
    [InlineData("Per chi voti di solito?", "forbidden_topic:politica_sindacato")]
    public void DomandeDiscriminatorie_Bloccate(string speech, string code)
    {
        Assert.Contains(code, Codes(speech));
    }

    [Fact]
    public void ParoleInnocue_NonSonoFalsiPositivi()
    {
        Assert.Empty(Codes("Da quale progetto sei partito per imparare React?"));
    }

    [Fact]
    public void Markdown_Bloccato()
    {
        Assert.Contains("markdown", Codes("Ecco le domande:\n- prima domanda\n- seconda"));
        Assert.Contains("markdown", Codes("Parliamo di **React**."));
    }

    [Fact]
    public void BattutaVuota_Bloccata()
    {
        Assert.Contains("empty_speech", Codes("   "));
    }

    [Fact]
    public void Sanitize_CorreggeValoriFuoriVocabolario()
    {
        var o = new RecruiterModelOutput { Speech = " Ciao ", Emotion = "arrabbiato", Gesture = "balla", Intensity = 3 };
        OutputGuard.Sanitize(o, "friendly");
        Assert.Equal("friendly", o.Emotion);
        Assert.Equal("none", o.Gesture);
        Assert.Equal(1.0, o.Intensity);
        Assert.Equal("Ciao", o.Speech);
    }

    [Theory]
    [InlineData("scrivimi a luca.rossi@example.com", "[EMAIL]")]
    [InlineData("il mio numero è 333 123 4567", "[TELEFONO]")]
    [InlineData("chiamami al +39 347-555-1234", "[TELEFONO]")]
    [InlineData("CF RSSLCU03A01L219X", "[CODICE_FISCALE]")]
    [InlineData("IBAN IT60X0542811101000000123456", "[IBAN]")]
    public void DatiPersonali_Oscurati(string input, string expected)
    {
        string output = _redactor.Redact(input);
        Assert.Contains(expected, output);
    }

    [Fact]
    public void TestoNormale_NonVieneModificato()
    {
        const string text = "Nel 2025 ho fatto uno stage di 3 mesi e ho lavorato su 12 pagine React.";
        Assert.Equal(text, _redactor.Redact(text));
    }
}
