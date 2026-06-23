# MidiCleaner

Single-file Windows MIDI cleanup utility with a screen-reader-friendly WinForms interface.

Current version: 1.1.

Project page: <https://github.com/OnjLouis/midi-cleaner>

## Upgrading from 1.0

MidiCleaner 1.0 has a known updater bug that can show `Ambiguous match found` when checking for updates. If you are on 1.0, download and install 1.1 manually once. Future update checks work from 1.1 onward.

## Build

Run:

```powershell
.\Build.ps1
```

The build script uses the Windows .NET Framework C# compiler and writes the executable to:

```text
portable\MidiCleaner.exe
```

To choose another output path:

```powershell
.\Build.ps1 -OutputPath "C:\Tools\MidiCleaner\MidiCleaner.exe"
```

The build script does not create an INI file by default. Use `-CreateDefaultIni` when you want a starter `MidiCleaner.ini` beside the executable. It does not overwrite existing portable settings.

## Release

Run:

```powershell
.\Release.ps1
```

To publish a GitHub release after the package checks pass:

```powershell
.\Release.ps1 -Publish
```

The release package includes `MidiCleaner.exe`, `README.md`, and `LICENSE.txt`. It must not include `MidiCleaner.ini`, logs, temp files, or token files.

## Behavior

- `Ctrl+O` or `File > Open MIDI File(s)` opens one or more MIDI files.
- `Ctrl+F` or `File > Open Folder` processes every `.mid` and `.midi` file in the selected folder.
- `Ctrl+,` opens Preferences.
- `F1` opens built-in help.
- `F4` reviews the selected result in a read-only text box.
- Preferences has tabs for MIDI cleanup choices and output defaults.
- Preferences also has an Automation tab for silent-conversion logging and adding MidiCleaner to the Windows Send To menu.
- Preferences has an Updates tab for GitHub release checks.
- MIDI cleanup preferences use a checked list. Checked items are removed or normalized; unchecked items are kept.
- MIDI cleanup can remove program changes, bank select, CC 7 volume, CC 10 pan, CC 11 expression, CC 91 reverb, CC 92 tremolo, CC 93 chorus, pitch bend, channel aftertouch, polyphonic aftertouch, sequencer-specific metadata, and original channel assignments.
- Output defaults can save MIDI type 1 or MIDI type 0. Type 1 is the default.
- Output defaults can ask where to save after choosing input, or immediately use saved output defaults.
- After files are selected, the user chooses between `Create Output folders alongside the source files` and `Put all cleaned files in one folder`.
- The output dialog includes `Add "cleaned" to output file names`, checked by default.
- Alongside-source output files are written to an `Output` folder beside the originals as `name cleaned.mid`.
- If `Add "cleaned" to output file names` is unchecked, output files keep the original file name in the chosen output location.
- Source files are never overwritten.
- Existing files in the output location are not overwritten; a number is added when needed.
- `MidiCleaner.ini` is saved beside `MidiCleaner.exe` with the last input folder, output settings, and MIDI cleanup preferences.
- No settings are written to AppData or the registry.
- The Send To option creates `Midi&Cleaner.lnk` in the current user's Windows SendTo folder, pointing to `MidiCleaner.exe --silent`.
- `Help > Check for Updates...` checks GitHub Releases.
- `Help > Version History...` shows the latest GitHub release notes.

## Command Line

Silent conversion opens no main window:

```powershell
MidiCleaner.exe --silent "C:\Music\song.mid"
MidiCleaner.exe --silent "C:\Music\Folder"
```

Useful switches:

- `--file path`, `--folder path`, or positional paths add inputs.
- `--output-mode alongside|folder`, `--output-folder path`, and `--alongside-source` override output location.
- `--add-cleaned true|false` or `--no-add-cleaned` controls output names.
- `--remove-cc 7,10,11,91,92,93` replaces the INI controller removal list.
- `--keep-cc 10,11` keeps listed controllers.
- `--remove-program-changes true|false` and `--remove-bank-select true|false` override those cleanup choices.
- `--keep-pitch-bend true|false`, `--keep-channel-aftertouch true|false`, and `--keep-poly-aftertouch true|false` override performance-message choices.
- `--remove-sequencer-metadata true|false` or `--keep-sequencer-metadata` controls QWS/sequencer metadata removal.
- `--normalize-channels true|false` or `--keep-channels` controls whether channel events are remapped to channel 1.
- `--midi-type 0|1` controls the output MIDI file type.
- `--log` writes to `MidiCleaner.log` beside the EXE, `--log=path` writes to a chosen file, and `--no-log` suppresses logging.

Unspecified command-line options use `MidiCleaner.ini`.
- Output is MIDI type 1 by default.
- Track names are preserved.
- Tracks without note-on MIDI data are removed.
- By default, all channel events are remapped to channel 1.
- By default, program changes, bank select, volume, reverb, chorus, sequencer-specific metadata, and any other selected cleanup items are removed.
