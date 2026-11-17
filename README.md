# Simulatore di colloquio di lavoro in VR

Progetto del Laboratorio Integrato ITS Academy (GEN AI Specialist + AR/VR e 3D Artist),
commessa di Piazza del Lavoro.

Un avatar 3D, nel ruolo di recruiter HR, conduce un colloquio con lo studente in realtà
virtuale: accoglienza, domande motivazionali e tecniche, approfondimenti sui punti deboli,
chiusura. Al termine lo studente riceve un feedback scritto basato su criteri espliciti.

## Struttura del repository

| Cartella | Responsabili | Contenuto |
|---|---|---|
| [`harness/`](harness/README.md) | GEN AI | Harness che governa Claude: fasi, profili recruiter, contesto, controlli, feedback, test |
| `unity/` | AR/VR | Progetto Unity: avatar, animazioni, lip sync, ambiente, integrazione |
| [`docs/`](docs/) | tutti | [Formato di scambio](docs/formato-scambio.md) tra harness e Unity, [flusso Git](docs/flusso-git.md) |

Il punto di integrazione tra le due parti è il **formato di scambio**
([`docs/formato-scambio.md`](docs/formato-scambio.md)): la harness produce messaggi JSON con il
testo da pronunciare, l'emozione e il gesto; Unity li trasforma in voce e animazioni.

## Requisiti

- **Harness**: .NET 8 SDK, chiave API Anthropic
- **Unity**: versione da definire (team AR/VR)
- **Git LFS** per gli asset 3D, le texture e l'audio (`git lfs install` una volta sola)
