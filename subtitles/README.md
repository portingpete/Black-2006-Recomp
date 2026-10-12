# BLACK cutscene captions

This folder contains English and Brazilian Portuguese subtitle tracks for nine spoken cutscenes: `03_N`, `05_N`, `07_N`, `09_N`, `11_N`, `13_N`, `15_N`, `17_N`, and `18_N`. The launcher language selects English (`subtitles/<stem>.srt`) or Brazilian Portuguese (`subtitles/pt-BR/<stem>.srt`). The runtime only loads these allowlisted stems while their matching XMV is active.

The wording and speech intervals come from the process audio capture in `captures/level0-difficulty-intro-20261009-235725.wav`. Cue times are relative to the movie session. The launcher log places the movie start roughly 1.5 seconds after the capture trigger, so that offset was subtracted from the capture times. This is an initial alignment estimate; the next in-game playback should be used to make any final timing adjustment.

The English transcript is uncertain on a few names, especially the line rendered as “Gunfire.” The Portuguese track translates that cue as “Tiros.” Names such as Kellar, Valencia, Ruben, Yev Dejanka, and Seventh Wave are retained as heard in the English transcript and should be checked during playback.
