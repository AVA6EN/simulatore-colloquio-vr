using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace InterviewHarness.Config
{
    /// <summary>
    /// Tutta la configurazione versionata della harness, caricata dalla cartella harness/.
    /// </summary>
    public class HarnessConfig
    {
        public string RootDir { get; }
        public Settings Settings { get; }
        public List<PhaseConfig> Phases { get; }
        public RecruitersFile Recruiters { get; }
        public EvaluationGrid Evaluation { get; }
        public GuardrailsConfig Guardrails { get; }

        private HarnessConfig(string rootDir, Settings settings, List<PhaseConfig> phases,
            RecruitersFile recruiters, EvaluationGrid evaluation, GuardrailsConfig guardrails)
        {
            RootDir = rootDir;
            Settings = settings;
            Phases = phases;
            Recruiters = recruiters;
            Evaluation = evaluation;
            Guardrails = guardrails;
        }

        private static readonly IDeserializer Yaml = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        /// <summary>Carica la configurazione dalla cartella harness/ (quella che contiene config/, prompts/, data/).</summary>
        public static HarnessConfig Load(string harnessRoot)
        {
            string configDir = Path.Combine(harnessRoot, "config");
            if (!Directory.Exists(configDir))
                throw new HarnessException($"Cartella di configurazione non trovata: {configDir}");

            var settings = LoadSettings(configDir);
            var phases = ReadYaml<PhasesFile>(Path.Combine(configDir, "phases.yaml")).Phases;
            var recruiters = ReadYaml<RecruitersFile>(Path.Combine(configDir, "recruiters.yaml"));
            var evaluation = ReadYaml<EvaluationGrid>(Path.Combine(configDir, "evaluation.yaml"));
            var guardrails = ReadYaml<GuardrailsConfig>(Path.Combine(configDir, "guardrails.yaml"));

            if (phases.Count == 0) throw new HarnessException("phases.yaml non contiene fasi.");
            return new HarnessConfig(harnessRoot, settings, phases, recruiters, evaluation, guardrails);
        }

        /// <summary>
        /// Cerca la cartella harness/ risalendo da <paramref name="startDir"/> (utile per CLI e test).
        /// </summary>
        public static string FindHarnessRoot(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "config", "phases.yaml")))
                    return dir.FullName;
                string nested = Path.Combine(dir.FullName, "harness");
                if (File.Exists(Path.Combine(nested, "config", "phases.yaml")))
                    return nested;
                dir = dir.Parent;
            }
            throw new HarnessException($"Cartella harness/ non trovata risalendo da {startDir}");
        }

        public RecruiterPersona GetPersona(string id) =>
            Recruiters.Personas.FirstOrDefault(p => p.Id == id)
            ?? throw new HarnessException($"Recruiter '{id}' non trovato in recruiters.yaml");

        public DifficultyLevel GetDifficulty(string id) =>
            Recruiters.DifficultyLevels.FirstOrDefault(d => d.Id == id)
            ?? throw new HarnessException($"Livello di difficoltà '{id}' non trovato in recruiters.yaml");

        public StudentProfile LoadStudent(string id) =>
            ReadYaml<StudentProfile>(Path.Combine(RootDir, "data", "students", id + ".yaml"));

        public JobPosition LoadPosition(string id) =>
            ReadYaml<JobPosition>(Path.Combine(RootDir, "data", "positions", id + ".yaml"));

        /// <summary>Legge un file di prompt relativo a harness/prompts/.</summary>
        public string ReadPrompt(string relativePath)
        {
            string path = Path.Combine(RootDir, "prompts", relativePath);
            if (!File.Exists(path)) throw new HarnessException($"Prompt non trovato: {path}");
            return File.ReadAllText(path);
        }

        public static T ReadYaml<T>(string path)
        {
            if (!File.Exists(path)) throw new HarnessException($"File non trovato: {path}");
            try
            {
                return Yaml.Deserialize<T>(File.ReadAllText(path));
            }
            catch (Exception ex) when (!(ex is HarnessException))
            {
                throw new HarnessException($"Errore nel leggere {path}: {ex.Message}", ex);
            }
        }

        /// <summary>settings.yaml, con eventuali valori sovrascritti da settings.local.yaml (non versionato).</summary>
        private static Settings LoadSettings(string configDir)
        {
            var merged = new Dictionary<string, object>();
            foreach (var name in new[] { "settings.yaml", "settings.local.yaml" })
            {
                string path = Path.Combine(configDir, name);
                if (!File.Exists(path)) continue;
                var values = Yaml.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                if (values == null) continue;
                foreach (var kv in values) merged[kv.Key] = kv.Value;
            }
            string yaml = new SerializerBuilder().Build().Serialize(merged);
            return Yaml.Deserialize<Settings>(yaml) ?? new Settings();
        }
    }
}
