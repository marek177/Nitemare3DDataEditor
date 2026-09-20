# Nitemare 3D DAT / FLI Editor

Editor pre `UIF.DAT`, `SND.DAT` a `ENDING.FLI` z Nitemare 3D. Projekt cieli na .NET 8 WinForms a Windows 10/11.

## Funkcie

- načítanie DAT tabuľky `uint16 length + uint32 offset` a zachovanie pevného počtu slotov;
- zobrazenie slotov, offsetov, dĺžok a SHA-1 kontrolného odtlačku;
- UIF: náhľad a export 256-farebného 320×200 PCX do PNG, import PNG/BMP/JPG späť do PCX;
- SND: rozpoznanie MIDI (`MThd`), export MIDI, export 8-bit PCM ako WAV (10989 Hz mono), import PCM WAV späť;
- RAW/IBK a neznáme položky sa exportujú a importujú bez interpretácie;
- pri zápise sa deduplikujú rovnaké payloady a neznáme sloty sa nemenia obsahovo;
- dĺžka jednej položky je limitovaná formátom na 65535 bajtov.
- FLI: načítanie 320×200/8-bit animácie, dekódovanie palette/LC/BRUN/COPY/BLACK chunkov, náhľad všetkých snímkov;
- export ľubovoľného FLI snímku do PNG a import PNG do vybraného snímku;
- zápis upraveného FLI s pôvodnými nezmenenými frame chunkmi a novým COPY chunkom iba pre zmenené snímky.

## Kompilácia na Windows 11

Najjednoduchšie je spustiť pribalený skript:

```bat
build-all-windows.bat
```

Samostatne:

```bat
build-win-x86.bat
build-win-x64.bat
```

Skripty používajú .NET 8 SDK, konfiguráciu `Release`, `self-contained true` a
`PublishSingleFile=true`. Výsledok je v:

- `bin\publish\win-x86\Nitemare3DDataEditor.exe`
- `bin\publish\win-x64\Nitemare3DDataEditor.exe`

Skripty nekopírujú herné dáta do systémových priečinkov. `UIF.DAT`, `SND.DAT`,
`ENDING.FLI`, `MAP.*`, `IMG.*` a ďalšie súbory sa otvárajú cez menu aplikácie.

Spustiteľný súbor bude v `bin/Release/net8.0-windows/win-x64/publish/`.

Referenčný formát DAT a rozdelenie SND slotov zodpovedá loaderu OpenNitemare3D: UIF obsahuje paletové PCX; SND obsahuje MIDI, rezervované sloty a zvuky od slotu 34. Aplikácia si nevymýšľa kompresiu ani neprepisuje neznáme formáty.

`ENDING.FLI` je štandardná Autodesk FLI animácia (hlavička `0xAF11`); editor zachováva pôvodné delta snímky a pri úprave použije podporovaný nekomprimovaný COPY frame.

