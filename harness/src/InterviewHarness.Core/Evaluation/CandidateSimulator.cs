using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using InterviewHarness.Config;
using InterviewHarness.Llm;
using InterviewHarness.Session;

namespace InterviewHarness.Evaluation
{
    /// <summary>
    /// Il candidato dei casi di test: risposte fisse ("scripted") oppure interpretato
    /// dal modello secondo una persona ("simulated").
    /// </summary>
    public class CandidateSimulator
    {
        private readonly HarnessConfig _config;
        private readonly ILlmClient _llm;
        private readonly EvalCandidate _candidate;
        private int _nextAnswer;

        public long InputTokens { get; private set; }
        public long OutputTokens { get; private set; }

        public CandidateSimulator(HarnessConfig config, ILlmClient llm, EvalCandidate candidate)
        {
            _config = config;
            _llm = llm;
            _candidate = candidate;
        }

        public async Task<string> AnswerAsync(IReadOnlyList<TranscriptEntry> transcript, CancellationToken ct)
        {
            if (_candidate.Mode != "simulated")
                return _nextAnswer < _candidate.Answers.Count ? _candidate.Answers[_nextAnswer++] : _candidate.DefaultAnswer;

            // Per il candidato simulato i ruoli sono invertiti: il recruiter è "user".
            var messages = new List<LlmMessage>();
            foreach (var e in transcript)
                messages.Add(new LlmMessage(e.Speaker == "recruiter" ? LlmRole.User : LlmRole.Assistant, e.Text));

            var response = await _llm.CompleteAsync(new LlmRequest
            {
                Purpose = "candidate_simulator",
                Model = _config.Settings.Model,
                System = "Stai interpretando un candidato a un colloquio di lavoro simulato, per testare un recruiter virtuale.\n" +
                         "Rispondi SOLO con le parole che il candidato direbbe ad alta voce, in italiano, senza note o didascalie.\n" +
                         "Risposte di lunghezza naturale (da 1 a 5 frasi).\n\n# Il tuo personaggio\n\n" + _candidate.Persona,
                Messages = messages,
                MaxTokens = 1000,
                Effort = "low",
            }, ct).ConfigureAwait(false);

            InputTokens += response.InputTokens;
            OutputTokens += response.OutputTokens;
            return response.Text.Trim();
        }
    }
}
