# Changelog

Tutte le modifiche importanti a Monitor Deck sono elencate qui.
Il formato segue [Keep a Changelog](https://keepachangelog.com/it-IT/1.1.0/) e le versioni seguono il [Semantic Versioning](https://semver.org/lang/it/).

Ogni release pubblicata su GitHub usa come note la sezione della sua versione: è anche il testo che l'app mostra quando propone un aggiornamento.

## [Non rilasciato]

## [2.1.0] - 2026-11-10

### Aggiunto
- **Rinominare gli ingressi**: tasto destro sul pulsante → "Rinomina". Utile quando il monitor dà alla porta il nome sbagliato, come certi LG che chiamano "DVI" l'ingresso HDMI, o per scrivere direttamente "PS5" o "Portatile". Il nome vale per quel monitor, resta salvato e compare anche nel menu dell'area di notifica; "Torna al nome originale" lo annulla. Le scorciatoie con il nome originale continuano a funzionare.
- **Rinominare i monitor**: clic sul nome del monitor → "Rinomina". Il nuovo nome compare nella finestra, nell'elenco dei monitor e nell'indicatore a schermo. Due monitor dello stesso modello possono avere nomi diversi.

## [2.0.2] - 2026-11-06

### Aggiunto
- **Contrasto nella pagina principale**, sotto la luminosità.
- **Nascondere gli ingressi che il monitor non ha**: tasto destro sul pulsante → "Nascondi". Alcuni monitor, per esempio diversi LG, dichiarano anche porte che non esistono. La scelta vale per quel monitor e resta salvata; "Mostra tutti gli ingressi" (sempre col tasto destro) li riporta. Accanto a "Ingresso" si vede quanti sono nascosti.

### Corretto
- **Regolazioni perse trascinando le leve**: a volte la leva restava, per esempio, a 0 mentre il monitor no. Ora tra un comando DDC/CI e il successivo c'è sempre la pausa minima richiesta dallo standard. Inoltre, quando smetti di trascinare, l'app rilegge il valore dal monitor e, se non coincide, lo rimanda (fino a tre volte). Se il monitor continua a rifiutarlo, la leva si sposta sul valore reale e compare un avviso.
- Il tasto destro sui pulsanti non attiva più il comando del pulsante.

## [2.0.1] - 2026-11-05

### Corretto
- Gli ingressi con codici fuori standard non compaiono più come "Ingresso 19": le porte vengono numerate per tipo. Ad esempio, su Alienware AW3423DWF il codice 0x13 diventa "HDMI 2"; una porta sola resta "HDMI" o "VGA".
- Ingressi in ordine: DisplayPort, poi HDMI 1, HDMI 2…

### Modificato
- I codici del produttore (E0–FF) sono nascosti: i produttori non ne pubblicano il significato e su alcuni monitor spengono lo schermo. Si mostrano con il pulsante in fondo a "Tutti i controlli" o dal menu, insieme a un avviso.
- Le scorciatoie per l'ingresso accettano il nome (`ingresso:HDMI 2`) e `ingresso:successivo`. Le scorciatoie predefinite create dalla 2.0.0 vengono convertite da sole.
- Il file `.ini` nuovo non impone più nomi di ingressi e profili: arrivano dal monitor.

### Aggiunto
- Sezione `[Nomi]` (o `[Nomi NomeMonitor]`) nel file `.ini` per dare un nome a qualsiasi comando, utile per i codici del produttore.

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

[Non rilasciato]: https://github.com/LoLler111/MonitorDeck/compare/v2.1.0...HEAD
[2.1.0]: https://github.com/LoLler111/MonitorDeck/compare/v2.0.2...v2.1.0
[2.0.2]: https://github.com/LoLler111/MonitorDeck/compare/v2.0.1...v2.0.2
[2.0.1]: https://github.com/LoLler111/MonitorDeck/compare/v2.0.0...v2.0.1
[2.0.0]: https://github.com/LoLler111/MonitorDeck/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/LoLler111/MonitorDeck/releases/tag/v1.0.0
