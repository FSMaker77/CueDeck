# Changelog

Tutte le modifiche rilevanti di CueDeck.

## 1.0.0 — 2026-11-08

### Aggiunto

- Montaggio nativo dei CD dati `.cue`/`.bin` tramite conversione ISO in cache e API Virtual Disk di Windows.
- Supporto a più unità CD virtuali contemporanee.
- Supporto `.iso`, BIN standalone e CUE multi-file.
- Parser CUE con tracce `MODE1/2352`, `MODE2/2352`, `MODE1/2048`, `MODE2/2048`, `MODE2/2336`, `CDI/2352`, indici 00/01 e metadati.
- Libreria con drag & drop, ricerca, filtri, preferiti, ordinamento, rinomina e copertine.
- Player CD audio integrato con estrazione WAV temporanea.
- Esportazione di tutte le tracce audio in WAV.
- Toast, menu contestuali scuri, tray, single instance e apertura da riga di comando.
- Cache ISO gestita e cartella dati configurabile dall'app.
- Integrazione opzionale con WinCDEmu per CD audio, tracce audio e modalità completa.
- Icona applicazione e manifest DPI/long path.
- Test del motore con mount nativo reale e verifica byte-per-byte della conversione ISO.

### Limiti noti

- Il montaggio nativo espone solo la traccia dati.
- I CD audio puri e le tracce audio dei CD misti richiedono WinCDEmu per il montaggio come unità virtuale.
- I formati audio esterni MP3/AIFF/MOTOROLA nei CUE non sono supportati dal player integrato.
- L'eseguibile non è firmato digitalmente.
