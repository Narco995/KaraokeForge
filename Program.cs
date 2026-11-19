//=====================================================================
//  KaraokeForge v0.4 - All-in-One Karaoke Video Creator for Windows
//  Single-file C# / .NET 8 WinForms application. Free, no watermarks.
//
//  WHAT v0.4 ADDS ON TOP OF v0.3
//  ------------------------------
//   * PROFESSIONAL KARAOKE LOOK. The subtitle renderer now draws the
//     current line AND the upcoming line (dimmer), fades lines in and
//     out, can box the text, and can move the whole lyric block to the
//     bottom / middle / top of the frame. Four ready-made style presets
//     (Classic, Neon, Sunset, Mono) set the colours and the font for you.
//   * BETTER VOCAL REMOVAL. KaraokeForge now looks for an AI separator
//     (Demucs) on your PC and uses it automatically when it finds one,
//     falling back to centre-channel cancellation when it does not.
//     Audio can also be loudness-normalised for consistent volume.
//   * SMARTER AI. whisper.cpp can now be run with a VAD model (so it does
//     not invent lyrics during instrumental breaks) and with token-level
//     JSON output; if the installed whisper.cpp build does not know a
//     flag, KaraokeForge retries without it instead of failing.
//   * A PROPER SETUP CHECK, a LOG PANE, RECENT FILES, keyboard
//     shortcuts, a 20 second QUICK PREVIEW and the option to save the
//     generated .lrc file next to the video.
//
//  WHAT v0.3 ADDED (still in place)
//  ---------------------------------
//   * SONGS COME FROM THIS PC ONLY. There is no upload, no URL input and
//     no network transfer of your music. Pick a file (or drag & drop it
//     onto the window) and everything happens locally.
//   * ONE-CLICK AUTOMATIC MODE. Press "MAKE KARAOKE VIDEO" and the app
//     runs the whole chain by itself:
//         song -> instrumental -> lyrics -> AI word timing -> MP4
//     Lyrics come from LRCLIB; if LRCLIB has no synced lyrics (or no
//     lyrics at all) Whisper transcribes the song and its timestamps
//     (and even its text) are used instead. Nothing is left for the user
//     to type.
//   * MANUAL OVERRIDES ARE STILL THERE. Every stage can be run on its own,
//     the lyrics editor stays editable, and the AI can be skipped, or
//     re-run at any time, on the songs where you want to hand-correct.
//   * REAL WORD-LEVEL KARAOKE. The AI pass produces a word-timed LRC
//     (<mm:ss.xx> word tags), so the exported MP4 highlights the lyrics
//     word by word in time with the singing - not just line by line.
//
//  PIPELINE
//    1. Load song from this PC (MP3/WAV/FLAC/M4A/OGG/AAC, or a video)
//    2. Extract audio to PCM WAV (FFmpeg)
//    3. Instrumental: Demucs / UVR5 WAV when available, otherwise
//       centre-channel cancellation
//    4. Lyrics: LRCLIB (free, no API key) -> synced / plain / none
//    5. AI auto-sync: whisper.cpp transcribes the song and rewrites every
//       timestamp (and supplies the text if LRCLIB had none)
//    6. Preview the instrumental / quick 20 s video preview
//    7. Export an MP4 with word-highlighting karaoke subtitles over your
//       own background image or video (or an auto-generated gradient)
//
//  HOW TO BUILD ON YOUR WINDOWS PC
//  -------------------------------
//  1. Install the .NET 8 SDK:        winget install Microsoft.DotNet.SDK.8
//  2. Install FFmpeg (adds ffmpeg/ffprobe to PATH):
//                                    winget install Gyan.FFmpeg -e
//     (close and reopen the terminal afterwards; verify with: ffmpeg -version)
//  3. Create and run the project:
//        mkdir KaraokeForge && cd KaraokeForge
//        dotnet new winforms -n KaraokeForge
//        (replace the generated Program.cs with this file)
//        dotnet run
//     ...or use the .csproj and README.md that ship next to this file:
//        dotnet run
//  4b. Optional - Whisper AI auto-sync (recommended, one-time setup):
//     Download the whisper.cpp Windows binary from
//        https://github.com/ggml-org/whisper.cpp/releases
//     (asset: whisper-bin-x64.zip; extract whisper-cli.exe anywhere)
//     Download a model from https://huggingface.co/ggml-org/whisper.cpp
//     (ggml-base.bin is a good start; ggml-small.bin / ggml-medium.bin
//     are notably more accurate on sung vocals but need more time/RAM)
//     Optional: a VAD model (ggml-silero-v5.1.2.bin) stops the AI from
//     inventing words during instrumental breaks.
//  5. Distribute as a standalone .exe for any Windows 10/11 PC:
//        dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
//
//  OPTIONAL (recommended for studio-quality vocal removal):
//  Download Ultimate Vocal Remover 5 (free, open source:
//  https://ultimatevocalremover.com ). Export the instrumental WAV for
//  any song in UVR5 and load that WAV in KaraokeForge.
//
//  ROADMAP (Phase 2 ideas):
//   - Real UVR5 model integration via ONNX Runtime
//   - Timeline drag-to-adjust word markers, microphone recording mode
//=====================================================================

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace KaraokeForge;

//=====================================================================
//  Program entry point
//=====================================================================

#region WINDOWS_UI
internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
#endregion

// ------------------------------------------------------------------
// Models
// ------------------------------------------------------------------

public record LyricWord(string Text, TimeSpan Start);

public class KaraokeLine
{
    public TimeSpan Start { get; set; }
    public List<LyricWord> Words { get; } = new();
    public bool WordTiming { get; set; }

    public string Text => string.Concat(Words.Select(w => w.Text)).Trim();
}

/// <summary>Everything the renderer needs to know for one job.</summary>
public class RenderOptions
{
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int FontSize { get; set; } = 72;
    public string FontName { get; set; } = "Arial";

    public string SungColor { get; set; } = "White";
    public string UnsungColor { get; set; } = "DeepSkyBlue";
    public string NextLineColor { get; set; } = "Silver";
    public bool ShowNextLine { get; set; } = true;
    public bool Boxed { get; set; }
    public string VerticalPosition { get; set; } = "Bottom";   // Bottom / Middle / Top
    public int FadeMs { get; set; } = 140;

    public double OffsetSeconds { get; set; }
    public int Fps { get; set; } = 30;
    public int Crf { get; set; } = 20;
    public string Preset { get; set; } = "medium";

    public bool NormalizeAudio { get; set; } = true;
    public bool SaveLrc { get; set; } = true;

    /// <summary>When &gt; 0 the render is limited to this many seconds
    /// (used by the quick preview).</summary>
    public int MaxSeconds { get; set; }
}

// ------------------------------------------------------------------
// Small shared helpers: paths, diagnostics log, tool detection
// ------------------------------------------------------------------

public static class AppPaths
{
    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KaraokeForge");

    public static string Work => Path.Combine(Root, "work");

    public static string RecentFile => Path.Combine(Root, "recent.txt");

    public static void EnsureFolders()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Work);
    }
}

/// <summary>Very small diagnostics sink: the UI shows these lines in the
/// log pane, and the last 200 lines are written to a file so a failed
/// render can be inspected after the fact.</summary>
public static class Diag
{
    private static readonly object Gate = new();
    private static readonly Queue<string> Lines = new();

    public static event Action<string>? Message;

    public static void Write(string text)
    {
        lock (Gate)
        {
            Lines.Enqueue(text);
            while (Lines.Count > 200) Lines.Dequeue();
        }
        try
        {
            AppPaths.EnsureFolders();
            var file = Path.Combine(AppPaths.Root, "karaokeforge.log");
            File.AppendAllText(file, $"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        }
        catch { /* logging must never break the app */ }
        Message?.Invoke(text);
    }

    public static void WriteError(string context, Exception ex) =>
        Write($"{context}: {ex.GetType().Name}: {ex.Message}");
}

// ------------------------------------------------------------------
// LRC parsing (line-level [mm:ss.xx] and enhanced word-level <mm:ss.xx>)
// ------------------------------------------------------------------

public static class LrcParser
{
    private static readonly Regex LineTag =
        new(@"^\[((?:\d+):(?:\d{1,2})(?:[.:]\d{1,3})?)\]", RegexOptions.Compiled);
    private static readonly Regex WordTag =
        new(@"<((?:\d+):(?:\d{1,2})(?:[.:]\d{1,3})?)>", RegexOptions.Compiled);

    public static List<KaraokeLine> Parse(string lrcText)
    {
        var lines = new List<KaraokeLine>();
        if (string.IsNullOrWhiteSpace(lrcText)) return lines;

        foreach (var raw in lrcText.Replace("\r\n", "\n").Split('\n'))
        {
            var s = raw.TrimStart();
            var times = new List<TimeSpan>();
            Match m;
            while ((m = LineTag.Match(s)).Success)
            {
                times.Add(ParseTag(m.Groups[1].Value));
                s = s[m.Length..];
            }
            if (times.Count == 0) continue; // skip metadata tags like [ar:...] etc.
            var text = s.Trim();

            var karaokeLine = new KaraokeLine { Start = times[0] };
            var wordMatches = WordTag.Matches(text);

            if (wordMatches.Count > 0)
            {
                karaokeLine.WordTiming = true;
                for (int i = 0; i < wordMatches.Count; i++)
                {
                    var wm = wordMatches[i];
                    var wordStart = ParseTag(wm.Groups[1].Value);
                    var textStart = wm.Index + wm.Length;
                    var textEnd = i + 1 < wordMatches.Count
                        ? wordMatches[i + 1].Index
                        : text.Length;
                    var wordText = text[textStart..textEnd];
                    if (wordText.Trim().Length > 0)
                        karaokeLine.Words.Add(new LyricWord(wordText, wordStart));
                }
            }
            else if (text.Length > 0)
            {
                // word timings unknown: give every word the line start;
                // the ASS generator distributes highlight timing evenly.
                foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    karaokeLine.Words.Add(new LyricWord(w + " ", karaokeLine.Start));
            }

            if (karaokeLine.Words.Count == 0)
                karaokeLine.Words.Add(new LyricWord("\u266a ", times[0])); // music break

            lines.Add(karaokeLine);
        }

        return lines.OrderBy(l => l.Start).ToList();
    }

    public static TimeSpan ParseTag(string tag)
    {
        var parts = tag.Split(':');
        double secs = double.Parse(parts[^1].Replace(',', '.'),
            CultureInfo.InvariantCulture);
        int mins = int.Parse(parts[^2], CultureInfo.InvariantCulture);
        var ts = TimeSpan.FromMinutes(mins) + TimeSpan.FromSeconds(secs);
        return parts.Length >= 3
            ? ts + TimeSpan.FromMinutes(double.Parse(parts[0], CultureInfo.InvariantCulture))
            : ts;
    }
}

// ------------------------------------------------------------------
// Lyrics lookup via LRCLIB (https://lrclib.net) - free, no API key
// ------------------------------------------------------------------

public record LyricsResult(string Artist, string Track, string LrcOrPlain, bool Synced);

public static class LyricsService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    static LyricsService() =>
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "KaraokeForge/0.4 (https://github.com/local/karaokeforge)");

    /// <summary>Best-effort lookup. Tries the exact-match endpoint first
    /// (it is the most accurate), then falls back to the search endpoint.</summary>
    public static async Task<LyricsResult?> SearchAsync(
        string artist, string track, TimeSpan? duration = null)
    {
        var found = await TryGetAsync(artist, track, duration);
        if (found is not null) return found;
        return await SearchFallbackAsync(artist, track);
    }

    private static async Task<LyricsResult?> TryGetAsync(
        string artist, string track, TimeSpan? duration)
    {
        var url = "https://lrclib.net/api/get?artist_name=" +
                  Uri.EscapeDataString(artist) +
                  "&track_name=" + Uri.EscapeDataString(track);
        if (duration is { } d && d.TotalSeconds > 0)
            url += "&duration=" + Math.Round(d.TotalSeconds).ToString(CultureInfo.InvariantCulture);

        try
        {
            using var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            return FromItem(doc.RootElement, preferSynced: true);
        }
        catch (Exception ex)
        {
            Diag.WriteError("LRCLIB /api/get", ex);
            return null;
        }
    }

    private static async Task<LyricsResult?> SearchFallbackAsync(string artist, string track)
    {
        var url = "https://lrclib.net/api/search?artist_name=" +
                  Uri.EscapeDataString(artist) +
                  "&track_name=" + Uri.EscapeDataString(track);
        try
        {
            using var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            LyricsResult? best = null;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var res = FromItem(item, preferSynced: true);
                if (res is not null && res.Synced) return res;   // synced wins
                if (res is not null && best is null) best = res; // keep plain as backup
            }
            return best;
        }
        catch (Exception ex)
        {
            Diag.WriteError("LRCLIB /api/search", ex);
            return null;
        }
    }

    private static LyricsResult? FromItem(JsonElement item, bool preferSynced)
    {
        string? synced = item.TryGetProperty("syncedLyrics", out var sl)
            && sl.ValueKind == JsonValueKind.String ? sl.GetString() : null;
        string? plain = item.TryGetProperty("plainLyrics", out var pl)
            && pl.ValueKind == JsonValueKind.String ? pl.GetString() : null;

        if (preferSynced && !string.IsNullOrWhiteSpace(synced))
            return new LyricsResult(Get(item, "artistName"), Get(item, "trackName"), synced!, true);
        if (!string.IsNullOrWhiteSpace(plain))
            return new LyricsResult(Get(item, "artistName"), Get(item, "trackName"), plain!, false);
        if (!string.IsNullOrWhiteSpace(synced))
            return new LyricsResult(Get(item, "artistName"), Get(item, "trackName"), synced!, true);
        return null;

        static string Get(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? "" : "";
    }

    /// <summary>Converts unsynced plain lyrics into a rough LRC skeleton
    /// (lines spread evenly across the song) that the user can fine-tune.</summary>
    public static string PlainToSkeleton(string plain, TimeSpan songLength)
    {
        var lyricLines = plain.Replace("\r\n", "\n").Split('\n',
            StringSplitOptions.RemoveEmptyEntries);
        if (lyricLines.Length == 0) return "";
        if (songLength <= TimeSpan.Zero) songLength = TimeSpan.FromMinutes(3.5);

        var sb = new StringBuilder();
        // start lyrics at 15s, finish 10s before the end
        var usable = (songLength - TimeSpan.FromSeconds(25)).TotalSeconds;
        if (usable <= 0) usable = 60;
        double step = usable / lyricLines.Length;
        for (int i = 0; i < lyricLines.Length; i++)
        {
            var t = TimeSpan.FromSeconds(15 + step * i);
            sb.Append('[').Append(FormatLrcTime(t)).Append(']')
              .Append(lyricLines[i]).Append('\n');
        }
        return sb.ToString();
    }

    public static string FormatLrcTime(TimeSpan t) =>
        $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}";
}

// ------------------------------------------------------------------
// FFmpeg helpers (audio extraction, vocal removal, background, MP4 export)
// ------------------------------------------------------------------

public static class FFmpeg
{
    /// <summary>True when ffmpeg and ffprobe are both callable.</summary>
    public static async Task<(bool ok, string detail)> CheckToolsAsync()
    {
        var ff = await VersionAsync("ffmpeg");
        var fp = await VersionAsync("ffprobe");
        if (!ff.ok) return (false, "FFmpeg was not found on PATH.");
        if (!fp.ok) return (false, "ffprobe was not found on PATH (it ships with FFmpeg).");
        return (true, ff.detail);
    }

    private static async Task<(bool ok, string detail)> VersionAsync(string tool)
    {
        try
        {
            var psi = new ProcessStartInfo(tool, "-version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return (false, "");
            var outText = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            var first = outText.Split('\n').FirstOrDefault()?.Trim() ?? "";
            return (true, first.Length > 0 ? first : "ok");
        }
        catch
        {
            return (false, "");
        }
    }

    /// <summary>Extract/convert any audio or video input to PCM WAV.</summary>
    public static async Task<(bool ok, string error)> ExtractWavAsync(
        string input, string outputWav)
    {
        return await RunAsync("-y", "-i", input, "-vn",
            "-ar", "44100", "-ac", "2", "-c:a", "pcm_s16le", outputWav);
    }

    /// <summary>Center-channel (lead vocal) cancellation: subtracts one
    /// stereo channel from the other, removing content panned to the
    /// center where lead vocals usually sit. Free and instant; for
    /// studio quality use Demucs or an instrumental exported from UVR5.</summary>
    public static async Task<(bool ok, string error)> RemoveVocalsAsync(
        string inputWav, string outputWav)
    {
        return await RunAsync("-y", "-i", inputWav,
            "-af", "pan=stereo|c0=c0-c1|c1=c1-c0", outputWav);
    }

    /// <summary>Generates a simple gradient background so a render never
    /// fails for the lack of a background image.</summary>
    public static async Task<(bool ok, string error)> GenerateBackgroundAsync(
        string outputPng, int width, int height)
    {
        return await RunAsync("-y",
            "-f", "lavfi",
            "-i", $"gradients=s={width}x{height}:c0=0x101a33:c1=0x2b4a7d:x0=0:y0=0:x1={width}:y1={height}:d=1",
            "-frames:v", "1", outputPng);
    }

    /// <summary>Song length in seconds (via ffprobe).</summary>
    public static async Task<TimeSpan> ProbeDurationAsync(string file)
    {
        try
        {
            var psi = new ProcessStartInfo("ffprobe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("quiet");
            psi.ArgumentList.Add("-show_entries"); psi.ArgumentList.Add("format=duration");
            psi.ArgumentList.Add("-of"); psi.ArgumentList.Add("default=noprint_wrappers=1:nokey=1");
            psi.ArgumentList.Add(file);
            using var proc = Process.Start(psi);
            if (proc is null) return TimeSpan.Zero;
            var outText = (await proc.StandardOutput.ReadToEndAsync()).Trim();
            await proc.WaitForExitAsync();
            if (double.TryParse(outText, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var secs) && secs > 0)
                return TimeSpan.FromSeconds(secs);
        }
        catch (Exception ex)
        {
            Diag.WriteError("ffprobe duration", ex);
        }
        return TimeSpan.Zero;
    }

    /// <summary>Renders the final karaoke video. Background may be an
    /// image (looped) or a video (looped until the audio ends).</summary>
    public static async Task<(bool ok, string error)> ExportVideoAsync(
        string background, string audioWav, string assFile, string outputMp4,
        RenderOptions opt, string workDir, Action<double>? onProgress = null)
    {
        bool bgIsVideo = Path.GetExtension(background)
            .ToLowerInvariant() is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm";

        // The ASS file is copied into the working folder and referenced by
        // relative name - that avoids Windows drive-letter escaping in the
        // filter graph (ass='C\:\\...' would break).
        const string assWorkingFile = "karaokeforge.ass";
        var assDest = Path.Combine(workDir, assWorkingFile);
        if (!string.Equals(Path.GetFullPath(assFile), Path.GetFullPath(assDest),
                StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(assFile, assDest, true);
        }

        var args = new List<string> { "-y" };
        if (bgIsVideo)
            args.AddRange(new[] { "-stream_loop", "-1", "-i", background });
        else
            args.AddRange(new[] { "-loop", "1", "-i", background });

        args.AddRange(new[]
        {
            "-i", audioWav,
            "-vf", $"fps={opt.Fps},ass={assWorkingFile}," +
                   $"scale={opt.Width}:{opt.Height}:force_original_aspect_ratio=decrease," +
                   $"pad={opt.Width}:{opt.Height}:(ow-iw)/2:(oh-ih)/2,format=yuv420p",
            "-c:v", "libx264", "-preset", opt.Preset,
            "-crf", opt.Crf.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuv420p", "-profile:v", "high", "-level", "4.1",
            "-c:a", "aac", "-b:a", "192k"
        });

        if (opt.NormalizeAudio)
            args.AddRange(new[] { "-af", "loudnorm=I=-14:TP=-1.5:LRA=11" });

        args.AddRange(new[] { "-movflags", "+faststart", "-shortest" });

        // "-shortest" is unreliable with a looped image input, so pin the
        // output length to the length of the song as well (-t must come
        // before the output file name to be treated as an output option)
        var audioLength = await ProbeDurationAsync(audioWav);
        if (audioLength > TimeSpan.Zero)
        {
            double seconds = audioLength.TotalSeconds;
            if (opt.MaxSeconds > 0) seconds = Math.Min(seconds, opt.MaxSeconds);
            args.AddRange(new[] { "-t", seconds.ToString("0.###", CultureInfo.InvariantCulture) });
        }
        args.Add(outputMp4);

        return await RunAsync(workDir, args.ToArray(), onProgress);
    }

    public static Task<(bool ok, string error)> RunAsync(params string[] args)
        => RunAsync(null, args);

    public static async Task<(bool ok, string error)> RunAsync(
        string? workDir, string[] args, Action<double>? onProgress = null)
    {
        var psi = new ProcessStartInfo("ffmpeg")
        {
            WorkingDirectory = workDir ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        Process? proc;
        try { proc = Process.Start(psi); }
        catch (Exception ex) { return (false, "FFmpeg could not be started: " + ex.Message); }
        if (proc is null) return (false, "FFmpeg could not be started. " +
            "Is it installed and on PATH?");

        var err = new StringBuilder();
        var tcs = new TaskCompletionSource();
        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await proc.StandardError.ReadLineAsync()) != null)
            {
                err.AppendLine(line);
                if (err.Length > 8000) err.Remove(0, 2000);
                if (onProgress is not null)
                {
                    var t = ParseFfmpegTime(line);
                    if (t is not null) onProgress(t.Value);
                }
            }
            tcs.TrySetResult();
        });
        await proc.WaitForExitAsync();
        await tcs.Task;

        var errorText = err.ToString();
        if (proc.ExitCode == 0) return (true, "");

        var message = "FFmpeg failed:\n" + Truncate(errorText, 1500) +
                      "\n\nCommand: ffmpeg " + string.Join(' ', args);
        Diag.Write(message);
        return (false, message);
    }

    /// <summary>Pulls the elapsed seconds out of an ffmpeg progress line.</summary>
    private static double? ParseFfmpegTime(string line)
    {
        var idx = line.IndexOf("time=", StringComparison.Ordinal);
        if (idx < 0) return null;
        var rest = line[(idx + 5)..].Trim();
        var sp = rest.IndexOf(' ');
        if (sp > 0) rest = rest[..sp];
        var parts = rest.Split(':');
        if (parts.Length < 2) return null;
        if (!double.TryParse(parts[^1], NumberStyles.Float,
                CultureInfo.InvariantCulture, out var sec)) return null;
        if (!double.TryParse(parts[^2], NumberStyles.Float,
                CultureInfo.InvariantCulture, out var min)) return null;
        double total = min * 60 + sec;
        if (parts.Length == 3 &&
            double.TryParse(parts[0], NumberStyles.Float,
                CultureInfo.InvariantCulture, out var hr)) total += hr * 3600;
        return total;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + " ...";
}

// ------------------------------------------------------------------
// AI vocal separation (Demucs) - optional, used automatically when found
// ------------------------------------------------------------------

public static class Demucs
{
    private static bool? _available;

    /// <summary>Looks for a Python with the demucs package installed.</summary>
    public static async Task<bool> IsAvailableAsync()
    {
        if (_available.HasValue) return _available.Value;
        foreach (var exe in new[] { "python", "python3", "py" })
        {
            var (ok, _) = await RunCaptureAsync(exe,
                "-c", "import demucs, sys; print('ok')");
            if (ok) { _available = true; return true; }
        }
        _available = false;
        return false;
    }

    /// <summary>Runs Demucs to separate the vocals. Returns the path of the
    /// resulting "no_vocals.wav" (the instrumental).</summary>
    public static async Task<(bool ok, string message, string instrumental)> SeparateAsync(
        string inputFile, string outputRoot, string model = "hdemucs")
    {
        var python = "python";
        foreach (var exe in new[] { "python", "python3", "py" })
        {
            var (ok, _) = await RunCaptureAsync(exe,
                "-c", "import demucs, sys; print('ok')");
            if (ok) { python = exe; break; }
        }

        Directory.CreateDirectory(outputRoot);
        var args = new[]
        {
            "-m", "demucs", "--two-stems=vocals", "-n", model,
            "-o", outputRoot, inputFile
        };

        var psi = new ProcessStartInfo(python)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return (false, "Could not start Python.", "");
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0)
                return (false, "Demucs failed:\n" +
                              (stderr.Length > 800 ? stderr[..800] : stderr), "");

            var produced = Directory
                .EnumerateFiles(outputRoot, "no_vocals.wav", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (produced is null)
                return (false, "Demucs finished but produced no instrumental file.\n" + stdout, "");
            return (true, "Demucs finished.", produced);
        }
        catch (Exception ex)
        {
            return (false, "Demucs could not be started: " + ex.Message, "");
        }
    }

    private static async Task<(bool ok, string output)> RunCaptureAsync(
        string exe, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi);
            if (proc is null) return (false, "");
            var outText = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return (proc.ExitCode == 0, outText.Trim());
        }
        catch
        {
            return (false, "");
        }
    }
}

// ------------------------------------------------------------------
// ASS subtitle generation (karaoke \k word highlighting)
// ------------------------------------------------------------------

public static class AssGenerator
{
    /// <summary>Named looks: (sung, upcoming, next line, font, size factor).</summary>
    public static readonly Dictionary<string, (string Sung, string Unsung, string Next,
        string Font, int Size)> Presets = new()
    {
        ["Classic"] = ("White", "DeepSkyBlue", "Silver", "Arial", 100),
        ["Neon"] = ("Black", "#00FF9C", "#7A7A7A", "Impact", 105),
        ["Sunset"] = ("Yellow", "OrangeRed", "#B08B57", "Verdana", 100),
        ["Mono"] = ("White", "Gray", "DarkGray", "Tahoma", 100),
        ["Pop"] = ("White", "Magenta", "LightPink", "Trebuchet MS", 105),
    };

    public static string Build(List<KaraokeLine> lines, RenderOptions opt)
    {
        var formatLine =
            "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, " +
            "OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, " +
            "ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, " +
            "MarginL, MarginR, MarginV, Encoding";

        // In ASS the part of the line that has NOT been sung yet is drawn in
        // SecondaryColour; the \k highlight then switches it to
        // PrimaryColour word by word. So: Primary = sung, Secondary = upcoming.
        var sung = ColourValue(opt.SungColor);
        var unsung = ColourValue(opt.UnsungColor);
        var nextColour = ColourValue(opt.NextLineColor);

        var borderStyle = opt.Boxed ? 3 : 1;      // 3 = opaque box
        var nextFontSize = Math.Max(24, (int)(opt.FontSize * 0.72));

        var sb = new StringBuilder();
        sb.Append("[Script Info]\nScriptType: v4.00+\n");
        sb.Append($"PlayResX: {opt.Width}\nPlayResY: {opt.Height}\n");
        sb.Append("WrapStyle: 0\nScaledBorderAndShadow: yes\n\n");
        sb.Append("[V4+ Styles]\n").Append(formatLine).Append('\n');
        // Style: Karaoke
        sb.Append("Style: Karaoke,").Append(opt.FontName).Append(',').Append(opt.FontSize)
          .Append(',').Append(sung).Append(',').Append(unsung)
          .Append(",&H00101010,&H90000000,-1,0,0,0,100,100,0,0,")
          .Append(borderStyle).Append(",4,1.5,2,60,60,0,1\n");
        // Style: Next (the line that comes after the current one)
        sb.Append("Style: Next,").Append(opt.FontName).Append(',').Append(nextFontSize)
          .Append(',').Append(nextColour).Append(',').Append(nextColour)
          .Append(",&H00202020,&H90000000,0,0,0,0,100,100,0,0,1,3,1,2,60,60,0,1\n\n");

        sb.Append("[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n");

        var offset = TimeSpan.FromSeconds(opt.OffsetSeconds);
        var ordered = lines
            .Select(l => (line: l, start: l.Start + offset > TimeSpan.Zero
                                        ? l.Start + offset : TimeSpan.Zero))
            .OrderBy(x => x.start)
            .ToList();

        // where the lyric block sits on the frame
        double scale = opt.Height / 1080.0;
        int blockOffset = opt.VerticalPosition switch
        {
            "Middle" => (int)(260 * scale),
            "Top" => (int)(520 * scale),
            _ => 0
        };
        int activeMargin = (int)((150 + blockOffset) * scale);
        int nextMargin = Math.Max(10, activeMargin - (int)(100 * scale));

        for (int i = 0; i < ordered.Count; i++)
        {
            var (line, start) = ordered[i];
            var end = i + 1 < ordered.Count
                ? ordered[i + 1].start
                : start + TimeSpan.FromSeconds(5);
            if (end <= start) end = start + TimeSpan.FromSeconds(2);

            var text = BuildKaraokeText(line, start, end, opt.FadeMs);
            sb.Append("Dialogue: 0,").Append(FormatAssTime(start)).Append(',')
              .Append(FormatAssTime(end)).Append(",Karaoke,,0,0,")
              .Append(activeMargin).Append(",,")
              .Append(text).Append('\n');

            if (opt.ShowNextLine && i + 1 < ordered.Count)
            {
                var nextText = Escape(ordered[i + 1].line.Text);
                sb.Append("Dialogue: 0,").Append(FormatAssTime(start)).Append(',')
                  .Append(FormatAssTime(end)).Append(",Next,,0,0,")
                  .Append(nextMargin).Append(",,")
                  .Append(Fade(opt.FadeMs)).Append(nextText).Append('\n');
            }
        }
        return sb.ToString();
    }

    private static string BuildKaraokeText(
        KaraokeLine line, TimeSpan start, TimeSpan end, int fadeMs)
    {
        double totalCs = Math.Max(20.0, (end - start).TotalMilliseconds / 10.0);
        var syllables = new List<(string text, double shareCs)>();
        int totalChars = line.Words.Sum(w => Math.Max(1, w.Text.TrimEnd().Length));

        for (int i = 0; i < line.Words.Count; i++)
        {
            var w = line.Words[i];
            double share;
            if (line.WordTiming)
            {
                var next = i + 1 < line.Words.Count
                    ? line.Words[i + 1].Start
                    : end;
                share = (next - w.Start).TotalMilliseconds / 10.0;
                if (share <= 0)
                    share = totalCs * Math.Max(1, w.Text.TrimEnd().Length) / totalChars;
            }
            else
            {
                share = totalCs * Math.Max(1, w.Text.TrimEnd().Length) / totalChars;
            }
            syllables.Add((Escape(w.Text.TrimEnd()), Math.Max(4, share)));
        }

        // normalize so durations sum to the line length (in centiseconds)
        double sum = syllables.Sum(s => s.shareCs);
        var sb = new StringBuilder();
        sb.Append(Fade(fadeMs)).Append("{\\k0}");
        foreach (var (text, shareCs) in syllables)
        {
            sb.Append("{\\k").Append(Math.Round(shareCs * totalCs / sum,
                MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture))
              .Append('}').Append(text).Append(' ');
        }
        return sb.ToString().TrimEnd();
    }

    private static string Fade(int ms) =>
        ms <= 0 ? "" : "{\\fad(" + ms + "," + ms + ")}";

    private static string Escape(string t) =>
        t.Replace("{", "(").Replace("}", ")").Replace("\n", " ");

    public static string FormatAssTime(TimeSpan t)
    {
        int cents = t.Milliseconds / 10;
        return $"{(int)t.TotalHours:0}:{t.Minutes:00}:{t.Seconds:00}.{cents:00}";
    }

    /// <summary>Colour name (or #RRGGBB) -> ASS &amp;HAABBGGRR value.</summary>
    private static string ColourValue(string name)
    {
        var (r, g, b) = ToRgb(name);
        // ASS stores alpha + blue + green + red; alpha 00 = fully opaque
        int ass = (b << 16) | (g << 8) | r;
        return "&H00" + (ass & 0xFFFFFF).ToString("X6") + "&";
    }

    private static readonly Dictionary<string, (int R, int G, int B)> Palette = new()
    {
        ["White"] = (255, 255, 255),
        ["Black"] = (0, 0, 0),
        ["Gray"] = (128, 128, 128),
        ["DarkGray"] = (169, 169, 169),
        ["LightGray"] = (211, 211, 211),
        ["Silver"] = (192, 192, 192),
        ["Yellow"] = (255, 255, 0),
        ["Gold"] = (255, 215, 0),
        ["Orange"] = (255, 165, 0),
        ["OrangeRed"] = (255, 69, 0),
        ["Red"] = (255, 0, 0),
        ["Pink"] = (255, 192, 203),
        ["HotPink"] = (255, 105, 180),
        ["LightPink"] = (255, 182, 193),
        ["Magenta"] = (255, 0, 255),
        ["Purple"] = (160, 32, 240),
        ["DeepSkyBlue"] = (0, 191, 255),
        ["SkyBlue"] = (135, 206, 235),
        ["LightCyan"] = (224, 255, 255),
        ["Cyan"] = (0, 255, 255),
        ["LimeGreen"] = (50, 205, 50),
        ["Green"] = (0, 128, 0),
    };

    private static (int R, int G, int B) ToRgb(string name)
    {
        name = (name ?? "").Trim();
        if (name.StartsWith('#'))
        {
            var hex = name[1..];
            if (hex.Length == 6 &&
                int.TryParse(hex[..2], NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var r) &&
                int.TryParse(hex[2..4], NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var g) &&
                int.TryParse(hex[4..6], NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var b))
                return (r, g, b);
        }
        return Palette.TryGetValue(name, out var c) ? c : (255, 255, 255);
    }
}

// ------------------------------------------------------------------
// Whisper AI auto-sync: transcribe the song with whisper.cpp, then
// align the lyric lines to the detected words and rewrite timestamps
// (like LRCGen / karaoke-gen do integrate: Whisper + timeline editing)
// ------------------------------------------------------------------

public record WhisperWord(string Text, TimeSpan Start, TimeSpan End);

public static class WhisperSync
{
    private static readonly Regex StripTags =
        new(@"\[[^\]]*\]|<[^>]*>", RegexOptions.Compiled);

    /// <summary>Converts the song to 16 kHz mono WAV, runs whisper-cli and
    /// parses the JSON it produces. Optional flags (VAD model, full JSON)
    /// are dropped automatically if the installed build does not know them.</summary>
    public static async Task<List<WhisperWord>> TranscribeAsync(
        string exePath, string modelPath, string audioFile, string workDir,
        string language = "auto", string vadModel = "", bool fullJson = true)
    {
        Directory.CreateDirectory(workDir);
        var wav16k = Path.Combine(workDir, "whisper_in.wav");
        var (ok, err) = await FFmpeg.RunAsync("-y", "-i", audioFile, "-vn",
            "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", wav16k);
        if (!ok) throw new Exception("Audio conversion for Whisper failed:\n" + err);

        var prefix = Path.Combine(workDir, "whisper_out");
        var baseArgs = new List<string>
        {
            "-m", modelPath, "-f", wav16k, "--max-len", "1", "-oj", "-of", prefix
        };
        if (language != "auto" && language.Length > 0)
        {
            baseArgs.Add("-l");
            baseArgs.Add(language);
        }

        // optional flags, tried first and dropped if the build rejects them
        var optional = new List<string>();
        if (fullJson) optional.Add("--output-json-full");
        if (vadModel.Length > 0 && File.Exists(vadModel))
        {
            optional.Add("--vad");
            optional.Add("--vad-model");
            optional.Add(vadModel);
        }

        var (ok2, err2) = await RunExeAsync(exePath, workDir,
            baseArgs.Concat(optional).ToArray());
        if (!ok2 && optional.Count > 0)
        {
            Diag.Write("This whisper.cpp build rejected the extra flags - retrying without them.");
            (ok2, err2) = await RunExeAsync(exePath, workDir, baseArgs.ToArray());
        }
        if (!ok2) throw new Exception("Whisper failed:\n" + err2);

        var jsonPath = prefix + ".json";
        if (!File.Exists(jsonPath))
            throw new Exception("Whisper produced no JSON output. Check the model path.");
        return ParseWhisperJson(await File.ReadAllTextAsync(jsonPath));
    }

    /// <summary>Parses whisper.cpp -oj output. Supports both the current
    /// format (a JSON array of segments, optionally with per-token
    /// timestamps) and the older {"transcription":[...]} wrapper.</summary>
    private static List<WhisperWord> ParseWhisperJson(string json)
    {
        var words = new List<WhisperWord>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        JsonElement segments;
        if (root.ValueKind == JsonValueKind.Array)
            segments = root;
        else if (root.TryGetProperty("transcription", out var tr))
            segments = tr;
        else if (root.TryGetProperty("segments", out var sg))
            segments = sg;
        else
            throw new Exception("Unexpected Whisper JSON format.");

        foreach (var seg in segments.EnumerateArray())
        {
            // 1) preferred: real word tokens (needs --output-json-full)
            if (seg.TryGetProperty("tokens", out var tokens) &&
                tokens.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tokens.EnumerateArray())
                {
                    var txt = GetString(t, "text");
                    if (string.IsNullOrWhiteSpace(txt)) continue;
                    if (t.TryGetProperty("p", out var p) && p.ValueKind == JsonValueKind.Number &&
                        p.GetDouble() < 0.5) continue; // low confidence token
                    if (!TryMillis(t, "t0", out var t0)) continue;
                    if (!TryMillis(t, "t1", out var t1)) t1 = t0 + 60;
                    if (t1 <= t0) t1 = t0 + 60;
                    words.Add(new WhisperWord(txt,
                        TimeSpan.FromMilliseconds(t0),
                        TimeSpan.FromMilliseconds(t1)));
                }
                if (words.Count > 0) continue;
            }

            // 2) fallback: segment level offsets (ms)
            if (!seg.TryGetProperty("offsets", out var off)) continue;
            if (!off.TryGetProperty("from", out var from) ||
                !off.TryGetProperty("to", out var to)) continue;
            var text = GetString(seg, "text");
            if (string.IsNullOrWhiteSpace(text)) continue;
            words.Add(new WhisperWord(text,
                TimeSpan.FromMilliseconds(from.GetDouble()),
                TimeSpan.FromMilliseconds(Math.Max(from.GetDouble(), to.GetDouble()))));
        }

        return words;
    }

    private static bool TryMillis(JsonElement e, string name, out double value)
    {
        value = 0;
        if (!e.TryGetProperty(name, out var v)) return false;
        if (v.ValueKind == JsonValueKind.Number) { value = v.GetDouble(); return true; }
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var d)) { value = d; return true; }
        return false;
    }

    private static string GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    /// <summary>Aligns the lyric lines (from the editor, timestamps are
    /// ignored) to the Whisper word timeline and returns a fresh WORD-TIMED
    /// LRC (&lt;mm:ss.xx&gt; word tags) so the video can highlight word by
    /// word. Lyric words that Whisper cannot find are interpolated between
    /// their matched neighbours.</summary>
    public static string Align(List<WhisperWord> whisper, string lrcText)
    {
        if (whisper.Count == 0) return lrcText; // nothing to align against

        // split into lines FIRST, then strip the timestamps - otherwise the
        // whole lyric sheet collapses into a single line
        var textLines = lrcText.Replace("\r\n", "\n").Split('\n')
            .Select(l => StripTags.Replace(l, "").Trim())
            .Where(l => l.Length > 0)
            .ToList();
        if (textLines.Count == 0) return lrcText;

        var sb = new StringBuilder();
        int wi = 0;                              // position in the whisper list

        foreach (var lyricLine in textLines)
        {
            var lyricTokens = lyricLine
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize)
                .Where(t => t.Length > 0).ToArray();
            if (lyricTokens.Length == 0) continue;

            var matched = MatchTokens(lyricTokens, whisper, ref wi);
            Interpolate(matched);

            // rebuild the line with its original spacing, but with word tags
            var rawWords = lyricLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var lineStart = matched.FirstOrDefault().start ?? TimeSpan.Zero;
            var lineSb = new StringBuilder();
            lineSb.Append('[').Append(LyricsService.FormatLrcTime(lineStart)).Append(']');
            for (int idx = 0; idx < rawWords.Length; idx++)
            {
                var stamp = idx < matched.Count
                    ? matched[idx].start ?? lineStart
                    : lineStart;
                lineSb.Append('<').Append(LyricsService.FormatLrcTime(stamp)).Append('>')
                      .Append(rawWords[idx]).Append(' ');
            }
            sb.Append(lineSb.ToString().TrimEnd()).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Matches the words of one lyric line against the words the
    /// AI heard, keeping the order and skipping anything that does not fit.</summary>
    private static List<(string word, TimeSpan? start)> MatchTokens(
        string[] lyricTokens, List<WhisperWord> whisper, ref int wi)
    {
        var matched = new List<(string word, TimeSpan? start)>();
        foreach (var tok in lyricTokens)
        {
            int found = -1;
            int limit = Math.Min(whisper.Count, wi + 40); // tolerate small extras
            int bestScore = 0;
            for (int probe = wi; probe < limit; probe++)
            {
                var wt = Normalize(whisper[probe].Text);
                if (wt.Length == 0) continue;
                int score = Similarity(tok, wt);
                if (score > bestScore)
                {
                    bestScore = score;
                    found = probe;
                    if (score >= 95) break; // exact hit, stop looking
                }
            }
            if (found >= 0 && bestScore >= 70)
            {
                matched.Add((tok, whisper[found].Start));
                wi = found + 1;
            }
            else
            {
                matched.Add((tok, null));   // unresolved -> interpolate
            }
        }
        return matched;
    }

    /// <summary>Gives every unresolved word a time between its neighbours.</summary>
    private static void Interpolate(List<(string word, TimeSpan? start)> matched)
    {
        for (int i = 0; i < matched.Count; i++)
        {
            if (matched[i].start.HasValue) continue;
            TimeSpan? prev = null, next = null;
            for (int p = i - 1; p >= 0 && prev == null; p--) prev = matched[p].start;
            for (int n = i + 1; n < matched.Count && next == null; n++) next = matched[n].start;
            if (prev.HasValue && next.HasValue)
            {
                double span = (next.Value - prev.Value).TotalMilliseconds;
                int gap = matched.Count - i;
                matched[i] = (matched[i].word,
                    prev.Value + TimeSpan.FromMilliseconds(span / gap));
            }
            else if (prev.HasValue)
            {
                matched[i] = (matched[i].word, prev.Value + TimeSpan.FromMilliseconds(400));
            }
            else if (next.HasValue)
            {
                matched[i] = (matched[i].word, next.Value - TimeSpan.FromMilliseconds(400));
            }
            else
            {
                matched[i] = (matched[i].word, TimeSpan.FromSeconds(3 + 0.35 * i));
            }
        }
    }

    /// <summary>Builds a word-timed LRC straight from the Whisper output.
    /// Used when no lyrics were found anywhere - the AI supplies both the
    /// words and the timing, so the video still works fully automatically.</summary>
    public static string ToLrcFromWhisper(List<WhisperWord> whisper)
    {
        if (whisper.Count == 0) return "";

        var lines = new List<(TimeSpan start, List<WhisperWord> words)>();
        var current = new List<WhisperWord>();
        foreach (var w in whisper)
        {
            var text = w.Text.Trim();
            if (text.Length == 0) continue;
            if (current.Count > 0 &&
                (w.Start - current[^1].End > TimeSpan.FromMilliseconds(900) ||
                 current[^1].Text.TrimEnd().EndsWith('.') ||
                 current[^1].Text.TrimEnd().EndsWith(',')))
            {
                lines.Add((current[0].Start, current));
                current = new List<WhisperWord>();
            }
            current.Add(w);
        }
        if (current.Count > 0) lines.Add((current[0].Start, current));

        var sb = new StringBuilder();
        foreach (var (start, words) in lines)
        {
            sb.Append('[').Append(LyricsService.FormatLrcTime(start)).Append(']');
            foreach (var w in words)
                sb.Append('<').Append(LyricsService.FormatLrcTime(w.Start)).Append('>')
                  .Append(w.Text.Trim()).Append(' ');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>0-100 similarity between two normalized tokens.</summary>
    private static int Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 100;
        if (b.StartsWith(a, StringComparison.Ordinal) ||
            a.StartsWith(b, StringComparison.Ordinal)) return 85;
        if (a.Contains(b, StringComparison.Ordinal) ||
            b.Contains(a, StringComparison.Ordinal)) return 75;

        var dist = Levenshtein(a, b);
        int longest = Math.Max(a.Length, b.Length);
        return (int)Math.Round(100.0 * (longest - dist) / longest);
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    private static string Normalize(string s) =>
        new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static async Task<(bool ok, string error)> RunExeAsync(
        string exe, string workDir, string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process? proc;
        try { proc = Process.Start(psi); }
        catch (Exception ex) { return (false, "Could not start Whisper: " + ex.Message); }
        if (proc is null) return (false, "Could not start Whisper.");
        var err = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return proc.ExitCode == 0
            ? (true, "")
            : (false, err.Length > 1500 ? err[..1500] + " ..." : err);
    }
}

// ------------------------------------------------------------------
// The automatic pipeline: song -> instrumental -> lyrics -> timing -> MP4
// ------------------------------------------------------------------

public class PipelineResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
    public string OutputFile { get; set; } = "";
    public string Lyrics { get; set; } = "";
    public string LrcFile { get; set; } = "";
}

public enum SeparationMode
{
    Auto,            // AI separation when available, otherwise centre cancel
    CentreChannel,   // fast, free, always available
    Demucs,          // force the AI separator
    OwnInstrumental  // use the WAV the user supplied
}

public class PipelineContext
{
    public string SongFile { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Track { get; set; } = "";
    public string WorkDir { get; set; } = "";
    public string OriginalWav { get; set; } = "";
    public string InstrumentalWav { get; set; } = "";
    public string InstrumentalOverride { get; set; } = "";   // user supplied WAV
    public string BackgroundOverride { get; set; } = "";    // user supplied img/vid
    public string LyricsText { get; set; } = "";
    public bool LyricsSynced { get; set; }
    public bool LyricsFromAi { get; set; }

    /// <summary>Lyrics the user already has in the editor. When present
    /// they are used as the text source (and hand-edited wording is kept),
    /// instead of being overwritten by the LRCLIB lookup.</summary>
    public string ExistingLyrics { get; set; } = "";

    public string WhisperExe { get; set; } = "";
    public string WhisperModel { get; set; } = "";
    public string WhisperVadModel { get; set; } = "";
    public string WhisperLanguage { get; set; } = "auto";
    public bool AlwaysRunAi { get; set; }
    public bool AutoMode { get; set; }
    public SeparationMode Separation { get; set; } = SeparationMode.Auto;
    public RenderOptions Options { get; set; } = new();
    public CancellationToken Cancel { get; set; }
}

public static class KaraokePipeline
{
    public static async Task<PipelineResult> RunAsync(
        PipelineContext ctx, Action<string> log, Action<double>? progress = null)
    {
        var result = new PipelineResult();
        try
        {
            Directory.CreateDirectory(ctx.WorkDir);

            var duration = await StageExtractAudio(ctx, log, progress);
            if (duration <= TimeSpan.Zero)
            {
                result.Message = "The song file could not be read.";
                return result;
            }

            var instrumental = await StageInstrumental(ctx, log, progress);
            if (instrumental is null) return result;

            await StageLyrics(ctx, log, progress, duration);
            await StageAiTiming(ctx, log, progress);
            if (ctx.LyricsText.Trim().Length == 0)
            {
                result.Message = "There is nothing to sing: no lyrics were found and the " +
                                 "AI is not configured. Fetch lyrics, paste them into the " +
                                 "editor, or set up whisper.cpp (see the README).";
                return result;
            }

            var background = await StageBackground(ctx, log, progress);
            if (background is null) return result;

            return await StageRender(ctx, log, progress, duration, background, instrumental);
        }
        catch (OperationCanceledException)
        {
            result.Message = "Cancelled.";
            return result;
        }
        catch (Exception ex)
        {
            Diag.WriteError("Pipeline", ex);
            result.Message = "Pipeline failed: " + ex.Message;
            return result;
        }
    }

    // ---- stage 1 -------------------------------------------------
    private static async Task<TimeSpan> StageExtractAudio(
        PipelineContext ctx, Action<string> log, Action<double>? progress)
    {
        log("Step 1/6: reading the song from this PC...");
        progress?.Invoke(0.05);
        ctx.Cancel.ThrowIfCancellationRequested();

        ctx.OriginalWav = Path.Combine(ctx.WorkDir, "song.wav");
        if (!File.Exists(ctx.OriginalWav))
        {
            var (ok, err) = await FFmpeg.ExtractWavAsync(ctx.SongFile, ctx.OriginalWav);
            if (!ok) throw new Exception(err);
        }
        var duration = await FFmpeg.ProbeDurationAsync(ctx.OriginalWav);
        if (duration <= TimeSpan.Zero)
            duration = await FFmpeg.ProbeDurationAsync(ctx.SongFile);
        log($"        song length: {duration:mm\\:ss}");
        return duration;
    }

    // ---- stage 2 -------------------------------------------------
    private static async Task<string?> StageInstrumental(
        PipelineContext ctx, Action<string> log, Action<double>? progress)
    {
        log("Step 2/6: preparing the instrumental...");
        progress?.Invoke(0.15);
        ctx.Cancel.ThrowIfCancellationRequested();

        var outWav = Path.Combine(ctx.WorkDir, "instrumental.wav");

        if (ctx.Separation == SeparationMode.OwnInstrumental &&
            ctx.InstrumentalOverride.Length > 0)
        {
            ctx.InstrumentalWav = ctx.InstrumentalOverride;
            log("        using the instrumental WAV you supplied");
            return ctx.InstrumentalWav;
        }

        var demucsAvailable = await Demucs.IsAvailableAsync();

        if (ctx.Separation == SeparationMode.Demucs ||
            (ctx.Separation == SeparationMode.Auto && demucsAvailable))
        {
            if (!demucsAvailable)
                throw new Exception("Demucs is not installed on this PC. Install it with " +
                                    "\"pip install demucs\" or switch the vocal removal " +
                                    "mode to \"Centre cancellation\".");
            log("        separating with Demucs (AI) - this takes a few minutes...");
            var demucsDir = Path.Combine(ctx.WorkDir, "demucs");
            var (ok, message, produced) = await Demucs.SeparateAsync(
                ctx.OriginalWav, demucsDir);
            if (ok && produced.Length > 0)
            {
                ctx.InstrumentalWav = produced;
                log("        Demucs finished - vocals removed with AI quality");
                return ctx.InstrumentalWav;
            }
            log("        " + message.Split('\n')[0] + " - falling back to centre cancellation");
        }

        var (ok2, err2) = await FFmpeg.RemoveVocalsAsync(ctx.OriginalWav, outWav);
        if (!ok2) throw new Exception(err2);
        ctx.InstrumentalWav = outWav;
        log("        vocals removed (centre-channel cancellation)");
        return ctx.InstrumentalWav;
    }

    // ---- stage 3 -------------------------------------------------
    private static async Task StageLyrics(
        PipelineContext ctx, Action<string> log, Action<double>? progress,
        TimeSpan duration)
    {
        log("Step 3/6: looking up lyrics (LRCLIB)...");
        progress?.Invoke(0.3);
        ctx.Cancel.ThrowIfCancellationRequested();

        if (ctx.ExistingLyrics.Trim().Length > 0)
        {
            // the user already has lyrics in the editor - keep their text
            ctx.LyricsText = ctx.ExistingLyrics;
            ctx.LyricsSynced = LrcParser.Parse(ctx.LyricsText).Any(l => l.WordTiming);
            log("        using the lyrics from the editor");
            return;
        }

        var lyrics = await LyricsService.SearchAsync(ctx.Artist, ctx.Track, duration);
        if (lyrics is not null)
        {
            ctx.LyricsText = lyrics.LrcOrPlain;
            ctx.LyricsSynced = lyrics.Synced;
            log($"        found: {lyrics.Artist} - {lyrics.Track} " +
                $"({(lyrics.Synced ? "synced" : "unsynced")})");
            return;
        }

        ctx.LyricsText = "";
        ctx.LyricsSynced = false;
        log("        nothing found on LRCLIB - the AI will supply the words");
    }

    // ---- stage 4 -------------------------------------------------
    private static async Task StageAiTiming(
        PipelineContext ctx, Action<string> log, Action<double>? progress)
    {
        log("Step 4/6: AI auto-sync (Whisper)...");
        progress?.Invoke(0.4);
        ctx.Cancel.ThrowIfCancellationRequested();

        var aiReady = File.Exists(ctx.WhisperExe) && File.Exists(ctx.WhisperModel);
        // In automatic mode the AI always runs when it is available: it
        // upgrades line timings to true word timings. In manual mode it
        // only runs on request ("Always re-time with AI") or when the
        // lyrics are not synced yet.
        var aiWanted = ctx.AutoMode || ctx.AlwaysRunAi || !ctx.LyricsSynced;

        if (!aiReady || !aiWanted)
        {
            log(!aiReady
                ? "        SKIPPED - whisper-cli.exe / model not configured"
                : "        SKIPPED - the lyrics are already synced and you did " +
                  "not ask for AI re-timing");
            if (!ctx.LyricsSynced && ctx.LyricsText.Length > 0)
            {
                var dur = await FFmpeg.ProbeDurationAsync(ctx.OriginalWav);
                ctx.LyricsText = LyricsService.PlainToSkeleton(ctx.LyricsText, dur);
                log("        laid the lyrics out evenly (no AI available) - " +
                    "run AI Auto-sync later for exact timing");
            }
            return;
        }

        var words = await WhisperSync.TranscribeAsync(
            ctx.WhisperExe, ctx.WhisperModel, ctx.OriginalWav,
            Path.Combine(ctx.WorkDir, "whisper"),
            ctx.WhisperLanguage, ctx.WhisperVadModel);
        if (words.Count == 0)
            throw new Exception("Whisper detected no words in this song. Try a bigger " +
                                "model (ggml-small.bin / ggml-medium.bin).");
        log($"        AI heard {words.Count} words");

        if (ctx.LyricsText.Length > 0)
        {
            ctx.LyricsText = WhisperSync.Align(words, ctx.LyricsText);
            log("        rewrote every timestamp to the AI word timing");
        }
        else
        {
            ctx.LyricsText = WhisperSync.ToLrcFromWhisper(words);
            ctx.LyricsFromAi = true;
            log("        used the AI transcription as the lyrics");
        }
    }

    // ---- stage 5 -------------------------------------------------
    private static async Task<string?> StageBackground(
        PipelineContext ctx, Action<string> log, Action<double>? progress)
    {
        log("Step 5/6: preparing the background...");
        progress?.Invoke(0.55);
        ctx.Cancel.ThrowIfCancellationRequested();

        var background = ctx.BackgroundOverride;
        if (background.Length > 0 && File.Exists(background))
        {
            log("        using your background file");
            return background;
        }

        var generated = Path.Combine(ctx.WorkDir, "background.png");
        var (ok, err) = await FFmpeg.GenerateBackgroundAsync(
            generated, ctx.Options.Width, ctx.Options.Height);
        if (!ok) throw new Exception("Could not create a background: " + err);
        log("        generated a gradient background");
        return generated;
    }

    // ---- stage 6 -------------------------------------------------
    private static async Task<PipelineResult> StageRender(
        PipelineContext ctx, Action<string> log, Action<double>? progress,
        TimeSpan duration, string background, string instrumental)
    {
        var result = new PipelineResult();
        log("Step 6/6: rendering the karaoke video...");
        progress?.Invoke(0.6);
        ctx.Cancel.ThrowIfCancellationRequested();

        var lines = LrcParser.Parse(ctx.LyricsText);
        if (lines.Count == 0)
        {
            result.Message = "There are no lyric lines to render.";
            return result;
        }

        var ass = AssGenerator.Build(lines, ctx.Options);
        var assFile = Path.Combine(ctx.WorkDir, "karaokeforge.ass");
        await File.WriteAllTextAsync(assFile, ass, Encoding.UTF8);

        var output = DefaultOutputPath(ctx.SongFile, ctx.Track);
        var (ok, err) = await FFmpeg.ExportVideoAsync(
            background, instrumental, assFile, output, ctx.Options,
            ctx.WorkDir, t => progress?.Invoke(0.6 + 0.4 * Math.Clamp(
                t / Math.Max(1, duration.TotalSeconds), 0, 1)));
        if (!ok)
        {
            result.Message = err;
            return result;
        }

        if (ctx.Options.SaveLrc)
        {
            try
            {
                var lrcPath = Path.ChangeExtension(output, ".lrc");
                await File.WriteAllTextAsync(lrcPath, ctx.LyricsText, Encoding.UTF8);
                result.LrcFile = lrcPath;
                log("        saved the word-timed lyrics as " + Path.GetFileName(lrcPath));
            }
            catch (Exception ex)
            {
                Diag.WriteError("Saving the .lrc file", ex);
            }
        }

        result.Ok = true;
        result.OutputFile = output;
        result.Lyrics = ctx.LyricsText;
        log("Done.");
        progress?.Invoke(1.0);
        return result;
    }

    public static string DefaultOutputPath(string songFile, string track)
    {
        var name = string.IsNullOrWhiteSpace(track)
            ? Path.GetFileNameWithoutExtension(songFile)
            : track;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        var dir = Path.GetDirectoryName(songFile) ?? Environment.GetFolderPath(
            Environment.SpecialFolder.Desktop);
        return Path.Combine(dir, name.Trim() + " (karaoke).mp4");
    }
}

// ------------------------------------------------------------------
// Main window
// ------------------------------------------------------------------

#region WINDOWS_UI
public partial class MainForm : Form
{
    private TextBox? _txtSource, _txtArtist, _txtTrack, _txtLrc, _txtInstrumental,
        _txtBackground, _txtWhisperExe, _txtWhisperModel, _txtVad, _txtLog;
    private NumericUpDown? _numFont, _numOffset, _numFade;
    private ComboBox? _cmbResolution, _cmbFont, _cmbSung, _cmbUnsung, _cmbNextColour,
        _cmbPreset, _cmbLanguage, _cmbSeparation, _cmbPosition, _cmbRecent;
    private CheckBox? _chkAlwaysAi, _chkShowNext, _chkBoxed, _chkNormalize, _chkSaveLrc;
    private Button? _btnLoad, _btnFetch, _btnVocal, _btnInstrumental, _btnPreview,
        _btnBg, _btnExport, _btnAutosync, _btnAuto, _btnCancel, _btnWhisperExe,
        _btnWhisperModel, _btnVad, _btnQuickPreview, _btnCopyLog, _btnClearLog,
        _btnSetup, _btnEvenly;
    private Label? _status, _lblSongInfo, _lblSetup;
    private ProgressBar? _progress;

    private readonly PipelineContext _ctx = new();
    private CancellationTokenSource? _cts;
    private System.Media.SoundPlayer? _previewPlayer;
    private bool _busy;

    private static readonly string[] Resolutions =
    {
        "1920x1080 (Full HD)", "1280x720 (HD)", "1080x1920 (Vertical / Shorts)",
        "1080x1080 (Square)", "2560x1440 (QHD)"
    };
    private static readonly string[] Fonts =
    {
        "Arial", "Verdana", "Tahoma", "Segoe UI", "Impact", "Trebuchet MS"
    };
    private static readonly string[] Colours =
    {
        "White", "Yellow", "Gold", "Orange", "OrangeRed", "Red", "Pink", "HotPink",
        "LightPink", "Magenta", "Purple", "DeepSkyBlue", "SkyBlue", "LightCyan",
        "Cyan", "LimeGreen", "Green", "Silver", "Gray", "DarkGray", "Black"
    };
    private static readonly string[] Languages =
    {
        "auto", "en", "es", "fr", "de", "it", "pt", "nl", "ru", "tr", "ar", "hi",
        "ja", "ko", "zh", "pl", "sv", "id"
    };
    private static readonly string[] SeparationModes =
    {
        "Auto (AI if available)", "Centre cancellation (fast)",
        "Demucs (AI separation)", "Use my instrumental file"
    };
    private static readonly string[] Positions = { "Bottom", "Middle", "Top" };

    public MainForm()
    {
        Text = "KaraokeForge - All-in-One Karaoke Video Creator";
        Size = new Size(1120, 920);
        MinimumSize = new Size(1000, 860);
        Font = new Font("Segoe UI", 9.5f);
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        KeyPreview = true;
        KeyDown += OnKeyDown;
        FormClosed += (_, _) => { try { _previewPlayer?.Stop(); } catch { } };
        BuildUi();
        Diag.Message += OnDiagMessage;
        _ = StartupCheckAsync();
    }

    // ---------------- UI construction ----------------

    private static Label L(string text, int x, int y) =>
        new() { Text = text, Location = new Point(x, y), AutoSize = true };

    private static Button B(string text, int x, int y, int w, EventHandler onClick)
    {
        var b = new Button { Text = text, Location = new Point(x, y), Width = w };
        b.Click += onClick;
        return b;
    }

    private static TextBox T(int x, int y, int w, bool multiline = false)
    {
        var t = new TextBox { Location = new Point(x, y), Width = w, Multiline = multiline };
        if (multiline) t.ScrollBars = ScrollBars.Both;
        return t;
    }

    private void BuildUi()
    {
        BuildSongGroup();
        BuildLyricsGroup();
        BuildInstrumentalGroup();
        BuildVideoGroup();
        BuildLogGroup();

        _status = new Label
        {
            Text = "Ready. Load a song from this PC, then press MAKE KARAOKE VIDEO.",
            Location = new Point(12, 800),
            Width = 1096,
            Height = 36
        };
        Controls.Add(_status);

        _progress = new ProgressBar
        {
            Location = new Point(12, 840),
            Size = new Size(1096, 18),
            Style = ProgressBarStyle.Continuous
        };
        Controls.Add(_progress);

        Controls.Add(new Label
        {
            Text = "Tip: drop the song file anywhere on this window. Everything runs " +
                   "locally - only the lyrics text is requested from the free LRCLIB service.",
            Location = new Point(12, 864),
            Width = 1096,
            Height = 20,
            ForeColor = Color.DimGray
        });
    }

    private void BuildSongGroup()
    {
        var g = new GroupBox
        {
            Text = "1 - Song (loaded from THIS PC only - nothing is uploaded)",
            Location = new Point(12, 8),
            Size = new Size(1096, 104)
        };
        g.Controls.Add(L("Song file:", 12, 26));
        _txtSource = T(92, 22, 620);
        g.Controls.Add(_txtSource);
        _btnLoad = B("Load song...", 722, 21, 120, BtnLoad_Click);
        g.Controls.Add(_btnLoad);
        _btnSetup = B("Setup help", 852, 21, 110, BtnSetup_Click);
        g.Controls.Add(_btnSetup);
        _lblSetup = L("Checking FFmpeg...", 972, 26);
        g.Controls.Add(_lblSetup);

        g.Controls.Add(L("Artist:", 12, 60));
        _txtArtist = T(62, 56, 230);
        g.Controls.Add(_txtArtist);
        g.Controls.Add(L("Track:", 306, 60));
        _txtTrack = T(352, 56, 340);
        g.Controls.Add(_txtTrack);
        _lblSongInfo = L("Duration: -", 704, 60);
        g.Controls.Add(_lblSongInfo);
        g.Controls.Add(L("Recent:", 812, 60));
        _cmbRecent = new ComboBox
        {
            Location = new Point(866, 56),
            Width = 210,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbRecent.SelectedIndexChanged += CmbRecent_SelectedIndexChanged;
        g.Controls.Add(_cmbRecent);

        Controls.Add(g);
    }

    private void BuildLyricsGroup()
    {
        var g = new GroupBox
        {
            Text = "2 - Lyrics & AI timing (automatic: LRCLIB + whisper.cpp)",
            Location = new Point(12, 118),
            Size = new Size(1096, 274)
        };
        _btnFetch = B("Fetch lyrics (LRCLIB)", 12, 22, 170, BtnFetch_Click);
        g.Controls.Add(_btnFetch);
        _btnAutosync = B("AI auto-sync", 190, 22, 120, BtnAutosync_Click);
        g.Controls.Add(_btnAutosync);
        _chkAlwaysAi = new CheckBox
        {
            Text = "Always re-time with AI on export",
            Location = new Point(320, 25),
            Width = 210
        };
        g.Controls.Add(_chkAlwaysAi);

        g.Controls.Add(L("Language:", 540, 25));
        _cmbLanguage = new ComboBox
        {
            Location = new Point(610, 22), Width = 80,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbLanguage.Items.AddRange(Languages);
        _cmbLanguage.SelectedIndex = 0;
        g.Controls.Add(_cmbLanguage);

        g.Controls.Add(L("whisper-cli.exe:", 12, 58));
        _txtWhisperExe = T(108, 54, 220);
        g.Controls.Add(_txtWhisperExe);
        _btnWhisperExe = B("...", 334, 53, 40, BtnWhisperExe_Click);
        g.Controls.Add(_btnWhisperExe);

        g.Controls.Add(L("model (.bin):", 386, 58));
        _txtWhisperModel = T(462, 54, 220);
        g.Controls.Add(_txtWhisperModel);
        _btnWhisperModel = B("...", 688, 53, 40, BtnWhisperModel_Click);
        g.Controls.Add(_btnWhisperModel);

        g.Controls.Add(L("VAD model (optional):", 740, 58));
        _txtVad = T(862, 54, 170);
        g.Controls.Add(_txtVad);
        _btnVad = B("...", 1038, 53, 40, BtnVad_Click);
        g.Controls.Add(_btnVad);

        g.Controls.Add(L("Lyrics editor - LRC; word tags <mm:ss.xx> give per-word highlighting:", 12, 90));
        _btnEvenly = B("Spread evenly", 660, 84, 110, BtnEvenly_Click);
        g.Controls.Add(_btnEvenly);

        _txtLrc = T(12, 112, 1072, multiline: true);
        _txtLrc.Height = 148;
        _txtLrc.Font = new Font("Consolas", 9.5f);
        g.Controls.Add(_txtLrc);

        Controls.Add(g);
    }

    private void BuildInstrumentalGroup()
    {
        var g = new GroupBox
        {
            Text = "3 - Instrumental (vocal removal)",
            Location = new Point(12, 398),
            Size = new Size(1096, 100)
        };
        g.Controls.Add(L("Vocal removal:", 12, 26));
        _cmbSeparation = new ComboBox
        {
            Location = new Point(100, 22), Width = 190,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbSeparation.Items.AddRange(SeparationModes);
        _cmbSeparation.SelectedIndex = 0;
        g.Controls.Add(_cmbSeparation);

        _btnVocal = B("Remove vocals now", 300, 21, 150, BtnVocal_Click);
        g.Controls.Add(_btnVocal);
        _btnInstrumental = B("Use my instrumental...", 458, 21, 160, BtnInstrumental_Click);
        g.Controls.Add(_btnInstrumental);
        _txtInstrumental = T(626, 20, 330);
        g.Controls.Add(_txtInstrumental);
        _btnPreview = B("Preview audio", 964, 19, 110, BtnPreview_Click);
        g.Controls.Add(_btnPreview);

        g.Controls.Add(L("Tip: for studio quality export an instrumental WAV with " +
                        "Ultimate Vocal Remover 5, or install Demucs (pip install demucs) " +
                        "and let KaraokeForge do the separation with AI.", 12, 66));
        Controls.Add(g);
    }

    private void BuildVideoGroup()
    {
        var g = new GroupBox
        {
            Text = "4 - Karaoke video output",
            Location = new Point(12, 504),
            Size = new Size(1096, 176)
        };
        g.Controls.Add(L("Background:", 12, 26));
        _txtBackground = T(88, 22, 600);
        g.Controls.Add(_txtBackground);
        _btnBg = B("Browse...", 696, 21, 100, BtnBg_Click);
        g.Controls.Add(_btnBg);
        g.Controls.Add(L("(empty = generated gradient)", 804, 26));

        g.Controls.Add(L("Resolution:", 12, 60));
        _cmbResolution = new ComboBox
        {
            Location = new Point(88, 56), Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbResolution.Items.AddRange(Resolutions);
        _cmbResolution.SelectedIndex = 0;
        g.Controls.Add(_cmbResolution);

        g.Controls.Add(L("Font:", 262, 60));
        _cmbFont = new ComboBox
        {
            Location = new Point(296, 56), Width = 110,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbFont.Items.AddRange(Fonts);
        _cmbFont.SelectedIndex = 0;
        g.Controls.Add(_cmbFont);

        g.Controls.Add(L("Size:", 414, 60));
        _numFont = new NumericUpDown
        {
            Location = new Point(450, 56), Width = 60,
            Minimum = 24, Maximum = 200, Value = 72
        };
        g.Controls.Add(_numFont);

        g.Controls.Add(L("Offset (s):", 522, 60));
        _numOffset = new NumericUpDown
        {
            Location = new Point(598, 56), Width = 60,
            Minimum = -30, Maximum = 30, DecimalPlaces = 1, Value = 0
        };
        g.Controls.Add(_numOffset);

        g.Controls.Add(L("Fade:", 672, 60));
        _numFade = new NumericUpDown
        {
            Location = new Point(706, 56), Width = 60,
            Minimum = 0, Maximum = 1000, Value = 140, Increment = 20
        };
        g.Controls.Add(_numFade);

        g.Controls.Add(L("Look:", 780, 60));
        _cmbPreset = new ComboBox
        {
            Location = new Point(820, 56), Width = 110,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var key in AssGenerator.Presets.Keys) _cmbPreset.Items.Add(key);
        _cmbPreset.SelectedIndex = 0;
        _cmbPreset.SelectedIndexChanged += CmbPreset_SelectedIndexChanged;
        g.Controls.Add(_cmbPreset);

        g.Controls.Add(L("Lyrics position:", 946, 60));
        _cmbPosition = new ComboBox
        {
            Location = new Point(12, 88), Width = 110,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbPosition.Items.AddRange(Positions);
        _cmbPosition.SelectedIndex = 0;
        g.Controls.Add(_cmbPosition);

        g.Controls.Add(L("Sung:", 132, 90));
        _cmbSung = new ComboBox
        {
            Location = new Point(170, 86), Width = 90,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbSung.Items.AddRange(Colours);
        _cmbSung.SelectedIndex = 0;
        g.Controls.Add(_cmbSung);

        g.Controls.Add(L("Upcoming:", 272, 90));
        _cmbUnsung = new ComboBox
        {
            Location = new Point(344, 86), Width = 90,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbUnsung.Items.AddRange(Colours);
        _cmbUnsung.SelectedIndex = Colours.ToList().IndexOf("DeepSkyBlue");
        g.Controls.Add(_cmbUnsung);

        g.Controls.Add(L("Next line:", 446, 90));
        _cmbNextColour = new ComboBox
        {
            Location = new Point(512, 86), Width = 90,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cmbNextColour.Items.AddRange(Colours);
        _cmbNextColour.SelectedIndex = Colours.ToList().IndexOf("Silver");
        g.Controls.Add(_cmbNextColour);

        _chkShowNext = new CheckBox
        {
            Text = "Show the next line", Location = new Point(614, 88), Width = 120,
            Checked = true
        };
        g.Controls.Add(_chkShowNext);
        _chkBoxed = new CheckBox
        {
            Text = "Boxed text", Location = new Point(740, 88), Width = 90
        };
        g.Controls.Add(_chkBoxed);
        _chkNormalize = new CheckBox
        {
            Text = "Normalize volume", Location = new Point(836, 88), Width = 120,
            Checked = true
        };
        g.Controls.Add(_chkNormalize);
        _chkSaveLrc = new CheckBox
        {
            Text = "Save .lrc next to video", Location = new Point(960, 88), Width = 130,
            Checked = true
        };
        g.Controls.Add(_chkSaveLrc);

        _btnExport = B("Export karaoke video (MP4)", 12, 124, 230, BtnExport_Click);
        _btnExport.Height = 34;
        g.Controls.Add(_btnExport);
        _btnQuickPreview = B("Quick preview (20 s)", 250, 124, 170, BtnQuickPreview_Click);
        _btnQuickPreview.Height = 34;
        g.Controls.Add(_btnQuickPreview);
        _btnAuto = B("MAKE KARAOKE VIDEO  (automatic)", 430, 124, 340, BtnAuto_Click);
        _btnAuto.Height = 34;
        g.Controls.Add(_btnAuto);
        _btnCancel = B("Cancel", 780, 124, 100, BtnCancel_Click);
        _btnCancel.Height = 34;
        _btnCancel.Enabled = false;
        g.Controls.Add(_btnCancel);

        Controls.Add(g);
    }

    private void BuildLogGroup()
    {
        var g = new GroupBox
        {
            Text = "Log",
            Location = new Point(12, 686),
            Size = new Size(1096, 108)
        };
        _txtLog = T(12, 22, 1000, multiline: true);
        _txtLog.Height = 74;
        _txtLog.ReadOnly = true;
        _txtLog.ScrollBars = ScrollBars.Vertical;
        g.Controls.Add(_txtLog);
        _btnCopyLog = B("Copy", 1020, 22, 64, BtnCopyLog_Click);
        g.Controls.Add(_btnCopyLog);
        _btnClearLog = B("Clear", 1020, 52, 64, BtnClearLog_Click);
        g.Controls.Add(_btnClearLog);
        Controls.Add(g);
    }

    // ---------------- small helpers ----------------

    /// <summary>Runs an action on the UI thread (safe from worker threads).</summary>
    private void Ui(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(action); } catch { /* window closing */ }
        }
        else
        {
            action();
        }
    }

    private void SetStatus(string msg) =>
        Ui(() => { if (_status is not null) _status.Text = msg; });

    private void OnDiagMessage(string text)
    {
        Ui(() =>
        {
            if (_txtLog is null) return;
            _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        });
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (var b in new[] { _btnLoad, _btnFetch, _btnVocal, _btnInstrumental,
            _btnBg, _btnExport, _btnAutosync, _btnAuto, _btnQuickPreview, _btnEvenly })
            b!.Enabled = !busy;
        if (_btnCancel is not null) _btnCancel.Enabled = busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private RenderOptions CurrentOptions()
    {
        var (w, h) = (_cmbResolution?.SelectedIndex) switch
        {
            1 => (1280, 720),
            2 => (1080, 1920),
            3 => (1080, 1080),
            4 => (2560, 1440),
            _ => (1920, 1080)
        };
        return new RenderOptions
        {
            Width = w,
            Height = h,
            FontSize = (int)(_numFont?.Value ?? 72),
            FontName = _cmbFont?.SelectedItem?.ToString() ?? "Arial",
            SungColor = _cmbSung?.SelectedItem?.ToString() ?? "White",
            UnsungColor = _cmbUnsung?.SelectedItem?.ToString() ?? "DeepSkyBlue",
            NextLineColor = _cmbNextColour?.SelectedItem?.ToString() ?? "Silver",
            ShowNextLine = _chkShowNext?.Checked ?? true,
            Boxed = _chkBoxed?.Checked ?? false,
            VerticalPosition = _cmbPosition?.SelectedItem?.ToString() ?? "Bottom",
            FadeMs = (int)(_numFade?.Value ?? 140),
            OffsetSeconds = (double)(_numOffset?.Value ?? 0),
            NormalizeAudio = _chkNormalize?.Checked ?? true,
            SaveLrc = _chkSaveLrc?.Checked ?? true
        };
    }

    private string WorkDir()
    {
        AppPaths.EnsureFolders();
        return AppPaths.Work;
    }

    private SeparationMode CurrentSeparation() =>
        (_cmbSeparation?.SelectedIndex) switch
        {
            1 => SeparationMode.CentreChannel,
            2 => SeparationMode.Demucs,
            3 => SeparationMode.OwnInstrumental,
            _ => SeparationMode.Auto
        };
}
// ------------------------------------------------------------------
//  MainForm - event handlers and the automatic run
// ------------------------------------------------------------------

public partial class MainForm
{
    private void OnDragEnter(object? s, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDragDrop(object? s, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
        var media = files.FirstOrDefault(f =>
            Path.GetExtension(f).ToLowerInvariant() is
            ".mp3" or ".wav" or ".flac" or ".m4a" or ".aac" or ".ogg" or
            ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm");
        if (media is null)
        {
            SetStatus("That file is not a supported song or video.");
            return;
        }
        await LoadSongAsync(media);
    }

    private void OnKeyDown(object? s, KeyEventArgs e)
    {
        if (!e.Control) return;
        switch (e.KeyCode)
        {
            case Keys.O: BtnLoad_Click(this, EventArgs.Empty); e.Handled = true; break;
            case Keys.E: BtnExport_Click(this, EventArgs.Empty); e.Handled = true; break;
            case Keys.R: BtnAuto_Click(this, EventArgs.Empty); e.Handled = true; break;
            case Keys.L: BtnFetch_Click(this, EventArgs.Empty); e.Handled = true; break;
        }
    }

    // ---------------- song ----------------

    private async void BtnLoad_Click(object? s, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Choose a song from this PC",
            Filter = "Audio & video|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.mp4;*.mkv;*.mov;*.avi;*.webm|All files|*.*"
        };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        await LoadSongAsync(ofd.FileName);
    }

    private async Task LoadSongAsync(string path)
    {
        _txtSource!.Text = path;
        _ctx.SongFile = path;

        var name = Path.GetFileNameWithoutExtension(path);
        var cleaned = Regex.Replace(name, @"^\d+[\s.\-_]*", "");   // strip "01 - "
        var parts = cleaned.Split(new[] { " - ", " – ", " _ " },
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            if (string.IsNullOrWhiteSpace(_txtArtist!.Text)) _txtArtist.Text = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(_txtTrack!.Text))
                _txtTrack.Text = parts.Length > 2
                    ? string.Join(" - ", parts[1..]).Trim()
                    : parts[1].Trim();
        }
        else if (string.IsNullOrWhiteSpace(_txtTrack!.Text))
        {
            _txtTrack.Text = cleaned;
        }

        SetBusy(true);
        SetStatus("Extracting audio with FFmpeg...");
        try
        {
            var wav = Path.Combine(WorkDir(), "song.wav");
            var (ok, err) = await FFmpeg.ExtractWavAsync(path, wav);
            if (!ok)
            {
                SetStatus(err);
                return;
            }
            _ctx.OriginalWav = wav;
            var dur = await FFmpeg.ProbeDurationAsync(wav);
            _lblSongInfo!.Text = $"Duration: {dur:mm\\:ss}";
            AddRecent(path);
            SetStatus($"Loaded {Path.GetFileName(path)} ({dur:mm\\:ss}). " +
                      "Press MAKE KARAOKE VIDEO for the automatic run, or use the steps below.");
        }
        catch (Exception ex)
        {
            Diag.WriteError("Loading the song", ex);
            SetStatus("Could not load the song: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---------------- lyrics ----------------

    private async void BtnFetch_Click(object? s, EventArgs e)
    {
        var artist = _txtArtist!.Text.Trim();
        var track = _txtTrack!.Text.Trim();
        if (track.Length == 0)
        {
            MessageBox.Show("Please enter a track title first (or load a song whose " +
                            "file name contains \"Artist - Title\").");
            return;
        }
        SetBusy(true);
        SetStatus($"Searching LRCLIB for \"{artist} - {track}\"...");
        try
        {
            var dur = _ctx.OriginalWav.Length > 0
                ? await FFmpeg.ProbeDurationAsync(_ctx.OriginalWav)
                : TimeSpan.Zero;
            var result = await LyricsService.SearchAsync(artist, track, dur);
            if (result is null)
            {
                SetStatus("No lyrics found on LRCLIB. Run AI auto-sync (Whisper) to " +
                          "let the AI write the words and timings for you.");
                return;
            }
            if (result.Synced)
            {
                _txtLrc!.Text = result.LrcOrPlain;
                SetStatus($"Synced lyrics loaded ({result.Artist} - {result.Track}).");
            }
            else
            {
                var songLen = dur > TimeSpan.Zero ? dur : TimeSpan.FromMinutes(3.5);
                _txtLrc!.Text = LyricsService.PlainToSkeleton(result.LrcOrPlain, songLen);
                SetStatus("Found unsynced lyrics and laid them out evenly. " +
                          "Run AI auto-sync for exact word timing.");
            }
        }
        catch (Exception ex)
        {
            Diag.WriteError("Lyrics lookup", ex);
            SetStatus("Lyrics lookup failed: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BtnAutosync_Click(object? s, EventArgs e)
    {
        var (ok, msg) = await RunAiOnlyAsync();
        if (!ok) SetStatus(msg);
    }

    /// <summary>Runs Whisper and rewrites the editor content with word-level
    /// timings. Returns false (and a message) when it could not run.</summary>
    private async Task<(bool ok, string msg)> RunAiOnlyAsync()
    {
        var exe = _txtWhisperExe!.Text.Trim();
        var model = _txtWhisperModel!.Text.Trim();
        if (!File.Exists(exe))
        {
            MessageBox.Show("Please locate whisper-cli.exe first " +
                            "(https://github.com/ggml-org/whisper.cpp/releases).");
            return (false, "Whisper executable not set.");
        }
        if (!File.Exists(model))
        {
            MessageBox.Show("Please locate a Whisper GGML model (.bin) first " +
                            "(https://huggingface.co/ggml-org/whisper.cpp).");
            return (false, "Whisper model not set.");
        }
        var audio = _ctx.OriginalWav.Length > 0
            ? _ctx.OriginalWav
            : Path.Combine(WorkDir(), "song.wav");
        if (!File.Exists(audio))
        {
            MessageBox.Show("Load a song first.");
            return (false, "No song loaded.");
        }

        SetBusy(true);
        SetStatus("Whisper is transcribing your song (a few minutes on CPU)...");
        try
        {
            var words = await WhisperSync.TranscribeAsync(
                exe, model, audio, Path.Combine(WorkDir(), "whisper"),
                _cmbLanguage?.SelectedItem?.ToString() ?? "auto",
                _txtVad?.Text.Trim() ?? "");
            if (words.Count == 0)
                throw new Exception("Whisper detected no words. Try a larger model " +
                                    "(ggml-small.bin / ggml-medium.bin).");

            var text = _txtLrc!.Text.Trim();
            if (text.Length > 0)
            {
                _txtLrc.Text = WhisperSync.Align(words, text);
                return (true, $"Auto-sync complete: {words.Count} words aligned to " +
                              "your lyrics. Check the timestamps, then export.");
            }

            _txtLrc.Text = WhisperSync.ToLrcFromWhisper(words);
            return (true, $"No lyrics were available, so the AI wrote the words " +
                          $"and timings for you ({words.Count} words).");
        }
        catch (Exception ex)
        {
            Diag.WriteError("AI auto-sync", ex);
            return (false, "Auto-sync failed: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BtnEvenly_Click(object? s, EventArgs e)
    {
        var text = _txtLrc!.Text.Trim();
        if (text.Length == 0)
        {
            SetStatus("There are no lyrics to spread out.");
            return;
        }
        var dur = _ctx.OriginalWav.Length > 0
            ? await FFmpeg.ProbeDurationAsync(_ctx.OriginalWav)
            : TimeSpan.Zero;
        var plain = string.Join("\n", text.Replace("\r\n", "\n").Split('\n')
            .Select(l => Regex.Replace(l, @"\[[^\]]*\]|<[^>]*>", "").Trim())
            .Where(l => l.Length > 0));
        _txtLrc.Text = LyricsService.PlainToSkeleton(plain,
            dur > TimeSpan.Zero ? dur : TimeSpan.FromMinutes(3.5));
        SetStatus("The lines are spread evenly again - run AI auto-sync for the " +
                  "exact word timing.");
    }

    // ---------------- instrumental ----------------

    private async void BtnVocal_Click(object? s, EventArgs e)
    {
        var audio = _ctx.OriginalWav.Length > 0
            ? _ctx.OriginalWav
            : Path.Combine(WorkDir(), "song.wav");
        if (!File.Exists(audio))
        {
            MessageBox.Show("Load a song first.");
            return;
        }
        SetBusy(true);
        SetStatus("Removing vocals...");
        try
        {
            var mode = CurrentSeparation();
            string outWav;
            if (mode == SeparationMode.Demucs ||
                (mode == SeparationMode.Auto && await Demucs.IsAvailableAsync()))
            {
                SetStatus("Separating with Demucs (AI) - this can take a few minutes...");
                var (okDemucs, message, produced) = await Demucs.SeparateAsync(
                    audio, Path.Combine(WorkDir(), "demucs"));
                if (okDemucs)
                {
                    outWav = produced;
                    SetStatus("Done - Demucs removed the vocals with AI quality.");
                    return;
                }
                SetStatus(message + " - falling back to centre-channel cancellation.");
            }

            outWav = Path.Combine(WorkDir(), "instrumental.wav");
            var (ok, err) = await FFmpeg.RemoveVocalsAsync(audio, outWav);
            SetStatus(ok
                ? "Vocals reduced. For studio quality, export an instrumental with " +
                  "the free Ultimate Vocal Remover 5 and pick it with " +
                  "\"Use my instrumental...\"."
                : err);
        }
        catch (Exception ex)
        {
            Diag.WriteError("Vocal removal", ex);
            SetStatus("Vocal removal failed: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BtnInstrumental_Click(object? s, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Choose an instrumental WAV (from UVR5 / Demucs)",
            Filter = "Audio|*.wav;*.flac;*.mp3|All files|*.*"
        };
        if (ofd.ShowDialog() == DialogResult.OK)
        {
            _txtInstrumental!.Text = ofd.FileName;
            if (_cmbSeparation is not null && _cmbSeparation.SelectedIndex != 3)
                _cmbSeparation.SelectedIndex = 3;
            SetStatus("Instrumental set. The vocal removal mode now says " +
                      "\"Use my instrumental file\".");
        }
    }

    private void BtnPreview_Click(object? s, EventArgs e)
    {
        var path = _ctx.InstrumentalWav.Length > 0
            ? _ctx.InstrumentalWav
            : _ctx.OriginalWav;
        if (path.Length == 0 || !File.Exists(path))
        {
            MessageBox.Show("Load a song first.");
            return;
        }
        try
        {
            // the player has to stay alive, otherwise the sound stops at once
            _previewPlayer?.Stop();
            _previewPlayer = new System.Media.SoundPlayer(path);
            _previewPlayer.Play();
            SetStatus("Playing the instrumental. Click Preview again to restart it.");
        }
        catch (Exception ex)
        {
            SetStatus("Preview failed: " + ex.Message);
        }
    }

    // ---------------- AI / background ----------------

    private void BtnWhisperExe_Click(object? s, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Locate whisper-cli.exe from whisper.cpp",
            Filter = "Executables|*.exe|All files|*.*"
        };
        if (ofd.ShowDialog() == DialogResult.OK) _txtWhisperExe!.Text = ofd.FileName;
    }

    private void BtnWhisperModel_Click(object? s, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Locate a Whisper GGML model file (e.g. ggml-base.bin)",
            Filter = "Models|*.bin|All files|*.*"
        };
        if (ofd.ShowDialog() == DialogResult.OK) _txtWhisperModel!.Text = ofd.FileName;
    }

    private void BtnVad_Click(object? s, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Optional VAD model (ggml-silero-v5.1.2.bin)",
            Filter = "Models|*.bin|All files|*.*"
        };
        if (ofd.ShowDialog() == DialogResult.OK) _txtVad!.Text = ofd.FileName;
    }

    private void BtnBg_Click(object? s, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Choose a background image or video",
            Filter = "Images & videos|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.mp4;*.mkv;*.mov;*.avi;*.webm|All files|*.*"
        };
        if (ofd.ShowDialog() == DialogResult.OK) _txtBackground!.Text = ofd.FileName;
    }

    // ---------------- presets, log, setup ----------------

    private void CmbPreset_SelectedIndexChanged(object? s, EventArgs e)
    {
        var name = _cmbPreset?.SelectedItem?.ToString();
        if (name is null || !AssGenerator.Presets.TryGetValue(name, out var p)) return;
        SetColour(_cmbSung, p.Sung);
        SetColour(_cmbUnsung, p.Unsung);
        SetColour(_cmbNextColour, p.Next);
        if (_cmbFont is not null && _cmbFont.Items.Contains(p.Font))
            _cmbFont.SelectedItem = p.Font;
        if (_numFont is not null)
            _numFont.Value = Math.Clamp((int)(72 * p.Size / 100.0), 24, 200);
    }

    /// <summary>Selects a colour in a drop-down, adding it first when the
    /// preset uses a custom hex value.</summary>
    private static void SetColour(ComboBox? combo, string colour)
    {
        if (combo is null || colour.Length == 0) return;
        if (!combo.Items.Contains(colour)) combo.Items.Add(colour);
        combo.SelectedItem = colour;
    }

    private void BtnCopyLog_Click(object? s, EventArgs e)
    {
        try
        {
            if (_txtLog?.Text is { Length: > 0 } text) Clipboard.SetText(text);
            SetStatus("The log was copied to the clipboard.");
        }
        catch (Exception ex)
        {
            SetStatus("Could not copy the log: " + ex.Message);
        }
    }

    private void BtnClearLog_Click(object? s, EventArgs e)
    {
        if (_txtLog is not null) _txtLog.Clear();
    }

    private void BtnSetup_Click(object? s, EventArgs e)
    {
        MessageBox.Show(
            "WHAT YOU NEED\n\n" +
            "1. .NET 8 runtime (only to run the .exe you build):\n" +
            "   winget install Microsoft.DotNet.SDK.8\n\n" +
            "2. FFmpeg - required, gives you ffmpeg and ffprobe:\n" +
            "   winget install Gyan.FFmpeg -e\n" +
            "   (close and reopen the terminal afterwards)\n\n" +
            "3. whisper.cpp (optional, for exact word timing):\n" +
            "   https://github.com/ggml-org/whisper.cpp/releases  -> whisper-bin-x64.zip\n" +
            "   https://huggingface.co/ggml-org/whisper.cpp         -> ggml-base.bin\n" +
            "   (add a VAD model too: ggml-silero-v5.1.2.bin)\n\n" +
            "4. Demucs (optional, best vocal removal):\n" +
            "   pip install demucs\n\n" +
            "Keyboard: Ctrl+O load  |  Ctrl+E export  |  Ctrl+R auto run  |  Ctrl+L lyrics",
            "KaraokeForge setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task StartupCheckAsync()
    {
        var (ok, detail) = await FFmpeg.CheckToolsAsync();
        Ui(() =>
        {
            if (_lblSetup is null) return;
            _lblSetup.Text = ok ? "FFmpeg OK" : "FFmpeg missing!";
            _lblSetup.ForeColor = ok ? Color.DarkGreen : Color.Firebrick;
        });
        Diag.Write(ok ? "Setup: " + detail : "Setup problem: " + detail);
        if (!ok)
        {
            SetStatus("FFmpeg was not found on PATH - click \"Setup help\" for the " +
                      "one-line install command.");
        }

        if (await Demucs.IsAvailableAsync())
            Diag.Write("Demucs was found - AI vocal separation will be used automatically.");
    }

    // ---------------- recent files ----------------

    private void AddRecent(string path)
    {
        try
        {
            AppPaths.EnsureFolders();
            var list = File.Exists(AppPaths.RecentFile)
                ? File.ReadAllLines(AppPaths.RecentFile).ToList()
                : new List<string>();
            list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, path);
            if (list.Count > 12) list = list.Take(12).ToList();
            File.WriteAllLines(AppPaths.RecentFile, list);
            RefreshRecent();
        }
        catch (Exception ex)
        {
            Diag.WriteError("Recent files", ex);
        }
    }

    private void RefreshRecent()
    {
        if (_cmbRecent is null) return;
        _cmbRecent.Items.Clear();
        try
        {
            if (File.Exists(AppPaths.RecentFile))
            {
                foreach (var line in File.ReadAllLines(AppPaths.RecentFile))
                {
                    if (line.Trim().Length > 0) _cmbRecent.Items.Add(line);
                }
            }
        }
        catch { /* ignore */ }
    }

    private async void CmbRecent_SelectedIndexChanged(object? s, EventArgs e)
    {
        if (_cmbRecent?.SelectedItem is not string path) return;
        if (!File.Exists(path)) return;
        await LoadSongAsync(path);
        _cmbRecent.SelectedIndex = -1;
    }

    // ---------------- export ----------------

    private async void BtnExport_Click(object? s, EventArgs e)
    {
        if (_txtLrc!.Text.Trim().Length == 0)
        {
            MessageBox.Show("There are no lyrics yet. Use \"Fetch lyrics\" or " +
                            "\"AI auto-sync\" first - or just press " +
                            "\"MAKE KARAOKE VIDEO\", which does everything by itself.");
            return;
        }
        var audio = _ctx.OriginalWav.Length > 0
            ? _ctx.OriginalWav
            : Path.Combine(WorkDir(), "song.wav");
        if (!File.Exists(audio))
        {
            MessageBox.Show("Load a song first.");
            return;
        }
        using var sfd = new SaveFileDialog
        {
            Title = "Save karaoke video",
            Filter = "MP4 video|*.mp4",
            FileName = Path.GetFileName(KaraokePipeline.DefaultOutputPath(audio,
                (_txtTrack?.Text ?? "").Trim()))
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        await RenderAsync(sfd.FileName);
    }

    private async void BtnQuickPreview_Click(object? s, EventArgs e)
    {
        if (_ctx.SongFile.Length == 0)
        {
            SetStatus("Load a song first.");
            return;
        }
        await RenderAsync(Path.Combine(WorkDir(), "quick preview.mp4"),
            automatic: false, seconds: 20, showDialog: false);
    }

    /// <summary>The one-click path: everything happens automatically.</summary>
    private async void BtnAuto_Click(object? s, EventArgs e)
    {
        if (_ctx.SongFile.Length == 0)
        {
            var r = MessageBox.Show(
                "No song is loaded yet. Choose a song from this PC now?",
                "KaraokeForge", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r != DialogResult.OK) return;
            using var ofd = new OpenFileDialog
            {
                Title = "Choose a song from this PC",
                Filter = "Audio & video|*.mp3;*.wav;*.flac;*.m4a;*.aac;*.ogg;*.mp4;*.mkv;*.mov;*.avi;*.webm|All files|*.*"
            };
            if (ofd.ShowDialog() != DialogResult.OK) return;
            await LoadSongAsync(ofd.FileName);
            if (_ctx.SongFile.Length == 0) return;
        }

        var output = KaraokePipeline.DefaultOutputPath(_ctx.SongFile, (_txtTrack?.Text ?? "").Trim());
        await RenderAsync(output, automatic: true);
    }

    private async Task RenderAsync(string outputPath, bool automatic = false,
        int seconds = 0, bool showDialog = true)
    {
        _cts = new CancellationTokenSource();
        SetBusy(true);
        if (_progress is not null) _progress.Value = 0;

        _ctx.Artist = _txtArtist!.Text.Trim();
        _ctx.Track = _txtTrack!.Text.Trim();
        _ctx.WorkDir = WorkDir();
        _ctx.OriginalWav = Path.Combine(_ctx.WorkDir, "song.wav");
        _ctx.InstrumentalOverride = _txtInstrumental!.Text.Trim();
        _ctx.BackgroundOverride = _txtBackground!.Text.Trim();
        _ctx.WhisperExe = _txtWhisperExe!.Text.Trim();
        _ctx.WhisperModel = _txtWhisperModel!.Text.Trim();
        _ctx.WhisperVadModel = _txtVad?.Text.Trim() ?? "";
        _ctx.WhisperLanguage = _cmbLanguage?.SelectedItem?.ToString() ?? "auto";
        _ctx.AlwaysRunAi = _chkAlwaysAi?.Checked ?? false;
        _ctx.AutoMode = automatic;
        _ctx.ExistingLyrics = _txtLrc!.Text;
        _ctx.Separation = CurrentSeparation();
        _ctx.Options = CurrentOptions();
        _ctx.Options.MaxSeconds = seconds;
        _ctx.Cancel = _cts.Token;

        try
        {
            var result = await KaraokePipeline.RunAsync(
                _ctx,
                msg => Ui(() => SetStatus(msg)),
                p => Ui(() =>
                {
                    if (_progress is not null)
                        _progress.Value = Math.Clamp((int)(p * 100), 0, 100);
                }));

            if (!result.Ok)
            {
                SetStatus(result.Message);
                if (automatic && showDialog)
                    MessageBox.Show(result.Message, "KaraokeForge",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _txtLrc!.Text = result.Lyrics;
            SetStatus($"Finished: {result.OutputFile}");

            if (!showDialog)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(result.OutputFile)
                    { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Diag.WriteError("Opening the preview", ex);
                }
                return;
            }

            var answer = MessageBox.Show(
                $"Your karaoke video is ready:\n\n{result.OutputFile}\n\n" +
                "Open the folder?",
                "KaraokeForge", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer == DialogResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe",
                        "/select,\"" + result.OutputFile + "\"") { UseShellExecute = true });
                }
                catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            Diag.WriteError("Export", ex);
            SetStatus("Export failed: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void BtnCancel_Click(object? s, EventArgs e)
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        SetStatus("Cancelling...");
    }
}
#endregion
