# Black 2006 Recomp

An experimental native Windows recompilation of the original Xbox release of
**BLACK (2006)**, built with XboxRecomp. It includes a Direct3D 11 renderer,
keyboard and mouse controls, XInput controller support, video settings, a PC
launcher, and native audio output. Movies are enabled by default.

Launch through the first mission has been tested, including the opening movies,
mission briefing, movement, shooting, and reloading with audio. This is still a
work in progress: later missions are unverified, crashes and rendering issues
remain possible, and Xbox GP/EP audio effects are not fully emulated.

Gameplay voices and effects now honor their source sample rate at the 48 kHz
audio output. This corrects 32 kHz speech playing about 50% too fast and high.
The [APU regression test](tests/apu_pitch/README.md) checks pitch, duration,
stream completion, and playback lifecycle without game files or an audio device.

This repository contains host source and build tooling. **You must supply your
own original Xbox game files.** Game assets, the XBE, generated game-function
bodies, recordings, and prebuilt executables are excluded.

## Build

Requirements:

- Windows x64, an AVX2-capable CPU, and a Direct3D 11 graphics device.
- Visual Studio 2022 with **Desktop development with C++** and a Windows SDK.
- Git, CMake 3.20 or newer, and Python 3.10 or newer (`py -3`).
- The supported retail original Xbox release of BLACK, extracted into `game/`.

The supported `game/default.xbe` has SHA-256:

```text
DF2739C372D254A90AEECCF5971D12097F22D89FBB90DB021FDA96DD4DEB68F6
```

Keep the extracted disc layout intact, including `GlobData.bin`, `levels/`,
`sound/`, and `videos/`. Other releases and the PlayStation 2 version are not
supported by this build.

From PowerShell in this repository:

```powershell
.\scripts\setup.ps1
.\'BLACK PC Launcher.exe'
```

Setup fetches a pinned XboxRecomp revision, applies the included runtime patch,
creates a local Python environment, generates code from your XBE, and builds
the game and launcher. These dependencies and outputs stay outside Git.
Building the generated C code can take several minutes.

Use the launcher to configure controls and video settings, then choose **Play**.
Leave **Skip movies** and **Advance startup menus automatically** unchecked to
watch the full opening and navigate manually. Local settings live in `local/`,
saves in `save/`, and diagnostic logs in `reports/`. Close the game before
removing the project folder; retain `save/` if you want to keep your progress.

## Credits and licensing

Built on [XboxRecomp](https://github.com/sp00nznet/xboxrecomp) by sp00nz, with
xemu-derived hardware components and nv2a_vsh_cpu. The original Xbox behavior
was investigated using community hardware documentation, including
[XboxDevWiki](https://xboxdevwiki.net/) and Cxbx-Reloaded references.
Development included assistance from Claude Code and OpenAI Codex.

Original project code is MIT licensed. Third-party components and their
modifications retain their respective licenses; see [LICENSE](LICENSE),
[NOTICE](NOTICE), and [LICENSES](LICENSES). BLACK belongs to its respective
owners; this project is an unofficial community effort.
