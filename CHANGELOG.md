# Changelog

Tutte le modifiche importanti a Monitor Deck sono elencate qui.
Il formato segue [Keep a Changelog](https://keepachangelog.com/it-IT/1.1.0/) e le versioni seguono il [Semantic Versioning](https://semver.org/lang/it/).

Ogni release pubblicata su GitHub usa come note la sezione della sua versione: è anche il testo che l'app mostra quando propone un aggiornamento.

## [Non rilasciato]

## [2.0.0] - 2026-11-05

### Aggiunto
- **Finestra principale** compatta, oltre all'icona nell'area di notifica: si trascina dall'intestazione, si riapre dove l'avevi lasciata (anche allargata o compatta), si riduce a icona e può restare sempre in primo piano.
- **Tutti i monitor collegati**, uno alla volta: si cambia con le frecce, cliccando sul nome o con Ctrl+Tab. Il monitor scelto viene evidenziato a schermo.
- Nomi reali dei monitor (come in Impostazioni di Windows), risoluzione e monitor principale.
- **Comandi principali** in vista: ingresso, luminosità e volume, con pulsante per disattivare l'audio.
- **Tutti i controlli**: un pulsante allarga la finestra e mostra tutto quello che il monitor supporta (contrasto, modalità immagine, profili colore, guadagni e livelli del nero RGB, nitidezza, lingua del menu, alimentazione, ripristini, firmware, codici del produttore…), letto dalla stringa delle capacità DDC/CI.
- Ingressi letti direttamente dal monitor, con nomi personalizzabili anche per singolo monitor (`[Ingressi NomeMonitor]` nel file `.ini`).
- **Aggiornamenti automatici** dalle release GitHub: avviso nella finestra e nell'area di notifica, note della versione, download con verifica SHA-256 e riavvio automatico.
- Nuove scorciatoie: `volume:+5`, `muto`, `monitor:successivo`, `monitor:2`, `finestra`.
- Conferma prima dei comandi irreversibili (ripristino di fabbrica, spegnimento del monitor).
- Rilevamento automatico quando colleghi o scolleghi un monitor.
- Menu scuri coerenti con lo stile dell'app.
- Un secondo avvio dell'exe riporta in primo piano la finestra già aperta.

### Modificato
- Il clic sull'icona nell'area di notifica apre e chiude la finestra (prima apriva un pannello a comparsa).
- Il pulsante di chiusura nasconde l'app nell'area di notifica (disattivabile dal menu).
- "Avvia con Windows" ora parte ridotta nell'area di notifica.
- Il codice è diviso in più file nella cartella `src/`.

### Rimosso
- La modalità "finestra fissa": ora la finestra principale fa lo stesso lavoro.

## [1.0.0] - 2026-11-05

### Aggiunto
- Prima versione: pannello nell'area di notifica con ingresso, luminosità, contrasto e profili colore via DDC/CI.
- Scorciatoie globali configurabili e indicatore a schermo.
- Impostazioni in `MonitorDeck.ini` e avvio con Windows.

[Non rilasciato]: https://github.com/LoLler111/MonitorDeck/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/LoLler111/MonitorDeck/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/LoLler111/MonitorDeck/releases/tag/v1.0.0
