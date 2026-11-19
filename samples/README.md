# Sample output

The sample MP4s in this folder are produced by the pipeline itself:

```powershell
dotnet run                      # then press "MAKE KARAOKE VIDEO (automatic)"
```

They are **not** checked in (see `.gitignore`) because they are build artefacts.
To regenerate a demo on any machine:

1. Put any song in a folder.
2. Start KaraokeForge, load the song, press **MAKE KARAOKE VIDEO**.
3. The MP4 is saved next to the song as `<track> (karaoke).mp4`, together with
   the word-timed `<track> (karaoke).lrc` file.

The GitHub Actions workflow (`.github/workflows/build-windows.yml`) builds the
Windows executable on every push - download it from the Actions tab:

**Actions → Build Windows app → Artifacts → KaraokeForge-windows-x64**
