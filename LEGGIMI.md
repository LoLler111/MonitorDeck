# Monitor Deck

Piccola app per l'area di notifica che controlla il monitor via DDC/CI: ingresso, luminosità, contrasto, profili colore e scorciatoie globali.

## Compilare
1. Metti `MonitorDeck.cs`, `app.ico` e `build.bat` nella stessa cartella.
2. Doppio clic su `build.bat`: usa il compilatore C# già presente in Windows (.NET Framework 4.x), non serve installare nulla.
3. Avvia `MonitorDeck.exe`. Al primo avvio crea `MonitorDeck.ini` accanto all'exe.

## Uso
- **Clic sull'icona** → pannello popup; si chiude cliccando altrove o con Esc.
- **Pulsante puntina** → finestra fissa: si trascina dall'intestazione (anche sul secondo schermo), ricorda la posizione e compare nella barra delle applicazioni. Il pulsante accanto attiva/disattiva "sempre in primo piano".
- **Nome del monitor** sotto il titolo → clic per passare al monitor successivo (se ne hai più di uno con DDC/CI).
- **Rotellina** sugli slider per regolazioni fini.
- **Menu tasto destro** sull'icona: ingresso rapido, finestra fissa, avvio con Windows, modifica/ricarica impostazioni.

## Scorciatoie predefinite
| Tasti | Azione |
|---|---|
| Ctrl+Alt+1 / 2 / 3 | DisplayPort / HDMI 1 / HDMI 2 |
| Ctrl+Alt+↑ / ↓ | Luminosità +10 / −10 |
| Ctrl+Alt+0 | Luminosità 0 |
| Ctrl+Alt+M | Apri/chiudi pannello |

Si cambiano in `MonitorDeck.ini`, sezione `[Scorciatoie]`.

## Note
- I nomi dei profili (codice VCP 14) sono provvisori: provali e rinominali nel file `.ini`.
- In modalità HDR il monitor blocca la luminosità: non è un limite dell'app.
- Se è attiva anche la vecchia app Alienware con i profili per applicazione, quella reimposterà la luminosità quando cambi app.
