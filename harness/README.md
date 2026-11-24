# Harness del recruiter (GEN AI)

La harness è lo strato che governa il modello linguistico durante il colloquio.
Il modello usato è **Google Gemini**; il fornitore si cambia in `config/settings.yaml`
(è disponibile anche Claude di Anthropic).
Sostituisce il singolo system prompt statico con componenti separati, versionati e testati.

```
                       ┌──────────────── harness/ ────────────────┐
 Unity ──CandidateTurn──▶ InterviewSession                         │
                       │   ├─ macchina a stati delle FASI           │  config/phases.yaml
                       │   ├─ PromptBuilder (persona + contesto     │  config/recruiters.yaml
                       │   │   + istruzioni di fase)                │  prompts/**/*.md
                       │   ├─ PiiRedactor (privacy)                 │  config/guardrails.yaml
                       │   ├─ ILlmClient ──▶ Gemini (API Google)    │
                       │   └─ OutputGuard (regole sull'output,      │  config/guardrails.yaml
                       │       rigenerazione, battuta di riserva)   │
 Unity ◀─RecruiterTurn──┤                                           │
 Unity ◀─InterviewEnd───┤ FeedbackGenerator (griglia di valutazione)│  config/evaluation.yaml
                       └───────────────────────────────────────────┘
```

Formato dei messaggi con Unity: [`docs/formato-scambio.md`](../docs/formato-scambio.md).

## Struttura

| Percorso | Contenuto |
|---|---|
| `config/settings.yaml` | Fornitore e modello, effort, limiti di token, tentativi di rigenerazione |
| `config/phases.yaml` | Le 5 fasi del colloquio con obiettivo e numero di domande |
| `config/recruiters.yaml` | Profili di recruiter per settore e livelli di difficoltà |
| `config/evaluation.yaml` | Griglia di valutazione del feedback (criteri, pesi, descrittori) |
| `config/guardrails.yaml` | Regole sull'output, domande vietate, oscuramento dati personali |
| `prompts/system/recruiter.md` | Template del system prompt del recruiter |
| `prompts/phases/*.md` | Istruzioni specifiche di ogni fase |
| `prompts/feedback.md` | Template del prompt per il feedback finale |
| `data/students/`, `data/positions/` | Profili studente (CV) e posizioni di esempio |
| `src/InterviewHarness.Core` | Libreria C# della harness (netstandard2.1, usabile in Unity), incluso il client Gemini |
| `src/InterviewHarness.Anthropic` | Client alternativo per Claude, tramite l'SDK ufficiale Anthropic |
| `cli/` | Strumento da terminale: chat di prova, valutazioni, anteprima prompt |
| `tests/` | Test unitari (modello finto, gratuiti) |
| `evals/` | Casi di valutazione con il modello vero |

## Come funziona un turno

1. **Contesto**: il CV dello studente e le sue risposte passano dal `PiiRedactor`, che oscura email, telefono, codice fiscale e IBAN.
2. **Prompt**: `PromptBuilder` compone il system prompt da persona del recruiter, livello di difficoltà, posizione, profilo, punti deboli raccolti finora e istruzioni della fase corrente.
3. **Modello**: Gemini risponde con un JSON vincolato da uno schema (`responseJsonSchema`): `speech`, `emotion`, `gesture`, `intensity`, `phase_goal_met`, `weak_points`.
4. **Controlli**: `OutputGuard` verifica lunghezza, una sola domanda, permanenza nel ruolo, domande vietate per legge, assenza di markdown. Se qualcosa non va, la battuta viene rigenerata spiegando l'errore al modello; dopo `max_guard_retries` tentativi si usa una battuta di riserva.
5. **Fasi**: la harness (non il modello) decide quando cambiare fase, in base a `phase_goal_met` e ai limiti min/max di domande.
6. **Approfondimenti**: i `weak_points` segnalati dal modello vengono accumulati e mostrati nella fase di approfondimento, così il recruiter torna sulle risposte deboli.
7. **Feedback**: a fine colloquio un'altra chiamata valuta la trascrizione con la griglia; il voto complessivo è calcolato dalla harness come media pesata.

## Comandi

Serve il [.NET 8 SDK](https://dotnet.microsoft.com/download). Dalla radice del repository:

```bash
dotnet test harness/InterviewHarness.sln
```

```bash
dotnet run --project harness/cli -- prompt --phase tecnica
```

```bash
dotnet run --project harness/cli -- chat --recruiter it-tech-lead --difficulty difficile
```

```bash
dotnet run --project harness/cli -- eval
```

`chat` ed `eval` chiamano l'API: serve la chiave in `GEMINI_API_KEY` oppure in un file
`harness/.env` (già escluso da git) con la riga `GEMINI_API_KEY=...`. La chiave si crea su
Google AI Studio. **Non committare mai la chiave.**

## Test

- **Test unitari** (`tests/`): usano un modello finto, sono gratuiti e verificano la logica
  della harness: configurazione coerente, regole sull'output, oscuramento dei dati, passaggi di
  fase, rigenerazione, battuta di riserva, calcolo del voto, formato dei messaggi per Unity.
- **Valutazioni** (`evals/`): colloqui completi con il modello vero e aspettative misurabili.
  Vedi [`evals/README.md`](evals/README.md).

## Uso da Unity

`InterviewHarness.Core` non dipende da Unity né da SDK esterni: si compila per
.NET Standard 2.1 e usa solo Newtonsoft.Json e YamlDotNet. Contiene già `GeminiLlmClient`,
che chiama Gemini via HTTP, quindi in Unity basta copiare la DLL e passare la chiave.
Se su visore (Android) `HttpClient` dovesse dare problemi, si può scrivere un `ILlmClient`
basato su `UnityWebRequest` con lo stesso formato di richiesta.
