using InterviewHarness.Config;
using InterviewHarness.Exchange;

namespace InterviewHarness.Session
{
    /// <summary>Le scelte che definiscono un colloquio: chi è lo studente, per quale posizione, con quale recruiter.</summary>
    public class SessionSetup
    {
        public StudentProfile Student { get; set; } = new StudentProfile();
        public JobPosition Position { get; set; } = new JobPosition();
        public RecruiterPersona Persona { get; set; } = new RecruiterPersona();
        public DifficultyLevel Difficulty { get; set; } = new DifficultyLevel();

        public static SessionSetup FromIds(HarnessConfig config, string studentId, string positionId, string recruiterId, string difficultyId) =>
            new SessionSetup
            {
                Student = config.LoadStudent(studentId),
                Position = config.LoadPosition(positionId),
                Persona = config.GetPersona(recruiterId),
                Difficulty = config.GetDifficulty(difficultyId),
            };

        public static SessionSetup FromMessage(HarnessConfig config, SessionStart start) =>
            FromIds(config, start.StudentProfileId, start.PositionId, start.RecruiterId, start.Difficulty);
    }
}
