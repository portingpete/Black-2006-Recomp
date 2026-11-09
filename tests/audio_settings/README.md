# Audio settings tests

These check `src/black_audio_settings.inc`, which reads and writes `local/audio.ini`, without game files or an audio
device: every slider value survives the game's float, a missing or damaged file changes nothing, a save replaces the
file whole, and only a change made on a sound page is written, once the sliders are still or the page is left.

```powershell
cmake -S tests/audio_settings -B build-audio-settings -A x64
cmake --build build-audio-settings --config Release
ctest --test-dir build-audio-settings -C Release --output-on-failure
```

Applying the volumes to the game is the `BLACK_AUDIO_SETTINGS` block in `src/black_native_menu.inc`, which needs the game
to run. `RECOMP_AUDIO_INI` points a test run at another file, and `RECOMP_BLACK_AUDIO_TRACE=1` logs each change of the
volumes and of the sound engine's copies.
