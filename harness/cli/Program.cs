using System.Globalization;
using System.Text;
using InterviewHarness;
using InterviewHarness.AnthropicClient;
using InterviewHarness.Config;
using InterviewHarness.Evaluation;
using InterviewHarness.Exchange;
using InterviewHarness.Prompting;
using InterviewHarness.Session;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

try
{
    string root = HarnessConfig.FindHarnessRoot(Directory.GetCurrentDirectory());
    var config = HarnessConfig.Load(root);
    var opts = Options.Parse(args);

    return opts.Command switch
    {
        "chat" => await Commands.Chat(config, opts),
        "eval" => await Commands.Eval(config, opts),
        "prompt" => Commands.Prompt(config, opts),
        _ => Commands.Help(),
    };
}
catch (HarnessException ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("Errore: " + ex.Message);
    Console.ResetColor();
    return 1;
}

static class Commands
{
    public static int Help()
    {
        Console.WriteLine("""
            Harness del simulatore di colloquio

            Uso: dotnet run --project harness/cli -- <comando> [opzioni]

              chat     Colloquio di prova da terminale (scrivi tu le risposte del candidato)
              eval     Esegue i casi di test in harness/evals/cases (tutti, o quello indicato)
              prompt   Mostra il system prompt del recruiter per una fase (non chiama l'API)

            Opzioni per chat e prompt:
              --student <id>      default: studente-esempio
              --position <id>     default: junior-developer
              --recruiter <id>    default: it-tech-lead
              --difficulty <id>   default: medio
              --json              (chat) mostra anche i messaggi JSON inviati a Unity
              --phase <id>        (prompt) fase da mostrare, default: la prima

            Il fornitore si sceglie in harness/config/settings.yaml (provider: gemini | anthropic).
            La chiave API si legge da GEMINI_API_KEY (o ANTHROPIC_API_KEY) oppure dal file
            harness/.env, escluso da git.
            """);
        return 0;
    }

    public static int Prompt(HarnessConfig config, Options opts)
    {
        var setup = opts.Setup(config);
        int index = opts.Phase == null ? 0 : config.Phases.FindIndex(p => p.Id == opts.Phase);
        if (index < 0) throw new HarnessException($"Fase '{opts.Phase}' non trovata.");
        var (min, max) = PromptBuilder.PhaseLimits(config.Phases[index], setup.Difficulty);
        var builder = new PromptBuilder(config);
        Console.WriteLine(builder.BuildRecruiterSystem(setup, $"Corso: {setup.Student.Course}\n\n{setup.Student.Cv}",
            new PhaseState { PhaseIndex = index, MinQuestions = min, MaxQuestions = max }, new List<string>()));
        return 0;
    }

    public static async Task<int> Chat(HarnessConfig config, Options opts)
    {
        var setup = opts.Setup(config);
        var session = new InterviewSession(config, CreateClient(config), setup);

        Console.WriteLine($"Colloquio per {setup.Position.Title} con {setup.Persona.Name} ({setup.Difficulty.Name}).");
        Console.WriteLine("Scrivi le risposte e premi Invio. Scrivi /fine per terminare subito.\n");

        var turn = await session.StartAsync();
        while (true)
        {
            PrintTurn(turn, session, opts.Json);
            if (turn.EndOfInterview) break;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"{setup.Student.FirstName}> ");
            Console.ResetColor();
            string? line = Console.ReadLine();
            if (line == null || line.Trim() == "/fine") break;
            turn = await session.RespondAsync(line);
        }

        Console.WriteLine("\nGenero il feedback...\n");
        var end = await session.EndAsync();
        PrintFeedback(end.Feedback);
        if (opts.Json) Console.WriteLine(ExchangeSerializer.ToJson(end, indented: true));

        string path = SaveRun(config, "chat", new { setup = opts.SetupIds(), transcript = session.Transcript, turns = session.Turns, feedback = end.Feedback });
        Console.WriteLine($"\nTrascrizione salvata in {path}");
        return 0;
    }

    public static async Task<int> Eval(HarnessConfig config, Options opts)
    {
        string casesDir = Path.Combine(config.RootDir, "evals", "cases");
        var files = Directory.GetFiles(casesDir, "*.yaml").OrderBy(f => f).ToList();
        if (opts.Positional != null)
            files = files.Where(f => Path.GetFileNameWithoutExtension(f) == opts.Positional).ToList();
        if (files.Count == 0) throw new HarnessException("Nessun caso di test trovato.");

        var runner = new EvalRunner(config, CreateClient(config));
        var results = new List<EvalResult>();
        foreach (var file in files)
        {
            var evalCase = HarnessConfig.ReadYaml<EvalCase>(file);
            Console.Write($"▶ {evalCase.Id} ... ");
            var result = await runner.RunAsync(evalCase);
            results.Add(result);
            Console.ForegroundColor = result.Passed ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(result.Passed ? "OK" : "FALLITO");
            Console.ResetColor();
            if (result.Error != null) Console.WriteLine($"   errore: {result.Error}");
            foreach (var c in result.Checks)
                Console.WriteLine($"   {(c.Passed ? "✔" : "✘")} {c.Name} — {c.Detail}");
        }

        string report = BuildReport(config, results);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string dir = Path.Combine(config.RootDir, "runs");
        Directory.CreateDirectory(dir);
        string mdPath = Path.Combine(dir, $"{stamp}-eval.md");
        File.WriteAllText(mdPath, report);
        SaveRun(config, "eval", results.Select(r => new
        {
            id = r.Case.Id, passed = r.Passed, error = r.Error, checks = r.Checks,
            transcript = r.Transcript, turns = r.Turns, feedback = r.Feedback,
        }), stamp);

        int passed = results.Count(r => r.Passed);
        Console.WriteLine($"\n{passed}/{results.Count} casi superati. Report: {mdPath}");
        return passed == results.Count ? 0 : 2;
    }

    // ------------------------------------------------------------------

    private static InterviewHarness.Llm.ILlmClient CreateClient(HarnessConfig config)
    {
        string provider = config.Settings.Provider.ToLowerInvariant();
        string variable = provider switch
        {
            "gemini" => "GEMINI_API_KEY",
            "anthropic" => "ANTHROPIC_API_KEY",
            _ => throw new HarnessException($"Provider '{config.Settings.Provider}' non supportato (usa gemini o anthropic)."),
        };

        string? key = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(key))
        {
            string envFile = Path.Combine(config.RootDir, ".env");
            if (File.Exists(envFile))
                key = File.ReadAllLines(envFile)
                    .Select(l => l.Trim())
                    .Where(l => l.StartsWith(variable + "=", StringComparison.Ordinal))
                    .Select(l => l[(variable.Length + 1)..].Trim().Trim('"'))
                    .FirstOrDefault();
        }
        if (string.IsNullOrEmpty(key))
            throw new HarnessException($"Chiave API mancante: imposta {variable} oppure crea harness/.env con la riga {variable}=...");

        return provider == "gemini"
            ? new InterviewHarness.Llm.GeminiLlmClient(key)
            : new AnthropicLlmClient(key);
    }

    private static void PrintTurn(RecruiterTurn turn, InterviewSession session, bool json)
    {
        var record = session.Turns[^1];
        Console.ForegroundColor = ConsoleColor.DarkGray;
        string extra = record.Retries > 0 ? $", rigenerata {record.Retries}x: {string.Join(", ", record.Violations)}" : "";
        if (record.UsedFallback) extra += ", BATTUTA DI RISERVA";
        Console.WriteLine($"[{turn.Phase} | {turn.Emotion} | {turn.Gesture}{extra}]");
        Console.ResetColor();
        Console.WriteLine($"{session.Setup.Persona.Name}: {turn.Speech}\n");
        if (json) Console.WriteLine(ExchangeSerializer.ToJson(turn, indented: true) + "\n");
    }

    private static void PrintFeedback(FeedbackReport f)
    {
        Console.WriteLine($"VOTO COMPLESSIVO: {f.OverallScore.ToString("0.0", CultureInfo.InvariantCulture)} / 5 — {f.OverallLabel}\n");
        Console.WriteLine(f.Summary + "\n");
        foreach (var c in f.Criteria)
        {
            Console.WriteLine($"■ {c.Name}: {c.Score}/5");
            Console.WriteLine($"  {c.Motivation}");
            foreach (var e in c.Evidence) Console.WriteLine($"  » {e}");
            Console.WriteLine($"  Consiglio: {c.Suggestion}\n");
        }
        Console.WriteLine("Punti di forza:");
        foreach (var s in f.Strengths) Console.WriteLine($"  + {s}");
        Console.WriteLine("Da migliorare:");
        foreach (var s in f.Improvements) Console.WriteLine($"  - {s}");
    }

    private static string BuildReport(HarnessConfig config, List<EvalResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Valutazione harness — {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"Fornitore: `{config.Settings.Provider}` · modello: `{config.Settings.Model}` · effort battute: `{config.Settings.TurnEffort}` · effort feedback: `{config.Settings.FeedbackEffort}`");
        sb.AppendLine();
        sb.AppendLine("| Caso | Esito | Check superati | Battute | Voto | Token in/out |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var r in results)
            sb.AppendLine($"| {r.Case.Id} | {(r.Passed ? "✅" : "❌")} | {r.Checks.Count(c => c.Passed)}/{r.Checks.Count} | {r.Turns.Count} | " +
                          $"{(r.Feedback == null ? "-" : r.Feedback.OverallScore.ToString("0.0", CultureInfo.InvariantCulture))} | {r.InputTokens}/{r.OutputTokens} |");
        foreach (var r in results)
        {
            sb.AppendLine($"\n## {r.Case.Id}\n");
            sb.AppendLine(r.Case.Description.Trim() + "\n");
            if (r.Error != null) sb.AppendLine($"**Errore:** {r.Error}\n");
            foreach (var c in r.Checks) sb.AppendLine($"- {(c.Passed ? "✅" : "❌")} {c.Name} — {c.Detail}");
            sb.AppendLine("\n<details><summary>Trascrizione</summary>\n");
            foreach (var t in r.Transcript) sb.AppendLine($"- **{(t.Speaker == "recruiter" ? "Recruiter" : "Candidato")}** _{t.Phase}_: {t.Text}");
            sb.AppendLine("\n</details>");
        }
        return sb.ToString();
    }

    private static string SaveRun(HarnessConfig config, string kind, object data, string? stamp = null)
    {
        stamp ??= DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string dir = Path.Combine(config.RootDir, "runs");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"{stamp}-{kind}.json");
        File.WriteAllText(path, ExchangeSerializer.ToJson(data, indented: true));
        return path;
    }
}

sealed class Options
{
    public string Command { get; private set; } = "help";
    public string? Positional { get; private set; }
    public string Student { get; private set; } = "studente-esempio";
    public string Position { get; private set; } = "junior-developer";
    public string Recruiter { get; private set; } = "it-tech-lead";
    public string Difficulty { get; private set; } = "medio";
    public string? Phase { get; private set; }
    public bool Json { get; private set; }

    public static Options Parse(string[] args)
    {
        var o = new Options();
        if (args.Length > 0) o.Command = args[0];
        for (int i = 1; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new HarnessException($"Manca il valore per {args[i]}");
            switch (args[i])
            {
                case "--student": o.Student = Next(); break;
                case "--position": o.Position = Next(); break;
                case "--recruiter": o.Recruiter = Next(); break;
                case "--difficulty": o.Difficulty = Next(); break;
                case "--phase": o.Phase = Next(); break;
                case "--json": o.Json = true; break;
                default:
                    if (args[i].StartsWith("--")) throw new HarnessException($"Opzione sconosciuta: {args[i]}");
                    o.Positional = args[i];
                    break;
            }
        }
        return o;
    }

    public SessionSetup Setup(HarnessConfig config) => SessionSetup.FromIds(config, Student, Position, Recruiter, Difficulty);

    public object SetupIds() => new { student = Student, position = Position, recruiter = Recruiter, difficulty = Difficulty };
}
