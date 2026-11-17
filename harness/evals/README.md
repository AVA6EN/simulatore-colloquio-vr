# Casi di valutazione della harness

Ogni file in `cases/` descrive un colloquio completo da far girare **contro il modello vero**
e le aspettative da verificare alla fine. Servono a rispondere alla domanda:
*"questa modifica alla harness ha migliorato o peggiorato il recruiter?"*

```bash
dotnet run --project harness/cli -- eval              # tutti i casi
dotnet run --project harness/cli -- eval candidato-vago  # un solo caso
```

Il report (markdown + JSON con trascrizioni e feedback) viene salvato in `harness/runs/`
(cartella esclusa da git). Ogni esecuzione chiama l'API e **consuma credito**.

## Come si usa nel lavoro di gruppo

1. Prima di modificare un prompt o una regola, esegui `eval` e annota i risultati.
2. Fai la modifica sul tuo branch.
3. Riesegui `eval`: nella pull request riporta prima/dopo (quanti check passano, voto dei casi).

## Campi di un caso

| Campo | Significato |
|---|---|
| `setup` | studente, posizione, recruiter e difficoltà (id dei file in `config/` e `data/`) |
| `candidate.mode` | `scripted` = risposte fisse in ordine; `simulated` = il candidato è interpretato dal modello secondo `persona` |
| `max_turns` | numero massimo di battute del recruiter |
| `expect.completes` | il colloquio deve arrivare alla chiusura |
| `expect.all_phases` | devono essere attraversate tutte le fasi |
| `expect.max_fallbacks` | quante volte al massimo si può usare la battuta di riserva |
| `expect.max_guard_violations` | quante battute al massimo possono essere bloccate dai controlli |
| `expect.recruiter_mentions_any` | almeno una parola deve comparire nelle domande (personalizzazione sul CV) |
| `expect.recruiter_never_says` | parole che il recruiter non deve mai dire |
| `expect.overall_score` / `expect.criteria` | intervalli `{min, max}` attesi per il feedback |

I casi `simulated` non sono identici a ogni esecuzione: se un check fallisce una volta,
riesegui il caso prima di trarre conclusioni.
