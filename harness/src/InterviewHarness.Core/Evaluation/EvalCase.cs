using System.Collections.Generic;

namespace InterviewHarness.Evaluation
{
    /// <summary>Un caso di test in harness/evals/cases/*.yaml.</summary>
    public class EvalCase
    {
        public string Id { get; set; } = "";
        public string Description { get; set; } = "";
        public EvalSetup Setup { get; set; } = new EvalSetup();
        public EvalCandidate Candidate { get; set; } = new EvalCandidate();
        public int MaxTurns { get; set; } = 25;
        public EvalExpectations Expect { get; set; } = new EvalExpectations();
    }

    public class EvalSetup
    {
        public string Student { get; set; } = "studente-esempio";
        public string Position { get; set; } = "junior-developer";
        public string Recruiter { get; set; } = "it-tech-lead";
        public string Difficulty { get; set; } = "medio";
    }

    public class EvalCandidate
    {
        /// <summary>"scripted": risposte fisse in ordine; "simulated": il candidato è interpretato dal modello.</summary>
        public string Mode { get; set; } = "scripted";
        public List<string> Answers { get; set; } = new List<string>();
        /// <summary>Per "simulated": come deve comportarsi il candidato.</summary>
        public string Persona { get; set; } = "";
        /// <summary>Per "scripted": risposta usata quando le risposte finiscono.</summary>
        public string DefaultAnswer { get; set; } = "Non saprei cosa aggiungere.";
    }

    public class ScoreRange
    {
        public double? Min { get; set; }
        public double? Max { get; set; }
    }

    public class EvalExpectations
    {
        public bool Completes { get; set; } = true;
        public bool AllPhases { get; set; } = true;
        public int MaxFallbacks { get; set; } = 0;
        /// <summary>Numero massimo di battute bloccate dai controlli (anche se poi corrette).</summary>
        public int? MaxGuardViolations { get; set; }
        /// <summary>Almeno una di queste parole deve comparire nelle battute del recruiter (personalizzazione).</summary>
        public List<string> RecruiterMentionsAny { get; set; } = new List<string>();
        /// <summary>Nessuna di queste parole deve comparire nelle battute del recruiter.</summary>
        public List<string> RecruiterNeverSays { get; set; } = new List<string>();
        public ScoreRange? OverallScore { get; set; }
        public Dictionary<string, ScoreRange> Criteria { get; set; } = new Dictionary<string, ScoreRange>();
    }
}
