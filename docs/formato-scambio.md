# Formato di scambio Harness ⇄ Unity

> **Stato: BOZZA v0.1 — da approvare insieme al team AR/VR.**
> Ogni modifica a questo file va fatta con una pull request revisionata da almeno
> un membro GEN AI e uno AR/VR, e fa salire il campo `version`.

Questo documento è il **contratto** tra le due metà del progetto:

```
 Studente (voce/testo)            Harness (GEN AI)                     Unity (AR/VR)
 ───────────────────▶  CandidateTurn  ──▶  Claude + regole  ──▶  RecruiterTurn  ──▶  avatar parla, si muove
                                                              └─▶ InterviewEnd   ──▶  schermata feedback
```

Unity non deve sapere come funziona la harness, e la harness non deve sapere come
sono fatte le animazioni: entrambe rispettano solo i messaggi qui sotto.

---

## 1. `SessionStart` — Unity → Harness

Inviato una volta, quando lo studente avvia il colloquio dal menu.

```json
{
  "version": "0.1",
  "type": "session_start",
  "student_profile_id": "studente-esempio",
  "position_id": "junior-developer",
  "recruiter_id": "it-tech-lead",
  "difficulty": "medio"
}
```

Gli id fanno riferimento ai file in `harness/data/` e `harness/config/`.

## 2. `CandidateTurn` — Unity → Harness

La risposta dello studente, già trasformata in testo.

```json
{
  "version": "0.1",
  "type": "candidate_turn",
  "text": "Buongiorno, mi chiamo Luca e ho appena finito il corso ITS...",
  "silence_seconds": 1.4
}
```

| Campo | Tipo | Note |
|---|---|---|
| `text` | string | Testo della risposta. Vuoto se lo studente non ha risposto. |
| `silence_seconds` | number, opzionale | Pausa prima di rispondere (utile per il feedback). |

## 3. `RecruiterTurn` — Harness → Unity

Una battuta del recruiter.

```json
{
  "version": "0.1",
  "type": "recruiter_turn",
  "turn_id": 3,
  "phase": "motivazionale",
  "speech": "Interessante. Cosa ti ha spinto a scegliere proprio lo sviluppo web?",
  "emotion": "interested",
  "gesture": "lean_forward",
  "intensity": 0.6,
  "end_of_interview": false
}
```

| Campo | Tipo | Valori ammessi | Uso in Unity |
|---|---|---|---|
| `turn_id` | int | progressivo da 1 | log / debug |
| `phase` | string | `accoglienza`, `motivazionale`, `tecnica`, `approfondimento`, `chiusura` | UI facoltativa (es. barra di avanzamento) |
| `speech` | string | max ~60 parole, niente markdown/emoji | testo da sintetizzare (TTS) e da cui ricavare il lip sync |
| `emotion` | string | vedi tabella 5.1 | espressione facciale / blend shape |
| `gesture` | string | vedi tabella 5.2 | animazione del corpo |
| `intensity` | number | 0.0 – 1.0 | peso dell'espressione |
| `end_of_interview` | bool | | se `true`, dopo il parlato Unity attende `InterviewEnd` |

## 4. `InterviewEnd` — Harness → Unity

Inviato dopo l'ultima battuta di chiusura. Contiene il feedback scritto da mostrare.

```json
{
  "version": "0.1",
  "type": "interview_end",
  "feedback": {
    "overall_score": 3.6,
    "overall_label": "Buono",
    "summary": "Hai presentato bene il tuo percorso...",
    "criteria": [
      {
        "id": "comunicazione",
        "name": "Comunicazione",
        "score": 4,
        "weight": 0.2,
        "motivation": "Risposte chiare e ben strutturate...",
        "evidence": ["\"Nel progetto finale mi sono occupato di...\""],
        "suggestion": "Prova a chiudere ogni risposta con un risultato concreto."
      }
    ],
    "strengths": ["..."],
    "improvements": ["..."]
  }
}
```

---

## 5. Vocabolario condiviso

Le liste sono **chiuse**: la harness scarta o corregge qualsiasi valore che non è
qui. Il team AR/VR deve avere un'animazione/espressione per ogni voce (anche
riusando la stessa clip per più voci).

### 5.1 `emotion`

| Valore | Significato |
|---|---|
| `neutral` | Espressione di base, attenta |
| `friendly` | Sorriso leggero, accogliente (saluti) |
| `interested` | Sopracciglia leggermente alzate, coinvolto |
| `thoughtful` | Riflessivo, sta valutando la risposta |
| `skeptical` | Leggero dubbio (risposta vaga, va approfondita) |
| `pleased` | Soddisfatto di una buona risposta |
| `serious` | Professionale, concentrato (domande tecniche) |

### 5.2 `gesture`

| Valore | Significato |
|---|---|
| `none` | Nessun gesto, solo idle/parlato |
| `nod` | Annuisce |
| `wave` | Saluto con la mano |
| `handshake` | Porge la mano (accoglienza/chiusura) |
| `hand_open` | Mano aperta, gesto esplicativo mentre parla |
| `lean_forward` | Si sporge in avanti, interesse |
| `lean_back` | Si appoggia allo schienale, ascolto rilassato |
| `take_notes` | Prende appunti |
| `think_chin` | Mano al mento, riflette |

### 5.3 Stati gestiti solo da Unity

Questi stati non arrivano dalla harness: li decide Unity in base al flusso.

| Stato | Quando |
|---|---|
| `idle` | Nessuno parla |
| `listening` | Lo studente sta parlando (cenni leggeri, sguardo sullo studente) |
| `waiting` | In attesa della risposta di Claude (es. `take_notes` o `thoughtful` in loop) |
| `speaking` | Durante il TTS del campo `speech` |

---

## 6. Punti aperti da decidere insieme

- [ ] **Voce (TTS)**: quale servizio sintetizza `speech`? Da chi è gestito (Unity)?
- [ ] **Riconoscimento vocale (STT)** dello studente o solo input da tastiera?
- [ ] **Tempi**: la risposta di Claude impiega qualche secondo — l'avatar usa lo stato `waiting`.
- [ ] **Come la libreria C# della harness entra in Unity** (DLL in `Assets/Plugins` o sorgenti).
- [ ] Servono altre emozioni/gesti? Aggiungerli qui **prima** di usarli.
