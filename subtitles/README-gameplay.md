# Custom gameplay subtitles / Legendas das falas

## English

1. Copy `templates/gameplay.en.csv` to the folder for your language, naming it `gameplay.csv`.
2. Translate only the `caption` column. Keep the header and the first five numeric columns unchanged: they identify and time the original audio.
3. Save as UTF-8 CSV, select that language in the launcher, then start/restart the game. No compilation is required for translation files.

| Launcher language | Translation file, relative to the game installation |
| --- | --- |
| English | `subtitles/en/gameplay.csv` |
| Português brasileiro | `subtitles/pt-BR/gameplay.csv` |
| Italiano | `subtitles/it/gameplay.csv` |
| Français | `subtitles/fr/gameplay.csv` |
| Deutsch | `subtitles/de/gameplay.csv` |
| Español | `subtitles/es/gameplay.csv` |

For example, an Italian translator copies the English template to `subtitles/it/gameplay.csv`, translates its captions and selects **Italiano** in the launcher.

CSV files may use commas or semicolons. Keep a complete caption in one spreadsheet cell/record; quote it when it contains the separator, and double any quotes inside a quoted cell. Normal spreadsheet CSV export does this automatically. UTF-8 with or without a BOM is supported. Quoted numeric fields are supported too.

Existing tab-separated `gameplay.tsv` files are also supported. The selected folder's `gameplay.csv` takes priority over `gameplay.tsv`. If neither exists, the English gameplay file is used. Other language files are not automatically translated.

Do not add manual line breaks to a caption. The game divides complete words into pages of at most 42 characters per line and two lines total, and retains the final page until the audio ends or is stopped. Timing within a long caption is proportional to text length, not individual word timestamps. Each caption may contain up to 511 UTF-16 units, and each uninterrupted word must fit within 42 characters. Unsupported/invalid rows are skipped and counted in the `[SPEECH SUB]` log entry.

The supplied template covers 525 mission dialogue clips and the opening radio montage during "Four days earlier" (526 records). It does not add translations for the global speech bank or modify cinematic `.srt` files.

The opening radio montage is one 17.464-second audio track. Its caption ends at that measured duration (excluding time paused), even when the ambient stream object remains allocated after playback. Native interruption still removes it earlier.

## Português

Copie `templates/gameplay.en.csv` para a pasta do idioma desejado, com o nome `gameplay.csv`. Traduza somente a coluna **caption**, mantenha os cinco campos numéricos e salve em **CSV UTF-8**. Depois selecione o idioma no launcher e reabra o jogo.

Exemplo italiano: `subtitles/it/gameplay.csv` + **Italiano** no menu. Espanhol usa `es`, francês `fr` e alemão `de`.

Também pode copiar os arquivos `.tsv` existentes, mantendo essa extensão. CSV tem preferência sobre TSV; sem arquivo no idioma escolhido, o jogo usa inglês. A quebra em trechos e o encerramento pelo áudio continuam automáticos. O limite é de duas linhas com até 42 caracteres cada; não precisa quebrar as linhas manualmente.
