using System;

namespace InterviewHarness
{
    /// <summary>Errore di configurazione o di esecuzione della harness.</summary>
    public class HarnessException : Exception
    {
        public HarnessException(string message) : base(message) { }
        public HarnessException(string message, Exception inner) : base(message, inner) { }
    }
}
