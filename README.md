# KaraokeForge v0.4 — All-in-One Karaoke Video Creator (Windows)

Free, no watermarks, no accounts, no upload. Your song stays on your PC.

```
song file on your PC
   → instrumental (AI separation if available)
   → lyrics (LRCLIB, free, no API key)
   → word-level AI timing (whisper.cpp)
   → karaoke MP4 with word-highlighting lyrics
```

---

## 1. What you get

| Your request | How v0.4 handles it |
|---|---|
| **"I will only upload song from pc"** | Songs are read **only** from your PC — *Load song* button, drag & drop, or the recent-files list. No URL box, nothing uploaded. The only network call is the text-only lyrics lookup. |
| **"Balance all fully automated or less semi auto"** | One button **MAKE KARAOKE VIDEO** runs everything by itself (including *writing* the lyrics when the song is not in LRCLIB). Every stage also has its own button, the lyrics editor stays editable, and the AI can be skipped or re-run at will. |
| **"Finally I need the video of karaoke with lyrics"** | An MP4 where the lyrics **highlight word by word in time with the singing**, with the next line waiting underneath, over your background image/video or an auto-generated gradient. |

### New in v0.4

* **Professional karaoke look** — the current line *and* the upcoming line are drawn (the next one smaller and dimmer), lines fade in and out, the text can be boxed, and the whole lyric block can sit at the bottom, middle or top of the frame.
* **Style presets** — *Classic, Neon, Sunset, Mono, Pop* set colours, font and size in one click.
* **Better vocal removal** — KaraokeForge detects **Demucs** on your PC and uses it automatically for AI-quality separation; otherwise it falls back to centre-channel cancellation.
* **Smarter AI** — whisper.cpp can run with a **VAD model** (so it stops inventing words during instrumental breaks) and with token-level JSON output. If your whisper.cpp build does not know a flag, KaraokeForge retries without it instead of failing.
* **Volume normalisation** (loudness-normalised audio), and the generated **word-timed `.lrc`** is saved next to the video.
* **Quick preview** — render the first 20 seconds to check the look before committing to a full render.
* **Setup check** at startup, a **log pane** (also written to `%LOCALAPPDATA%\KaraokeForge\karaokeforge.log`), **recent files**, and keyboard shortcuts.

### Automatic vs. manual

| | Automatic (one click) | Manual (step by step) |
|---|---|---|
| Lyrics | LRCLIB → if missing, the AI writes them | Fetch / paste / type yourself |
| Timing | AI word timing (whisper.cpp) | Editor timestamps, or AI on demand |
| Vocals | Demucs when installed, else centre cancellation | You choose the mode |
| Background | Auto gradient | Your own image or video |
| Output | Saved next to your song as `<track> (karaoke).mp4` | You pick the file in the save dialog |

---

## 2. Install the dependencies (Windows 10/11)

```powershell
winget install Microsoft.DotNet.SDK.8
winget install Gyan.FFmpeg -e
```

Close and reopen the terminal afterwards, then check:

```powershell
dotnet --version      # 8.x
ffmpeg -version       # must print a version
ffprobe -version      # ffprobe must be there too (it ships with FFmpeg)
```

KaraokeForge checks this for you at startup — the label next to **Setup help**
turns green when FFmpeg is ready.

### Optional but recommended — Whisper AI (this is what makes the timing exact)

1. Download **whisper.cpp** Windows binaries:
   <https://github.com/ggml-org/whisper.cpp/releases> → asset `whisper-bin-x64.zip` → extract `whisper-cli.exe` anywhere.
2. Download a model from <https://huggingface.co/ggml-org/whisper.cpp>:
   * `ggml-base.bin` — fast, good enough
   * `ggml-small.bin` / `ggml-medium.bin` — noticeably better on sung vocals (slower, more RAM)
3. Optional: a **VAD model** (`ggml-silero-v5.1.2.bin`) — the AI then ignores
   instrumental breaks instead of inventing words there.
4. In the app: group 2 → point at `whisper-cli.exe`, the `.bin` model and (optionally) the VAD model.
5. Pick the **language** if you do not want automatic detection.

Without Whisper the app still works: LRCLIB's synced lyrics are used and the highlight is
distributed evenly across each line. With Whisper you get true word-by-word highlighting.

### Optional — AI vocal separation (Demucs)

```powershell
pip install demucs
```

KaraokeForge finds it automatically and uses it for the instrumental. It is much better
than centre-channel cancellation, but it needs a few minutes per song (a CUDA GPU helps).

---

## 3. Build and run

### Option A — use the project files in this folder (fastest)

```powershell
cd KaraokeForge
dotnet run
```

### Option B — drop the single file into your own project

```powershell
mkdir KaraokeForge && cd KaraokeForge
dotnet new winforms -n KaraokeForge
# copy Program.cs over the generated Program.cs
dotnet run
```

### Standalone .exe for any other PC

```powershell
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

The result is a single `KaraokeForge.exe` (FFmpeg still has to be installed on the target PC).
`build.bat` does this for you; `build.bat publish` produces the single-file exe.

---

## 4. How to make the video

**Fully automatic**

1. Put your song in a folder on your PC.
2. Open KaraokeForge → drag the song onto the window (or **Load song…**).
   *Artist* and *Track* are guessed from a file name like `01 - Artist - Title.mp3` — correct them if needed.
3. Press **MAKE KARAOKE VIDEO (automatic)**.
4. Watch the log pane. The MP4 is saved **next to your song**:
   `Song Folder\Title (karaoke).mp4` (plus the word-timed `.lrc`). A dialog offers to open the folder.

**Semi-automatic / hand-corrected**

1. Same steps 1–2.
2. **Fetch lyrics (LRCLIB)** → the lyrics appear in the editor.
3. Optionally **AI auto-sync** → the AI rewrites every timestamp from the audio.
4. Fix any wording or timing in the editor by hand (LRC syntax below).
5. **Quick preview (20 s)** to check the look, then **Export karaoke video (MP4)**.

**Studio-quality vocals**

Use the drop-down *Vocal removal*:

| Mode | What it does |
|---|---|
| Auto (AI if available) | Demucs when installed, otherwise centre cancellation |
| Centre cancellation (fast) | Free, instant, slightly dulls centred instruments |
| Demucs (AI separation) | Forces the AI separator |
| Use my instrumental file | Uses the WAV you pick (e.g. exported from UVR5) |

**Keyboard:** `Ctrl+O` load · `Ctrl+L` lyrics · `Ctrl+R` automatic run · `Ctrl+E` export

---

## 5. Lyrics editor syntax

The editor understands both plain LRC and **enhanced (word-timed) LRC**:

```lrc
[00:12.34]Line-level lyric
[00:15.00]<00:15.00>Word <00:15.42>by <00:15.90>word <00:16.35>highlight
```

* `[mm:ss.xx]` = line start
* `<mm:ss.xx>` = the start of the word that follows it → this is what drives the karaoke highlight
* Lyrics produced by the AI use the word-timed form.
* **Spread evenly** rebuilds an evenly spaced skeleton from the words you typed.

---

## 6. Settings

| Setting | Meaning |
|---|---|
| Resolution | 1080p, 720p, 1080×1920 (vertical / Shorts), 1080×1080 (square), 1440p |
| Font / size | Arial, Verdana, Tahoma, Segoe UI, Impact, Trebuchet MS — size 24–200 |
| Offset (s) | Shift all lyrics later (+) or earlier (−) if the words are not in sync |
| Fade | How long a line fades in and out (ms) |
| Look | Classic / Neon / Sunset / Mono / Pop — sets colours, font and size |
| Lyrics position | Bottom, middle or top of the frame |
| Sung / Upcoming / Next line | Colour of the sung part, the not-yet-sung part and the line that comes next |
| Show the next line / Boxed text | The classic two-line karaoke look, or a solid box behind the text |
| Normalize volume | Loudness-normalises the audio for consistent volume |
| Save .lrc next to video | Keeps the word-timed lyrics as a separate file |

---

## 7. Troubleshooting

| Problem | Fix |
|---|---|
| "FFmpeg could not be started" / red label at the top | FFmpeg is not on PATH. Reinstall it and **open a new terminal**. |
| No lyrics found and no AI | Set up whisper.cpp (section 2), or paste the lyrics into the editor yourself. |
| Lyrics are late / early | Change *Offset (s)*, or re-run **AI auto-sync**. |
| Vocals still audible | Centre cancellation only removes centred vocals. Use Demucs or a UVR5 instrumental. |
| AI invents words in the breaks | Add a VAD model, or use a bigger Whisper model. |
| AI auto-sync is slow | Use `ggml-base.bin`, or a CUDA build of whisper.cpp. |
| Export is slow | Choose 1280×720, or switch the encoder preset to `fast` in `RenderOptions`. |
| Something went wrong | The log pane has the details; a copy is also in `%LOCALAPPDATA%\KaraokeForge\karaokeforge.log`. |

---

## 8. Notes & credits

* Lyrics: [LRCLIB](https://lrclib.net) — free, no API key.
* AI timing: [whisper.cpp](https://github.com/ggml-org/whisper.cpp) (MIT), models from Hugging Face.
* Vocal separation: Demucs (MIT) or FFmpeg centre-channel cancellation;
  [Ultimate Vocal Remover 5](https://ultimatevocalremover.com) for the best manual result.
* Encoding: libx264 + AAC in MP4, `+faststart` for smooth web playback.
* Fonts: the app uses the fonts already installed on your PC.
