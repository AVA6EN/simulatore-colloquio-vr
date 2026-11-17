# Flusso di lavoro Git

La consegna valuta l'uso di Git e il contributo individuale visibile dalla cronologia.
Regole del gruppo:

## Branch

- `main` contiene sempre una versione funzionante. **Non si lavora direttamente su `main`.**
- Un branch per ogni funzionalità, con prefisso per area:
  - `harness/<cosa>` — es. `harness/profili-recruiter`, `harness/test-feedback`
  - `unity/<cosa>` — es. `unity/animazioni-viso`, `unity/ambiente-ufficio`
  - `docs/<cosa>` — es. `docs/formato-scambio-v0.2`

```bash
git switch main
```

```bash
git pull
```

```bash
git switch -c harness/nome-funzionalita
```

## Commit

- Frequenti e piccoli, ognuno con un solo scopo.
- Messaggio in italiano, all'imperativo, che dice **cosa** e **perché**:
  `Aggiunge la fase di approfondimento sui punti deboli`
  e non `modifiche` o `fix`.

## Pull request

1. Prima di aprire la PR aggiorna il branch con il lavoro dei compagni:

   ```bash
   git fetch origin
   ```

   ```bash
   git merge origin/main
   ```

   e risolvi gli eventuali conflitti.
2. Per la harness: `dotnet test harness/InterviewHarness.sln` deve passare. Se hai cambiato prompt
   o regole, riporta nella PR il risultato di `eval` prima e dopo la modifica.
3. Apri la PR verso `main` con una descrizione di cosa cambia e come provarlo.
4. **Almeno un altro membro del gruppo** la revisiona e approva prima del merge.
5. Le modifiche a `docs/formato-scambio.md` vanno approvate da un membro GEN AI **e** da uno AR/VR.

## Asset grandi

Modelli, texture, audio e video passano da **Git LFS** (regole già in `.gitattributes`).
Ognuno, una volta sola sul proprio PC:

```bash
git lfs install
```
