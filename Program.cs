using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("MidiCleaner")]
[assembly: AssemblyDescription("Accessible MIDI cleanup utility")]
[assembly: AssemblyCompany("Andre Louis")]
[assembly: AssemblyProduct("MidiCleaner")]
[assembly: AssemblyCopyright("Copyright (c) Andre Louis")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1")]

namespace MidiCleaner
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (CommandLineOptions.HasCommandLineMode(args))
            {
                Environment.ExitCode = CommandLineRunner.Run(args);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form
    {
        private const string AppName = "MidiCleaner";
        private const string Version = "1.1";
        private const string ProjectUrl = "https://github.com/OnjLouis/midi-cleaner";

        private readonly Label statusLabel;
        private readonly ListView resultsList;
        private readonly Button openFilesButton;
        private readonly Button openFolderButton;
        private readonly Button reviewButton;
        private readonly List<string> resultLines = new List<string>();
        private readonly AppSettings settings = AppSettings.Load();
        private readonly Timer updateCheckTimer;
        private bool automaticUpdateCheckStartedThisRun;

        private static readonly string HelpText =
@"MidiCleaner

Purpose
MidiCleaner removes setup and sequencer data from MIDI files so they can be loaded cleanly elsewhere.

Keyboard
Ctrl+O: Open one or more MIDI files.
Ctrl+F: Open a folder and process all .mid and .midi files in that folder.
Ctrl+F1: Open the project page on GitHub.
F1: Show this help.
F4: Review the selected result, or all results if none is selected.
Ctrl+Comma: Open Preferences.
Alt+F4: Close the program.

Menus
File > Open MIDI File(s): choose one or more MIDI files.
File > Open Folder: process every .mid and .midi file in the chosen folder.
File > Exit: close the program.
Options > Preferences: choose which MIDI messages and controllers are removed.
Help > Check for Updates: check GitHub Releases for a newer version.
Help > Version History: show the latest GitHub release notes.
Help > Project on GitHub: open the project page.
Help > Donate: open onj.me/donate if you want to support development.
Help > MidiCleaner Help: show this help.
Help > About: show program version.

Automation
Silent command-line conversion can clean files or folders and then exit without opening the main window.
Preferences > Automation can enable silent-conversion logging and add MidiCleaner to the Windows Send To menu.
The Send To entry runs MidiCleaner silently using the current INI preferences.
Preferences > Updates controls automatic GitHub release checks and quiet update installs.

Output Location
After you choose files or a folder, MidiCleaner asks where to save the cleaned files.
Choose ""Create Output folders alongside the source files"" to put each cleaned file in an Output folder next to its source file.
Choose ""Put all cleaned files in one folder"" to browse to a folder and store every cleaned file there.
Source files are never overwritten. Existing files in the output location are never overwritten; MidiCleaner adds a number when needed.
The output-location dialog also lets you choose whether to add ""cleaned"" to output file names.
These choices are saved in MidiCleaner.ini beside MidiCleaner.exe so the app stays portable and does not use AppData or the registry.

What Gets Removed
Program changes, bank select, volume, pan, expression, reverb, tremolo, chorus, pitch bend, channel aftertouch, polyphonic aftertouch, sequencer-specific metadata, and channel normalization can be selected for removal or cleanup from Preferences.
Sequencer-specific metadata, including QWS metadata such as QWSmark, QWSloop, and QWSport.
Tracks that do not contain MIDI note data.

What Gets Kept
Track names.
Notes and timing.
Other standard metadata such as tempo and time signature.
Unchecked cleanup items are kept.

Output Format
Output is saved as MIDI type 1 by default. Preferences can save MIDI type 0 instead.
MIDI channel events are changed to channel 1 by default. Preferences can keep original channel assignments instead.";

        public MainForm()
        {
            Text = AppName;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 460);
            Size = new Size(860, 520);
            KeyPreview = true;
            AccessibleName = "MidiCleaner";
            AccessibleDescription = "Accessible MIDI cleanup utility.";

            MainMenuStrip = BuildMenu();
            Controls.Add(MainMenuStrip);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12, 10, 12, 12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true
            };
            root.Controls.Add(buttons, 0, 0);

            openFilesButton = new Button
            {
                Text = "&Open MIDI File(s)...",
                AutoSize = true,
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "Open MIDI files",
                AccessibleDescription = "Choose one or more MIDI files to clean."
            };
            openFilesButton.Click += delegate { OpenFiles(); };
            buttons.Controls.Add(openFilesButton);

            openFolderButton = new Button
            {
                Text = "Open &Folder...",
                AutoSize = true,
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "Open folder",
                AccessibleDescription = "Choose a folder and clean every MIDI file in it."
            };
            openFolderButton.Click += delegate { OpenFolder(); };
            buttons.Controls.Add(openFolderButton);

            reviewButton = new Button
            {
                Text = "&Review Results",
                AutoSize = true,
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "Review results",
                AccessibleDescription = "Review the selected result, or all results if no result is selected."
            };
            reviewButton.Click += delegate { ReviewResults(); };
            buttons.Controls.Add(reviewButton);

            var helpButton = new Button
            {
                Text = "Help",
                AutoSize = true,
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "Help",
                AccessibleDescription = "Open built-in MidiCleaner help."
            };
            helpButton.Click += delegate { ShowHelp(); };
            buttons.Controls.Add(helpButton);

            var preferencesButton = new Button
            {
                Text = "&Preferences...",
                AutoSize = true,
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "Preferences",
                AccessibleDescription = "Choose MIDI cleanup and output defaults."
            };
            preferencesButton.Click += delegate { ShowPreferences(); };
            buttons.Controls.Add(preferencesButton);

            statusLabel = new Label
            {
                Text = "Choose MIDI files or a folder to clean.",
                AutoSize = true,
                Margin = new Padding(0, 10, 0, 8),
                AccessibleRole = AccessibleRole.StaticText,
                AccessibleName = "Status",
                AccessibleDescription = "Current MidiCleaner status."
            };
            root.Controls.Add(statusLabel, 0, 1);

            resultsList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                AccessibleRole = AccessibleRole.Table,
                AccessibleName = "Cleaning results",
                AccessibleDescription = "List of source files and cleaning results."
            };
            resultsList.Columns.Add("Source", 360);
            resultsList.Columns.Add("Result", 420);
            resultsList.DoubleClick += delegate { ReviewResults(); };
            root.Controls.Add(resultsList, 0, 2);

            KeyDown += MainForm_KeyDown;
            updateCheckTimer = new Timer();
            updateCheckTimer.Interval = 60 * 60 * 1000;
            updateCheckTimer.Tick += delegate { CheckAutomaticUpdateSchedule(); };
            StartAutomaticUpdateChecks();
        }

        private MenuStrip BuildMenu()
        {
            var menu = new MenuStrip { AccessibleName = "Menu bar" };
            var file = new ToolStripMenuItem("&File");
            var openFiles = new ToolStripMenuItem("&Open MIDI File(s)...", null, delegate { OpenFiles(); }, Keys.Control | Keys.O);
            var openFolder = new ToolStripMenuItem("Open &Folder...", null, delegate { OpenFolder(); }, Keys.Control | Keys.F);
            var exit = new ToolStripMenuItem("E&xit", null, delegate { Close(); });
            file.DropDownItems.Add(openFiles);
            file.DropDownItems.Add(openFolder);
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(exit);

            var options = new ToolStripMenuItem("&Options");
            var preferences = new ToolStripMenuItem("&Preferences...", null, delegate { ShowPreferences(); }, Keys.Control | Keys.Oemcomma);
            options.DropDownItems.Add(preferences);

            var help = new ToolStripMenuItem("&Help");
            var checkUpdates = new ToolStripMenuItem("&Check for Updates...", null, delegate { CheckForUpdates(true, true); }, Keys.Shift | Keys.F1);
            var versionHistory = new ToolStripMenuItem("&Version History...", null, delegate { ShowVersionHistoryDialog(); });
            var project = new ToolStripMenuItem("&Project on GitHub", null, delegate { OpenProjectPage(); }, Keys.Control | Keys.F1);
            var donate = new ToolStripMenuItem("&Donate...", null, delegate { OpenDonatePage(); });
            var helpItem = new ToolStripMenuItem("MidiCleaner &Help", null, delegate { ShowHelp(); }, Keys.F1);
            var about = new ToolStripMenuItem("&About MidiCleaner", null, delegate { ShowAbout(); });
            help.DropDownItems.Add(checkUpdates);
            help.DropDownItems.Add(versionHistory);
            help.DropDownItems.Add(project);
            help.DropDownItems.Add(donate);
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add(helpItem);
            help.DropDownItems.Add(about);

            menu.Items.Add(file);
            menu.Items.Add(options);
            menu.Items.Add(help);
            return menu;
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F1)
            {
                ShowHelp();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F4 && !e.Alt)
            {
                ReviewResults();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.Oemcomma)
            {
                ShowPreferences();
                e.Handled = true;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F1))
            {
                OpenProjectPage();
                return true;
            }

            if (keyData == (Keys.Control | Keys.Oemcomma))
            {
                ShowPreferences();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OpenFiles()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Open MIDI file or files";
                dialog.Filter = "MIDI files (*.mid;*.midi)|*.mid;*.midi|All files (*.*)|*.*";
                dialog.Multiselect = true;
                if (Directory.Exists(settings.LastInputFolder))
                {
                    dialog.InitialDirectory = settings.LastInputFolder;
                }
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                if (dialog.FileNames.Length > 0)
                {
                    settings.LastInputFolder = Path.GetDirectoryName(dialog.FileNames[0]);
                }
                ProcessPaths(new List<string>(dialog.FileNames));
            }
        }

        private void OpenFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose a folder containing MIDI files.";
                dialog.ShowNewFolderButton = false;
                if (Directory.Exists(settings.LastInputFolder))
                {
                    dialog.SelectedPath = settings.LastInputFolder;
                }
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                settings.LastInputFolder = dialog.SelectedPath;

                var paths = new List<string>();
                foreach (var file in Directory.GetFiles(dialog.SelectedPath))
                {
                    var extension = Path.GetExtension(file);
                    if (extension.Equals(".mid", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".midi", StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(file);
                    }
                }

                paths.Sort(StringComparer.CurrentCultureIgnoreCase);
                if (paths.Count == 0)
                {
                    MessageBox.Show(this, "No .mid or .midi files were found in that folder.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                ProcessPaths(paths);
            }
        }

        private void ProcessPaths(List<string> paths)
        {
            var outputMode = settings.OutputMode;
            var outputFolder = settings.OutputFolder;
            var addCleaned = settings.AddCleanedToFileNames;

            if (settings.AskForOutputLocationAfterInput)
            {
                using (var outputDialog = new OutputLocationForm(paths.Count, settings))
                {
                    if (outputDialog.ShowDialog(this) != DialogResult.OK)
                    {
                        statusLabel.Text = "Cleaning cancelled before output location was chosen.";
                        return;
                    }

                    outputMode = outputDialog.Mode;
                    outputFolder = outputDialog.SelectedOutputFolder;
                    addCleaned = outputDialog.AddCleanedToFileNames;
                    settings.OutputMode = outputMode;
                    settings.OutputFolder = outputFolder;
                    settings.AddCleanedToFileNames = addCleaned;
                    SaveSettingsNonFatal();
                }
            }

            if (outputMode == OutputMode.SingleFolder && string.IsNullOrWhiteSpace(outputFolder))
            {
                MessageBox.Show(this, "Choose an output folder in Preferences, or turn on the option to ask where to save after choosing input.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                statusLabel.Text = "Cleaning cancelled because no output folder is configured.";
                return;
            }

            resultsList.Items.Clear();
            resultLines.Clear();
            var successes = 0;
            var failures = 0;
            var stopwatch = Stopwatch.StartNew();

            foreach (var path in paths)
            {
                try
                {
                    var result = MidiFileCleaner.CleanFile(path, outputMode, outputFolder, addCleaned, settings.CreateCleanOptions());
                    successes++;
                    var message = string.Format("Saved: {0}. Kept {1} track(s), removed {2} empty track(s), {3} note(s).",
                        result.OutputPath, result.KeptTracks, result.RemovedTracks, result.NoteCount);
                    AddResult(path, message);
                }
                catch (Exception ex)
                {
                    failures++;
                    AddResult(path, "Error: " + ex.Message);
                }
            }

            stopwatch.Stop();
            var elapsedText = FormatElapsed(stopwatch.Elapsed);
            var summary = string.Format("Finished. {0} file(s) cleaned, {1} failed. Time taken: {2}.", successes, failures, elapsedText);
            resultLines.Insert(0, summary);
            statusLabel.Text = summary;
            if (failures > 0)
            {
                MessageBox.Show(this, statusLabel.Text + " Review the results list for details.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(this, statusLabel.Text, AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalSeconds < 1)
            {
                return elapsed.TotalMilliseconds.ToString("0") + " ms";
            }

            if (elapsed.TotalMinutes < 1)
            {
                return elapsed.TotalSeconds.ToString("0.0") + " seconds";
            }

            return string.Format("{0}:{1:00}.{2:0} minutes", (int)elapsed.TotalMinutes, elapsed.Seconds, elapsed.Milliseconds / 100);
        }

        private void AddResult(string source, string result)
        {
            var item = new ListViewItem(source);
            item.SubItems.Add(result);
            resultsList.Items.Add(item);
            resultLines.Add(source + Environment.NewLine + result);
        }

        private void SaveSettingsNonFatal()
        {
            try
            {
                settings.Save();
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Settings could not be saved, but cleaning will continue. " + ex.Message;
            }
        }

        private void ReviewResults()
        {
            if (resultsList.SelectedItems.Count > 0)
            {
                var item = resultsList.SelectedItems[0];
                ShowTextDialog("Review Result", item.Text + Environment.NewLine + item.SubItems[1].Text);
                return;
            }

            if (resultLines.Count == 0)
            {
                ShowTextDialog("Review Results", "No results yet.");
                return;
            }

            ShowTextDialog("Review Results", string.Join(Environment.NewLine + Environment.NewLine, resultLines.ToArray()));
        }

        private void ShowHelp()
        {
            ShowTextDialog("MidiCleaner Help", HelpText);
        }

        private void ShowAbout()
        {
            ShowTextDialog("About MidiCleaner",
                AppName + " " + Version + Environment.NewLine + Environment.NewLine +
                "Accessible MIDI cleanup utility." + Environment.NewLine + Environment.NewLine +
                "Project page:" + Environment.NewLine +
                ProjectUrl + Environment.NewLine + Environment.NewLine +
                "Created by Andre Louis with Codex.");
        }

        private void ShowPreferences()
        {
            using (var dialog = new PreferencesForm(settings))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    dialog.ApplyTo(settings);
                    SaveSettingsNonFatal();
                    ApplySendToNonFatal();
                    StartAutomaticUpdateChecks();
                    statusLabel.Text = "Preferences saved.";
                }
            }
        }

        private void CheckForUpdates(bool showUpToDate, bool showErrors)
        {
            try
            {
                UseWaitCursor = true;
                var releases = UpdateService.FetchReleases(ProjectUrl, Version);
                var release = UpdateService.LatestVersionedRelease(releases) ?? UpdateService.FetchLatestRelease(ProjectUrl, Version);
                var latest = release == null ? string.Empty : (release.tag_name ?? string.Empty).Trim();
                var latestVersion = latest.TrimStart('v', 'V');
                System.Version current;
                System.Version remote;
                if (System.Version.TryParse(Version, out current) && System.Version.TryParse(latestVersion, out remote) && remote > current)
                {
                    if (settings.InstallUpdatesQuietly && TryStartUpdate(release, true))
                    {
                        return;
                    }

                    ShowUpdateAvailableDialog(release, latest, UpdateService.BuildReleaseNotes(releases, current, remote, Version));
                    return;
                }

                if (showUpToDate)
                {
                    MessageBox.Show(this, "MidiCleaner is up to date. Current version: " + Version + ".", "Check for Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                if (showErrors)
                {
                    MessageBox.Show(this, "Could not check for updates. GitHub releases may not exist yet, or the network request failed." + Environment.NewLine + Environment.NewLine + ex.Message, "Check for Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private void StartAutomaticUpdateChecks()
        {
            updateCheckTimer.Stop();
            CheckAutomaticUpdateSchedule();
            if (UpdateService.AutomaticUpdateInterval(settings.UpdateCheckFrequency).HasValue)
            {
                updateCheckTimer.Start();
            }
        }

        private void CheckAutomaticUpdateSchedule()
        {
            var frequency = UpdateService.NormalizeUpdateCheckFrequency(settings.UpdateCheckFrequency);
            if (frequency == "Never")
            {
                return;
            }

            if (frequency == "Startup")
            {
                if (!automaticUpdateCheckStartedThisRun)
                {
                    automaticUpdateCheckStartedThisRun = true;
                    BeginSilentAutomaticUpdateCheck(false);
                }
                return;
            }

            var interval = UpdateService.AutomaticUpdateInterval(frequency);
            if (!interval.HasValue)
            {
                return;
            }

            DateTime last;
            if (!DateTime.TryParse(settings.LastAutomaticUpdateCheckUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out last) ||
                DateTime.UtcNow - last >= interval.Value)
            {
                settings.LastAutomaticUpdateCheckUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                SaveSettingsNonFatal();
                BeginSilentAutomaticUpdateCheck(true);
            }
        }

        private void BeginSilentAutomaticUpdateCheck(bool recordAttemptAlreadySaved)
        {
            Task.Factory.StartNew(delegate
            {
                try
                {
                    if (!recordAttemptAlreadySaved)
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            settings.LastAutomaticUpdateCheckUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                            SaveSettingsNonFatal();
                        });
                    }

                    var releases = UpdateService.FetchReleases(ProjectUrl, Version);
                    var release = UpdateService.LatestVersionedRelease(releases) ?? UpdateService.FetchLatestRelease(ProjectUrl, Version);
                    var latest = release == null ? string.Empty : (release.tag_name ?? string.Empty).Trim();
                    System.Version current;
                    System.Version remote;
                    if (!System.Version.TryParse(Version, out current) || !System.Version.TryParse(latest.TrimStart('v', 'V'), out remote) || remote <= current)
                    {
                        return;
                    }

                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!IsDisposed)
                        {
                            if (settings.InstallUpdatesQuietly && TryStartUpdate(release, false))
                            {
                                return;
                            }

                            ShowUpdateAvailableDialog(release, latest, UpdateService.BuildReleaseNotes(releases, current, remote, Version));
                        }
                    });
                }
                catch
                {
                    // Automatic update checks stay quiet unless an update is available.
                }
            });
        }

        private void ShowUpdateAvailableDialog(GitHubReleaseInfo release, string latest, string releaseNotes)
        {
            var releaseUrl = release == null || string.IsNullOrWhiteSpace(release.html_url) ? ProjectUrl + "/releases" : release.html_url;
            var zipAsset = UpdateService.FindPortableZipAsset(release);

            using (var dialog = new Form())
            {
                dialog.Text = "Update available";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.Width = 720;
                dialog.Height = 520;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowIcon = false;
                dialog.ShowInTaskbar = false;
                dialog.AccessibleName = "Update available";

                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layout.Controls.Add(new Label { AutoSize = true, Dock = DockStyle.Top, Text = "MidiCleaner " + latest + " is available.", Padding = new Padding(0, 0, 0, 8), AccessibleRole = AccessibleRole.StaticText }, 0, 0);
                layout.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = releaseNotes, AccessibleRole = AccessibleRole.Text, AccessibleName = "Release notes" }, 0, 1);

                var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 8, 0, 0) };
                if (zipAsset != null)
                {
                    var installButton = new Button { Text = "&Download and install", AutoSize = true, AccessibleRole = AccessibleRole.PushButton, AccessibleName = "Download and install update" };
                    installButton.Click += delegate { dialog.DialogResult = DialogResult.OK; dialog.Close(); StartUpdate(zipAsset.browser_download_url); };
                    buttons.Controls.Add(installButton);
                    dialog.AcceptButton = installButton;
                }
                var releaseButton = new Button { Text = "Open &release page", AutoSize = true, AccessibleRole = AccessibleRole.PushButton, AccessibleName = "Open release page" };
                releaseButton.Click += delegate { Process.Start(new ProcessStartInfo { FileName = releaseUrl, UseShellExecute = true }); };
                var laterButton = new Button { Text = "&Later", DialogResult = DialogResult.Cancel, AutoSize = true, AccessibleRole = AccessibleRole.PushButton, AccessibleName = "Later" };
                buttons.Controls.Add(releaseButton);
                buttons.Controls.Add(laterButton);
                dialog.CancelButton = laterButton;
                layout.Controls.Add(buttons, 0, 2);
                dialog.Controls.Add(layout);
                dialog.ShowDialog(this);
            }
        }

        private void ShowVersionHistoryDialog()
        {
            try
            {
                UseWaitCursor = true;
                var releases = UpdateService.FetchReleases(ProjectUrl, Version);
                var release = UpdateService.LatestVersionedRelease(releases) ?? UpdateService.FetchLatestRelease(ProjectUrl, Version);
                var version = release == null ? Version : (release.tag_name ?? Version).Trim().TrimStart('v', 'V');
                var releaseUrl = release == null || string.IsNullOrWhiteSpace(release.html_url) ? ProjectUrl + "/releases" : release.html_url;
                var notes = UpdateService.FormatReleaseNotesForDialog(release == null ? string.Empty : release.body, "No release notes were provided for this update.");
                using (var dialog = new TextReviewForm("Version History - " + version, "Latest release: " + version + Environment.NewLine + Environment.NewLine + notes))
                {
                    dialog.ShowDialog(this);
                }
                statusLabel.Text = "Latest release page: " + releaseUrl;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not check updates. GitHub releases may not exist yet, or the network request failed." + Environment.NewLine + Environment.NewLine + ex.Message, "Version History", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                UseWaitCursor = false;
            }
        }

        private void OpenDonatePage()
        {
            OpenExternalPage("https://onj.me/donate", "Could not open the donation page.");
        }

        private void OpenProjectPage()
        {
            OpenExternalPage(ProjectUrl, "Could not open the project page.");
        }

        private void OpenExternalPage(string url, string errorTitle)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, errorTitle + Environment.NewLine + Environment.NewLine + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private bool TryStartUpdate(GitHubReleaseInfo release, bool showErrors)
        {
            var zipAsset = UpdateService.FindPortableZipAsset(release);
            if (zipAsset == null || string.IsNullOrWhiteSpace(zipAsset.browser_download_url))
            {
                if (showErrors)
                {
                    MessageBox.Show(this, "This GitHub release does not include a downloadable ZIP package. Please open the release page instead.", "Check for Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return false;
            }

            StartUpdate(zipAsset.browser_download_url);
            return true;
        }

        private void StartUpdate(string zipUrl)
        {
            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var exePath = Application.ExecutablePath;
                var updaterTempDir = UpdateService.GetUpdaterTempDirectory(appDir);
                var scriptPath = Path.Combine(updaterTempDir, "MidiCleanerUpdater-" + Guid.NewGuid().ToString("N") + ".ps1");
                File.WriteAllText(scriptPath, UpdateService.BuildUpdaterScript(zipUrl, appDir, exePath, updaterTempDir, Process.GetCurrentProcess().Id, Version));
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not start updater", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplySendToNonFatal()
        {
            try
            {
                SendToInstaller.SetInstalled(settings.SendToEnabled);
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Preferences saved, but the Send To entry could not be updated. " + ex.Message;
            }
        }

        private void ShowTextDialog(string title, string text)
        {
            using (var dialog = new TextReviewForm(title, text))
            {
                dialog.ShowDialog(this);
            }
        }
    }

    internal enum OutputMode
    {
        AlongsideSourceFiles,
        SingleFolder
    }

    internal sealed class CommandLineOptions
    {
        public readonly List<string> Inputs = new List<string>();
        public bool ShowHelp;
        public bool Silent;
        public bool? AddCleanedToFileNames;
        public OutputMode? OutputModeOverride;
        public string OutputFolder;
        public bool? RemoveProgramChanges;
        public bool? RemoveBankSelect;
        public string RemoveCcList;
        public string KeepCcList;
        public bool? KeepPitchBend;
        public bool? KeepChannelAftertouch;
        public bool? KeepPolyAftertouch;
        public bool? RemoveSequencerMetadata;
        public bool? NormalizeChannelsToOne;
        public int? OutputMidiType;
        public bool? LogEnabled;
        public string LogPath;

        public static bool HasCommandLineMode(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return false;
            }

            foreach (var arg in args)
            {
                if (IsHelpArg(arg) ||
                    IsSwitch(arg, "--silent") ||
                    IsSwitch(arg, "/silent") ||
                    IsSwitch(arg, "--convert") ||
                    IsSwitch(arg, "--file") ||
                    IsSwitch(arg, "--folder") ||
                    IsSwitch(arg, "--input"))
                {
                    return true;
                }
            }

            return false;
        }

        public static CommandLineOptions Parse(string[] args)
        {
            var options = new CommandLineOptions();
            if (args == null)
            {
                return options;
            }

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                string value;

                if (IsHelpArg(arg))
                {
                    options.ShowHelp = true;
                }
                else if (IsSwitch(arg, "--silent") || IsSwitch(arg, "/silent") || IsSwitch(arg, "--convert"))
                {
                    options.Silent = true;
                }
                else if (TryReadValue(args, ref i, "--file", out value) ||
                         TryReadValue(args, ref i, "--folder", out value) ||
                         TryReadValue(args, ref i, "--input", out value))
                {
                    AddInputValues(options.Inputs, value);
                }
                else if (TryReadValue(args, ref i, "--output-folder", out value))
                {
                    options.OutputModeOverride = OutputMode.SingleFolder;
                    options.OutputFolder = value;
                }
                else if (IsSwitch(arg, "--alongside-source") || IsSwitch(arg, "--alongside-sources"))
                {
                    options.OutputModeOverride = OutputMode.AlongsideSourceFiles;
                }
                else if (TryReadValue(args, ref i, "--output-mode", out value))
                {
                    if (value.Equals("folder", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("single", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("single-folder", StringComparison.OrdinalIgnoreCase))
                    {
                        options.OutputModeOverride = OutputMode.SingleFolder;
                    }
                    else if (value.Equals("alongside", StringComparison.OrdinalIgnoreCase) ||
                             value.Equals("source", StringComparison.OrdinalIgnoreCase))
                    {
                        options.OutputModeOverride = OutputMode.AlongsideSourceFiles;
                    }
                }
                else if (TryReadValue(args, ref i, "--add-cleaned", out value))
                {
                    options.AddCleanedToFileNames = ParseBool(value, true);
                }
                else if (IsSwitch(arg, "--no-add-cleaned"))
                {
                    options.AddCleanedToFileNames = false;
                }
                else if (TryReadValue(args, ref i, "--remove-program-changes", out value))
                {
                    options.RemoveProgramChanges = ParseBool(value, true);
                }
                else if (TryReadValue(args, ref i, "--remove-bank-select", out value))
                {
                    options.RemoveBankSelect = ParseBool(value, true);
                }
                else if (TryReadValue(args, ref i, "--remove-cc", out value))
                {
                    options.RemoveCcList = value;
                }
                else if (TryReadValue(args, ref i, "--keep-cc", out value))
                {
                    options.KeepCcList = value;
                }
                else if (TryReadValue(args, ref i, "--keep-pitch-bend", out value))
                {
                    options.KeepPitchBend = ParseBool(value, true);
                }
                else if (TryReadValue(args, ref i, "--keep-channel-aftertouch", out value))
                {
                    options.KeepChannelAftertouch = ParseBool(value, true);
                }
                else if (TryReadValue(args, ref i, "--keep-poly-aftertouch", out value))
                {
                    options.KeepPolyAftertouch = ParseBool(value, true);
                }
                else if (TryReadValue(args, ref i, "--remove-sequencer-metadata", out value))
                {
                    options.RemoveSequencerMetadata = ParseBool(value, true);
                }
                else if (IsSwitch(arg, "--keep-sequencer-metadata"))
                {
                    options.RemoveSequencerMetadata = false;
                }
                else if (TryReadValue(args, ref i, "--normalize-channels", out value) ||
                         TryReadValue(args, ref i, "--normalize-channels-to-one", out value))
                {
                    options.NormalizeChannelsToOne = ParseBool(value, true);
                }
                else if (IsSwitch(arg, "--keep-channels"))
                {
                    options.NormalizeChannelsToOne = false;
                }
                else if (TryReadValue(args, ref i, "--midi-type", out value) ||
                         TryReadValue(args, ref i, "--output-midi-type", out value))
                {
                    int type;
                    if (int.TryParse(value, out type) && (type == 0 || type == 1))
                    {
                        options.OutputMidiType = type;
                    }
                }
                else if (arg.StartsWith("--log=", StringComparison.OrdinalIgnoreCase))
                {
                    options.LogEnabled = true;
                    options.LogPath = arg.Substring("--log=".Length);
                }
                else if (IsSwitch(arg, "--log"))
                {
                    options.LogEnabled = true;
                }
                else if (IsSwitch(arg, "--no-log"))
                {
                    options.LogEnabled = false;
                }
                else if (!arg.StartsWith("-", StringComparison.Ordinal) && !arg.StartsWith("/", StringComparison.Ordinal))
                {
                    AddInputValues(options.Inputs, arg);
                }
            }

            return options;
        }

        private static bool IsHelpArg(string arg)
        {
            return IsSwitch(arg, "--help") || IsSwitch(arg, "-?") || IsSwitch(arg, "/?");
        }

        private static bool IsSwitch(string arg, string name)
        {
            return string.Equals(arg, name, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReadValue(string[] args, ref int index, string name, out string value)
        {
            value = null;
            var arg = args[index];
            if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                value = arg.Substring(name.Length + 1);
                return true;
            }

            if (IsSwitch(arg, name) && index + 1 < args.Length)
            {
                value = args[index + 1];
                index++;
                return true;
            }

            return false;
        }

        private static bool ParseBool(string value, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (value.Equals("1") || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value.Equals("0") || value.Equals("no", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : defaultValue;
        }

        private static void AddInputValues(List<string> inputs, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            inputs.Add(value.Trim());
        }
    }

    internal static class CommandLineRunner
    {
        public static int Run(string[] args)
        {
            var commandLine = CommandLineOptions.Parse(args);
            if (commandLine.ShowHelp)
            {
                MessageBox.Show(CommandLineHelpText(), "MidiCleaner", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            var settings = AppSettings.Load();
            ApplyOverrides(settings, commandLine);
            var inputs = ExpandInputs(commandLine.Inputs);
            var logLines = new List<string>();
            var stopwatch = Stopwatch.StartNew();
            var successes = 0;
            var failures = 0;

            logLines.Add("MidiCleaner silent conversion");
            logLines.Add("Started: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            if (inputs.Count == 0)
            {
                logLines.Add("Error: no MIDI files or folders were supplied.");
                WriteLogIfNeeded(settings, commandLine, logLines);
                return 2;
            }

            foreach (var input in inputs)
            {
                try
                {
                    var result = MidiFileCleaner.CleanFile(input, settings.OutputMode, settings.OutputFolder, settings.AddCleanedToFileNames, settings.CreateCleanOptions());
                    successes++;
                    logLines.Add("OK: " + input);
                    logLines.Add("Saved: " + result.OutputPath);
                    logLines.Add(string.Format("Tracks kept: {0}; empty tracks removed: {1}; notes: {2}", result.KeptTracks, result.RemovedTracks, result.NoteCount));
                }
                catch (Exception ex)
                {
                    failures++;
                    logLines.Add("ERROR: " + input);
                    logLines.Add(ex.Message);
                }
            }

            stopwatch.Stop();
            logLines.Insert(2, string.Format("Finished: {0} file(s) cleaned, {1} failed. Time taken: {2}.", successes, failures, FormatElapsed(stopwatch.Elapsed)));
            WriteLogIfNeeded(settings, commandLine, logLines);
            return failures == 0 ? 0 : 1;
        }

        private static void ApplyOverrides(AppSettings settings, CommandLineOptions commandLine)
        {
            if (commandLine.OutputModeOverride.HasValue) settings.OutputMode = commandLine.OutputModeOverride.Value;
            if (!string.IsNullOrWhiteSpace(commandLine.OutputFolder)) settings.OutputFolder = commandLine.OutputFolder;
            if (commandLine.AddCleanedToFileNames.HasValue) settings.AddCleanedToFileNames = commandLine.AddCleanedToFileNames.Value;
            if (commandLine.RemoveProgramChanges.HasValue) settings.RemoveProgramChanges = commandLine.RemoveProgramChanges.Value;
            if (commandLine.RemoveBankSelect.HasValue) settings.RemoveBankSelect = commandLine.RemoveBankSelect.Value;
            if (commandLine.KeepPitchBend.HasValue) settings.KeepPitchBend = commandLine.KeepPitchBend.Value;
            if (commandLine.KeepChannelAftertouch.HasValue) settings.KeepChannelAftertouch = commandLine.KeepChannelAftertouch.Value;
            if (commandLine.KeepPolyAftertouch.HasValue) settings.KeepPolyAftertouch = commandLine.KeepPolyAftertouch.Value;
            if (commandLine.RemoveSequencerMetadata.HasValue) settings.RemoveSequencerMetadata = commandLine.RemoveSequencerMetadata.Value;
            if (commandLine.NormalizeChannelsToOne.HasValue) settings.NormalizeChannelsToOne = commandLine.NormalizeChannelsToOne.Value;
            if (commandLine.OutputMidiType.HasValue) settings.OutputMidiType = commandLine.OutputMidiType.Value;
            if (commandLine.LogEnabled.HasValue) settings.LogSilentConversions = commandLine.LogEnabled.Value;
            if (!string.IsNullOrWhiteSpace(commandLine.LogPath)) settings.LogPath = commandLine.LogPath;

            if (!string.IsNullOrWhiteSpace(commandLine.RemoveCcList))
            {
                settings.SetControllerRemovalDefaults(false);
                foreach (var cc in ParseControllerList(commandLine.RemoveCcList))
                {
                    settings.SetControllerRemoved(cc, true);
                }
            }

            foreach (var cc in ParseControllerList(commandLine.KeepCcList))
            {
                settings.SetControllerRemoved(cc, false);
            }
        }

        private static List<string> ExpandInputs(List<string> inputs)
        {
            var files = new List<string>();
            foreach (var input in inputs)
            {
                if (File.Exists(input))
                {
                    if (IsMidiFile(input))
                    {
                        files.Add(input);
                    }
                }
                else if (Directory.Exists(input))
                {
                    foreach (var file in Directory.GetFiles(input))
                    {
                        if (IsMidiFile(file))
                        {
                            files.Add(file);
                        }
                    }
                }
            }

            files.Sort(StringComparer.CurrentCultureIgnoreCase);
            return files;
        }

        private static bool IsMidiFile(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".mid", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".midi", StringComparison.OrdinalIgnoreCase);
        }

        private static List<int> ParseControllerList(string text)
        {
            var values = new List<int>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return values;
            }

            var parts = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                int value;
                if (int.TryParse(part, out value) && value >= 0 && value <= 127)
                {
                    values.Add(value);
                }
            }

            return values;
        }

        private static void WriteLogIfNeeded(AppSettings settings, CommandLineOptions commandLine, List<string> lines)
        {
            var logEnabled = commandLine.LogEnabled.HasValue ? commandLine.LogEnabled.Value : settings.LogSilentConversions;
            if (!logEnabled)
            {
                return;
            }

            var logPath = !string.IsNullOrWhiteSpace(commandLine.LogPath) ? commandLine.LogPath : settings.LogPath;
            if (string.IsNullOrWhiteSpace(logPath))
            {
                logPath = Path.Combine(Application.StartupPath, "MidiCleaner.log");
            }

            var logDirectory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }
            File.AppendAllText(logPath, string.Join(Environment.NewLine, lines.ToArray()) + Environment.NewLine + Environment.NewLine);
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalSeconds < 1)
            {
                return elapsed.TotalMilliseconds.ToString("0") + " ms";
            }

            if (elapsed.TotalMinutes < 1)
            {
                return elapsed.TotalSeconds.ToString("0.0") + " seconds";
            }

            return string.Format("{0}:{1:00}.{2:0} minutes", (int)elapsed.TotalMinutes, elapsed.Seconds, elapsed.Milliseconds / 100);
        }

        private static string CommandLineHelpText()
        {
            return
                "MidiCleaner command-line options:" + Environment.NewLine + Environment.NewLine +
                "--silent path1 [path2 ...]" + Environment.NewLine +
                "Clean MIDI files or every MIDI file in supplied folders, then exit." + Environment.NewLine + Environment.NewLine +
                "--file path, --folder path, or --input path" + Environment.NewLine +
                "Add an input file or folder. These can be repeated." + Environment.NewLine + Environment.NewLine +
                "--output-mode alongside|folder" + Environment.NewLine +
                "--output-folder path" + Environment.NewLine +
                "--alongside-source" + Environment.NewLine +
                "Override output location settings." + Environment.NewLine + Environment.NewLine +
                "--add-cleaned true|false or --no-add-cleaned" + Environment.NewLine +
                "Choose whether output names include cleaned." + Environment.NewLine + Environment.NewLine +
                "--remove-program-changes true|false" + Environment.NewLine +
                "--remove-bank-select true|false" + Environment.NewLine +
                "--remove-cc 7,10,11,91,92,93" + Environment.NewLine +
                "--keep-cc 10,11" + Environment.NewLine +
                "Override controller cleanup. If --remove-cc is supplied, it replaces the INI controller removal list." + Environment.NewLine + Environment.NewLine +
                "--keep-pitch-bend true|false" + Environment.NewLine +
                "--keep-channel-aftertouch true|false" + Environment.NewLine +
                "--keep-poly-aftertouch true|false" + Environment.NewLine +
                "Choose whether performance messages are kept." + Environment.NewLine + Environment.NewLine +
                "--remove-sequencer-metadata true|false or --keep-sequencer-metadata" + Environment.NewLine +
                "--normalize-channels true|false or --keep-channels" + Environment.NewLine +
                "Choose whether sequencer-specific metadata is removed and whether channel events are changed to channel 1." + Environment.NewLine + Environment.NewLine +
                "--midi-type 0|1" + Environment.NewLine +
                "Choose the output MIDI file type." + Environment.NewLine + Environment.NewLine +
                "--log, --log=path, or --no-log" + Environment.NewLine +
                "Write or suppress a silent conversion log. If no path is supplied, MidiCleaner.log beside the EXE is used." + Environment.NewLine + Environment.NewLine +
                "Unspecified options use MidiCleaner.ini beside the EXE.";
        }
    }

    internal sealed class GitHubReleaseInfo
    {
        public string tag_name { get; set; }
        public string html_url { get; set; }
        public string body { get; set; }
        public List<GitHubReleaseAsset> assets { get; set; }
    }

    internal sealed class GitHubReleaseAsset
    {
        public string name { get; set; }
        public string browser_download_url { get; set; }
    }

    internal static class UpdateService
    {
        public static GitHubReleaseInfo FetchLatestRelease(string projectUrl, string version)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var client = CreateGitHubClient(version))
            {
                var json = client.DownloadString(ApiUrl(projectUrl) + "/releases/latest");
                return new JavaScriptSerializer().Deserialize<GitHubReleaseInfo>(json);
            }
        }

        public static List<GitHubReleaseInfo> FetchReleases(string projectUrl, string version)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var client = CreateGitHubClient(version))
            {
                var json = client.DownloadString(ApiUrl(projectUrl) + "/releases?per_page=100");
                return new JavaScriptSerializer().Deserialize<List<GitHubReleaseInfo>>(json) ?? new List<GitHubReleaseInfo>();
            }
        }

        public static GitHubReleaseInfo LatestVersionedRelease(IEnumerable<GitHubReleaseInfo> releases)
        {
            return (releases ?? new List<GitHubReleaseInfo>())
                .Select(r => new { Release = r, Version = ReleaseVersion(r) })
                .Where(i => i.Version != null)
                .OrderByDescending(i => i.Version)
                .Select(i => i.Release)
                .FirstOrDefault();
        }

        public static GitHubReleaseAsset FindPortableZipAsset(GitHubReleaseInfo release)
        {
            if (release == null)
            {
                return null;
            }

            return (release.assets ?? new List<GitHubReleaseAsset>())
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.browser_download_url) && !string.IsNullOrWhiteSpace(a.name))
                .Where(a => a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.name.IndexOf("portable", StringComparison.OrdinalIgnoreCase) >= 0)
                .ThenByDescending(a => a.name.IndexOf("midi", StringComparison.OrdinalIgnoreCase) >= 0)
                .FirstOrDefault();
        }

        public static string BuildReleaseNotes(IEnumerable<GitHubReleaseInfo> releases, System.Version current, System.Version latest, string currentVersion)
        {
            var newer = (releases ?? new List<GitHubReleaseInfo>())
                .Select(r => new { Release = r, Version = ReleaseVersion(r) })
                .Where(i => i.Version != null && i.Version > current && i.Version <= latest)
                .OrderByDescending(i => i.Version)
                .ToList();

            var builder = new StringBuilder();
            builder.AppendLine("Your version: " + currentVersion);
            builder.AppendLine("New version: " + latest);
            builder.AppendLine();
            builder.AppendLine("Changes between " + currentVersion + " and " + latest);
            builder.AppendLine();
            if (newer.Count == 0)
            {
                builder.AppendLine("No release notes were provided for this update.");
                return builder.ToString();
            }

            foreach (var item in newer)
            {
                builder.AppendLine(item.Release.tag_name);
                builder.AppendLine(FormatReleaseNotesForDialog(item.Release.body, "No release notes were provided for this update."));
                builder.AppendLine();
            }

            return builder.ToString();
        }

        public static string FormatReleaseNotesForDialog(string text, string emptyText)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return emptyText;
            }

            return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine).Trim();
        }

        public static string NormalizeUpdateCheckFrequency(string value)
        {
            if (string.Equals(value, "Hour", StringComparison.OrdinalIgnoreCase)) return "Hourly";
            if (string.Equals(value, "Hourly", StringComparison.OrdinalIgnoreCase)) return "Hourly";
            if (string.Equals(value, "6Hours", StringComparison.OrdinalIgnoreCase)) return "6Hours";
            if (string.Equals(value, "12Hours", StringComparison.OrdinalIgnoreCase)) return "12Hours";
            if (string.Equals(value, "Daily", StringComparison.OrdinalIgnoreCase)) return "Daily";
            if (string.Equals(value, "Weekly", StringComparison.OrdinalIgnoreCase)) return "Weekly";
            if (string.Equals(value, "Never", StringComparison.OrdinalIgnoreCase)) return "Never";
            return "Startup";
        }

        public static TimeSpan? AutomaticUpdateInterval(string frequency)
        {
            switch (NormalizeUpdateCheckFrequency(frequency))
            {
                case "Hourly": return TimeSpan.FromHours(1);
                case "6Hours": return TimeSpan.FromHours(6);
                case "12Hours": return TimeSpan.FromHours(12);
                case "Daily": return TimeSpan.FromDays(1);
                case "Weekly": return TimeSpan.FromDays(7);
                default: return null;
            }
        }

        public static string GetUpdaterTempDirectory(string appDir)
        {
            var candidates = new List<string>();
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                candidates.Add(Path.Combine(localAppData, "Temp"));
            }

            candidates.Add(Path.GetTempPath());
            candidates.Add(Path.Combine(appDir, "Update Temp"));

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                try
                {
                    var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate));
                    Directory.CreateDirectory(fullPath);
                    return fullPath;
                }
                catch
                {
                }
            }

            throw new InvalidOperationException("Could not create a temporary folder for the updater.");
        }

        public static string BuildUpdaterScript(string zipUrl, string targetDir, string exePath, string tempDir, int processId, string version)
        {
            return
                "$ErrorActionPreference = 'Stop'\r\n" +
                "Add-Type -AssemblyName System.Windows.Forms\r\n" +
                "$zipUrl = " + PowerShellQuote(zipUrl) + "\r\n" +
                "$userAgent = " + PowerShellQuote("MidiCleaner " + version) + "\r\n" +
                "$target = " + PowerShellQuote(targetDir) + "\r\n" +
                "$exe = " + PowerShellQuote(exePath) + "\r\n" +
                "$tempBase = " + PowerShellQuote(tempDir) + "\r\n" +
                "$pidToWait = " + processId.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                "try {\r\n" +
                "  [System.IO.Directory]::CreateDirectory($tempBase) | Out-Null\r\n" +
                "  $root = Join-Path $tempBase ('MidiCleanerUpdate_' + [guid]::NewGuid().ToString('N'))\r\n" +
                "  $zip = Join-Path $root 'update.zip'\r\n" +
                "  $stage = Join-Path $root 'stage'\r\n" +
                "  [System.IO.Directory]::CreateDirectory($root) | Out-Null\r\n" +
                "  [System.IO.Directory]::CreateDirectory($stage) | Out-Null\r\n" +
                "  Invoke-WebRequest -Uri $zipUrl -OutFile $zip -UseBasicParsing -UserAgent $userAgent\r\n" +
                "  Expand-Archive -LiteralPath $zip -DestinationPath $stage -Force\r\n" +
                "  $source = $stage\r\n" +
                "  if (-not (Test-Path -LiteralPath (Join-Path $source 'MidiCleaner.exe'))) {\r\n" +
                "    $candidate = Get-ChildItem -LiteralPath $stage -Recurse -Filter 'MidiCleaner.exe' -File | Select-Object -First 1\r\n" +
                "    if ($candidate) { $source = $candidate.DirectoryName }\r\n" +
                "  }\r\n" +
                "  if (-not (Test-Path -LiteralPath (Join-Path $source 'MidiCleaner.exe'))) { throw 'The downloaded ZIP does not contain MidiCleaner.exe.' }\r\n" +
                "  Get-Process -Id $pidToWait -ErrorAction SilentlyContinue | Wait-Process\r\n" +
                "  Get-ChildItem -LiteralPath $source -Force | ForEach-Object {\r\n" +
                "    if ($_.name -ieq 'MidiCleaner.ini' -or $_.name -ieq 'MidiCleaner.log') { return }\r\n" +
                "    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $target $_.name) -Recurse -Force\r\n" +
                "  }\r\n" +
                "  Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue\r\n" +
                "  Start-Process -FilePath $exe\r\n" +
                "} catch {\r\n" +
                "  [System.Windows.Forms.MessageBox]::Show('MidiCleaner update failed:' + [Environment]::NewLine + [Environment]::NewLine + $_.Exception.Message, 'MidiCleaner updater', 'OK', 'Error') | Out-Null\r\n" +
                "}\r\n" +
                "Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue\r\n";
        }

        private static WebClient CreateGitHubClient(string version)
        {
            var client = new WebClient();
            client.Headers.Add("User-Agent", "MidiCleaner " + version);
            return client;
        }

        private static string ApiUrl(string projectUrl)
        {
            return projectUrl.Replace("https://github.com/", "https://api.github.com/repos/");
        }

        private static System.Version ReleaseVersion(GitHubReleaseInfo release)
        {
            if (release == null || string.IsNullOrWhiteSpace(release.tag_name))
            {
                return null;
            }

            System.Version version;
            return System.Version.TryParse(release.tag_name.Trim().TrimStart('v', 'V'), out version) ? version : null;
        }

        private static string PowerShellQuote(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
        }
    }

    internal static class SendToInstaller
    {
        private const string SendToShortcutName = "Midi&Cleaner.lnk";
        private const string LegacySendToFileName = "MidiCleaner.cmd";

        public static void SetInstalled(bool installed)
        {
            var path = GetSendToPath();
            if (installed)
            {
                var exePath = Application.ExecutablePath;
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    throw new InvalidOperationException("WScript.Shell is not available.");
                }

                var shell = Activator.CreateInstance(shellType);
                var shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                var shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exePath });
                shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { "--silent" });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Application.StartupPath });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                DeleteLegacyFile();
            }
            else
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                DeleteLegacyFile();
            }
        }

        private static string GetSendToPath()
        {
            var sendToFolder = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            if (string.IsNullOrWhiteSpace(sendToFolder))
            {
                throw new InvalidOperationException("The Windows Send To folder could not be found.");
            }

            Directory.CreateDirectory(sendToFolder);
            return Path.Combine(sendToFolder, SendToShortcutName);
        }

        private static void DeleteLegacyFile()
        {
            var sendToFolder = Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
            if (!string.IsNullOrWhiteSpace(sendToFolder))
            {
                var legacyPath = Path.Combine(sendToFolder, LegacySendToFileName);
                if (File.Exists(legacyPath))
                {
                    File.Delete(legacyPath);
                }
            }
        }
    }

    internal sealed class AppSettings
    {
        public OutputMode OutputMode = OutputMode.AlongsideSourceFiles;
        public string OutputFolder = string.Empty;
        public string LastInputFolder = string.Empty;
        public bool AddCleanedToFileNames = true;
        public bool RemoveProgramChanges = true;
        public bool RemoveBankSelect = true;
        public bool RemoveVolume = true;
        public bool RemovePan = false;
        public bool RemoveExpression = false;
        public bool RemoveReverb = true;
        public bool RemoveTremolo = false;
        public bool RemoveChorus = true;
        public bool KeepPitchBend = true;
        public bool KeepChannelAftertouch = true;
        public bool KeepPolyAftertouch = true;
        public bool RemoveSequencerMetadata = true;
        public bool NormalizeChannelsToOne = true;
        public int OutputMidiType = 1;
        public bool AskForOutputLocationAfterInput = true;
        public bool LogSilentConversions = false;
        public string LogPath = string.Empty;
        public bool SendToEnabled = false;
        public string UpdateCheckFrequency = "Startup";
        public bool InstallUpdatesQuietly = false;
        public string LastAutomaticUpdateCheckUtc = string.Empty;

        private static string SettingsPath
        {
            get { return Path.Combine(Application.StartupPath, "MidiCleaner.ini"); }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            var path = SettingsPath;
            if (!File.Exists(path))
            {
                return settings;
            }

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("["))
                {
                    continue;
                }

                var split = line.IndexOf('=');
                if (split <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, split).Trim();
                var value = line.Substring(split + 1).Trim();

                if (key.Equals("OutputMode", StringComparison.OrdinalIgnoreCase))
                {
                    OutputMode mode;
                    if (Enum.TryParse(value, true, out mode))
                    {
                        settings.OutputMode = mode;
                    }
                }
                else if (key.Equals("OutputFolder", StringComparison.OrdinalIgnoreCase))
                {
                    settings.OutputFolder = value;
                }
                else if (key.Equals("LastInputFolder", StringComparison.OrdinalIgnoreCase))
                {
                    settings.LastInputFolder = value;
                }
                else if (key.Equals("AddCleanedToFileNames", StringComparison.OrdinalIgnoreCase))
                {
                    bool addCleaned;
                    if (bool.TryParse(value, out addCleaned))
                    {
                        settings.AddCleanedToFileNames = addCleaned;
                    }
                }
                else if (key.Equals("RemoveProgramChanges", StringComparison.OrdinalIgnoreCase))
                {
                    settings.RemoveProgramChanges = ParseBool(value, settings.RemoveProgramChanges);
                }
                else if (key.Equals("RemoveBankSelect", StringComparison.OrdinalIgnoreCase))
                {
                    settings.RemoveBankSelect = ParseBool(value, settings.RemoveBankSelect);
                }
                else if (key.Equals("RemoveVolume", StringComparison.OrdinalIgnoreCase))
                {
                    settings.RemoveVolume = ParseBool(value, settings.RemoveVolume);
                }
                else if (key.Equals("RemovePan", StringComparison.OrdinalIgnoreCase))
                {
                    settings.RemovePan = ParseBool(value, settings.RemovePan);
                }
                else if (key.Equals("RemoveExpression", StringComparison.OrdinalIgnoreCase))
                {
                    settings.RemoveExpression = ParseBool(value, settings.RemoveExpression);
                }
                else if (key.Equals("RemoveReverb", StringComparison.OrdinalIgnoreCase))
                {
                    settings.RemoveReverb = ParseBool(value, settings.RemoveReverb);
                }
                else if (key.Equals("RemoveTremolo", StringComparison.OrdinalIgnoreCase))
                {
                    settings.RemoveTremolo = ParseBool(value, settings.RemoveTremolo);
                }
                else if (key.Equals("RemoveChorus", StringComparison.OrdinalIgnoreCase))
                {
                    settings.RemoveChorus = ParseBool(value, settings.RemoveChorus);
                }
                else if (key.Equals("KeepPitchBend", StringComparison.OrdinalIgnoreCase))
                {
                    settings.KeepPitchBend = ParseBool(value, settings.KeepPitchBend);
                }
                else if (key.Equals("KeepChannelAftertouch", StringComparison.OrdinalIgnoreCase))
                {
                    settings.KeepChannelAftertouch = ParseBool(value, settings.KeepChannelAftertouch);
                }
                else if (key.Equals("KeepPolyAftertouch", StringComparison.OrdinalIgnoreCase))
                {
                    settings.KeepPolyAftertouch = ParseBool(value, settings.KeepPolyAftertouch);
                }
                else if (key.Equals("RemoveSequencerMetadata", StringComparison.OrdinalIgnoreCase))
                {
                    settings.RemoveSequencerMetadata = ParseBool(value, settings.RemoveSequencerMetadata);
                }
                else if (key.Equals("NormalizeChannelsToOne", StringComparison.OrdinalIgnoreCase))
                {
                    settings.NormalizeChannelsToOne = ParseBool(value, settings.NormalizeChannelsToOne);
                }
                else if (key.Equals("OutputMidiType", StringComparison.OrdinalIgnoreCase))
                {
                    int type;
                    if (int.TryParse(value, out type) && (type == 0 || type == 1))
                    {
                        settings.OutputMidiType = type;
                    }
                }
                else if (key.Equals("AskForOutputLocationAfterInput", StringComparison.OrdinalIgnoreCase))
                {
                    settings.AskForOutputLocationAfterInput = ParseBool(value, settings.AskForOutputLocationAfterInput);
                }
                else if (key.Equals("LogSilentConversions", StringComparison.OrdinalIgnoreCase))
                {
                    settings.LogSilentConversions = ParseBool(value, settings.LogSilentConversions);
                }
                else if (key.Equals("LogPath", StringComparison.OrdinalIgnoreCase))
                {
                    settings.LogPath = value;
                }
                else if (key.Equals("SendToEnabled", StringComparison.OrdinalIgnoreCase))
                {
                    settings.SendToEnabled = ParseBool(value, settings.SendToEnabled);
                }
                else if (key.Equals("UpdateCheckFrequency", StringComparison.OrdinalIgnoreCase))
                {
                    settings.UpdateCheckFrequency = UpdateService.NormalizeUpdateCheckFrequency(value);
                }
                else if (key.Equals("InstallUpdatesQuietly", StringComparison.OrdinalIgnoreCase))
                {
                    settings.InstallUpdatesQuietly = ParseBool(value, settings.InstallUpdatesQuietly);
                }
                else if (key.Equals("LastAutomaticUpdateCheckUtc", StringComparison.OrdinalIgnoreCase))
                {
                    settings.LastAutomaticUpdateCheckUtc = value;
                }
            }

            return settings;
        }

        private static bool ParseBool(string value, bool defaultValue)
        {
            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : defaultValue;
        }

        public CleanOptions CreateCleanOptions()
        {
            var controllers = new HashSet<int>();
            if (RemoveBankSelect)
            {
                controllers.Add(0);
                controllers.Add(32);
            }
            if (RemoveVolume) controllers.Add(7);
            if (RemovePan) controllers.Add(10);
            if (RemoveExpression) controllers.Add(11);
            if (RemoveReverb) controllers.Add(91);
            if (RemoveTremolo) controllers.Add(92);
            if (RemoveChorus) controllers.Add(93);

            return new CleanOptions
            {
                RemoveProgramChanges = RemoveProgramChanges,
                RemoveControllers = controllers,
                KeepPitchBend = KeepPitchBend,
                KeepChannelAftertouch = KeepChannelAftertouch,
                KeepPolyAftertouch = KeepPolyAftertouch,
                RemoveSequencerMetadata = RemoveSequencerMetadata,
                NormalizeChannelsToOne = NormalizeChannelsToOne,
                OutputMidiType = OutputMidiType
            };
        }

        public void SetControllerRemovalDefaults(bool removed)
        {
            RemoveVolume = removed;
            RemovePan = removed;
            RemoveExpression = removed;
            RemoveReverb = removed;
            RemoveTremolo = removed;
            RemoveChorus = removed;
            RemoveBankSelect = removed;
        }

        public void SetControllerRemoved(int controller, bool removed)
        {
            if (controller == 0 || controller == 32)
            {
                RemoveBankSelect = removed;
            }
            else if (controller == 7)
            {
                RemoveVolume = removed;
            }
            else if (controller == 10)
            {
                RemovePan = removed;
            }
            else if (controller == 11)
            {
                RemoveExpression = removed;
            }
            else if (controller == 91)
            {
                RemoveReverb = removed;
            }
            else if (controller == 92)
            {
                RemoveTremolo = removed;
            }
            else if (controller == 93)
            {
                RemoveChorus = removed;
            }
        }

        public void Save()
        {
            var lines = new[]
            {
                "[Settings]",
                "OutputMode=" + OutputMode,
                "OutputFolder=" + OutputFolder,
                "LastInputFolder=" + LastInputFolder,
                "AddCleanedToFileNames=" + AddCleanedToFileNames,
                "RemoveProgramChanges=" + RemoveProgramChanges,
                "RemoveBankSelect=" + RemoveBankSelect,
                "RemoveVolume=" + RemoveVolume,
                "RemovePan=" + RemovePan,
                "RemoveExpression=" + RemoveExpression,
                "RemoveReverb=" + RemoveReverb,
                "RemoveTremolo=" + RemoveTremolo,
                "RemoveChorus=" + RemoveChorus,
                "KeepPitchBend=" + KeepPitchBend,
                "KeepChannelAftertouch=" + KeepChannelAftertouch,
                "KeepPolyAftertouch=" + KeepPolyAftertouch,
                "RemoveSequencerMetadata=" + RemoveSequencerMetadata,
                "NormalizeChannelsToOne=" + NormalizeChannelsToOne,
                "OutputMidiType=" + OutputMidiType,
                "AskForOutputLocationAfterInput=" + AskForOutputLocationAfterInput,
                "LogSilentConversions=" + LogSilentConversions,
                "LogPath=" + LogPath,
                "SendToEnabled=" + SendToEnabled,
                "UpdateCheckFrequency=" + UpdateService.NormalizeUpdateCheckFrequency(UpdateCheckFrequency),
                "InstallUpdatesQuietly=" + InstallUpdatesQuietly,
                "LastAutomaticUpdateCheckUtc=" + LastAutomaticUpdateCheckUtc
            };
            File.WriteAllLines(SettingsPath, lines);
        }
    }

    internal sealed class OutputLocationForm : Form
    {
        private readonly RadioButton alongsideRadio;
        private readonly RadioButton singleFolderRadio;
        private readonly TextBox folderTextBox;
        private readonly Button browseButton;
        private readonly CheckBox addCleanedCheckBox;

        public OutputMode Mode
        {
            get { return singleFolderRadio.Checked ? OutputMode.SingleFolder : OutputMode.AlongsideSourceFiles; }
        }

        public string SelectedOutputFolder
        {
            get { return folderTextBox.Text.Trim(); }
        }

        public bool AddCleanedToFileNames
        {
            get { return addCleanedCheckBox.Checked; }
        }

        public OutputLocationForm(int fileCount, AppSettings settings)
        {
            Text = "Output Location";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(570, 260);
            AccessibleName = "Output location";
            AccessibleDescription = "Choose where cleaned MIDI files should be saved.";

            var intro = new Label
            {
                Text = fileCount == 1
                    ? "Where should MidiCleaner save the cleaned file?"
                    : "Where should MidiCleaner save the cleaned files?",
                AutoSize = true,
                Location = new Point(12, 14),
                AccessibleRole = AccessibleRole.StaticText,
                AccessibleName = "Output location question"
            };
            Controls.Add(intro);

            alongsideRadio = new RadioButton
            {
                Text = "Create &Output folders alongside the source files",
                Location = new Point(15, 46),
                AutoSize = true,
                Checked = true,
                AccessibleRole = AccessibleRole.RadioButton,
                AccessibleName = "Create Output folders alongside the source files",
                AccessibleDescription = "Each cleaned file is saved in an Output folder next to its source file."
            };
            Controls.Add(alongsideRadio);

            singleFolderRadio = new RadioButton
            {
                Text = "Put all cleaned files in &one folder",
                Location = new Point(15, 78),
                AutoSize = true,
                AccessibleRole = AccessibleRole.RadioButton,
                AccessibleName = "Put all cleaned files in one folder",
                AccessibleDescription = "Choose one folder where every cleaned file will be saved."
            };
            singleFolderRadio.CheckedChanged += delegate { UpdateFolderControls(); };
            Controls.Add(singleFolderRadio);

            var folderLabel = new Label
            {
                Text = "Output folder:",
                AutoSize = true,
                Location = new Point(36, 114),
                AccessibleRole = AccessibleRole.StaticText,
                AccessibleName = "Output folder label"
            };
            Controls.Add(folderLabel);

            folderTextBox = new TextBox
            {
                Location = new Point(125, 110),
                Size = new Size(320, 23),
                AccessibleRole = AccessibleRole.Text,
                AccessibleName = "Output folder path",
                AccessibleDescription = "The folder where all cleaned MIDI files will be saved."
            };
            Controls.Add(folderTextBox);

            browseButton = new Button
            {
                Text = "&Browse...",
                Location = new Point(455, 108),
                AutoSize = true,
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "Browse for output folder",
                AccessibleDescription = "Choose the output folder."
            };
            browseButton.Click += delegate { BrowseForFolder(); };
            Controls.Add(browseButton);

            var note = new Label
            {
                Text = "Source files and existing output files are never overwritten.",
                AutoSize = true,
                Location = new Point(12, 176),
                AccessibleRole = AccessibleRole.StaticText,
                AccessibleName = "Overwrite safety note"
            };
            Controls.Add(note);

            addCleanedCheckBox = new CheckBox
            {
                Text = "Add \"cleaned\" to output file &names",
                Location = new Point(15, 146),
                AutoSize = true,
                Checked = settings.AddCleanedToFileNames,
                AccessibleRole = AccessibleRole.CheckButton,
                AccessibleName = "Add cleaned to output file names",
                AccessibleDescription = "When checked, output files include the word cleaned before the file extension. When unchecked, output files keep the original file name unless a number is needed to avoid overwriting."
            };
            Controls.Add(addCleanedCheckBox);

            var okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(374, 217),
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "OK"
            };
            okButton.Click += OkButton_Click;
            Controls.Add(okButton);
            AcceptButton = okButton;

            var cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(463, 217),
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "Cancel"
            };
            Controls.Add(cancelButton);
            CancelButton = cancelButton;

            if (settings.OutputMode == OutputMode.SingleFolder)
            {
                singleFolderRadio.Checked = true;
            }
            if (!string.IsNullOrWhiteSpace(settings.OutputFolder))
            {
                folderTextBox.Text = settings.OutputFolder;
            }
            UpdateFolderControls();
        }

        private void BrowseForFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose where cleaned MIDI files should be saved.";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(folderTextBox.Text))
                {
                    dialog.SelectedPath = folderTextBox.Text;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    folderTextBox.Text = dialog.SelectedPath;
                    singleFolderRadio.Checked = true;
                }
            }
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            if (singleFolderRadio.Checked)
            {
                if (string.IsNullOrWhiteSpace(folderTextBox.Text))
                {
                    MessageBox.Show(this, "Choose an output folder, or select the alongside-source option.", "Output Location", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    folderTextBox.Focus();
                    return;
                }
                Directory.CreateDirectory(folderTextBox.Text);
            }
        }

        private void UpdateFolderControls()
        {
            var enabled = singleFolderRadio.Checked;
            folderTextBox.Enabled = enabled;
            browseButton.Enabled = enabled;
        }
    }

    internal sealed class PreferencesForm : Form
    {
        private readonly CheckedListBox removeMessagesList;
        private readonly RadioButton outputAlongsideRadio;
        private readonly RadioButton outputSingleFolderRadio;
        private readonly TextBox outputFolderTextBox;
        private readonly Button outputBrowseButton;
        private readonly CheckBox addCleanedCheckBox;
        private readonly CheckBox askForOutputLocationCheckBox;
        private readonly ComboBox outputMidiTypeBox;
        private readonly CheckBox logSilentConversionsCheckBox;
        private readonly TextBox logPathTextBox;
        private readonly Button logBrowseButton;
        private readonly CheckBox sendToCheckBox;
        private readonly ComboBox updateCheckFrequencyBox;
        private readonly CheckBox installUpdatesQuietlyCheckBox;

        public PreferencesForm(AppSettings settings)
        {
            Text = "Preferences";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(690, 560);
            MinimumSize = new Size(620, 480);
            AccessibleName = "Preferences";
            AccessibleDescription = "MidiCleaner preferences.";

            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                AccessibleName = "Preference tabs"
            };
            Controls.Add(tabs);

            var cleanupTab = new TabPage("MIDI Cleanup");
            cleanupTab.AccessibleName = "MIDI Cleanup";
            tabs.TabPages.Add(cleanupTab);

            var cleanupPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10)
            };
            cleanupPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cleanupPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            cleanupPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            cleanupTab.Controls.Add(cleanupPanel);

            var cleanupIntro = new Label
            {
                Text = "Checked items are removed from cleaned MIDI files.",
                AutoSize = true,
                AccessibleRole = AccessibleRole.StaticText,
                AccessibleName = "Checked items are removed from cleaned MIDI files."
            };
            cleanupPanel.Controls.Add(cleanupIntro);

            removeMessagesList = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
                AccessibleRole = AccessibleRole.CheckButton,
                AccessibleName = "MIDI messages to remove",
                AccessibleDescription = "Checked items are removed from cleaned MIDI files. Unchecked items are kept."
            };
            AddRemoveMessageItems(removeMessagesList, settings);
            cleanupPanel.Controls.Add(removeMessagesList);

            var cleanupButtons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 8, 0, 0)
            };
            var selectAllButton = new Button
            {
                Text = "Select &All",
                AutoSize = true,
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "Select all cleanup items"
            };
            selectAllButton.Click += delegate { SetAllRemoveItems(true); };
            cleanupButtons.Controls.Add(selectAllButton);

            var selectNoneButton = new Button
            {
                Text = "Select &None",
                AutoSize = true,
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "Select no cleanup items"
            };
            selectNoneButton.Click += delegate { SetAllRemoveItems(false); };
            cleanupButtons.Controls.Add(selectNoneButton);
            cleanupPanel.Controls.Add(cleanupButtons);

            var outputTab = new TabPage("Output Defaults");
            outputTab.AccessibleName = "Output Defaults";
            tabs.TabPages.Add(outputTab);

            var outputPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                ColumnCount = 1,
                Padding = new Padding(12)
            };
            outputTab.Controls.Add(outputPanel);

            outputAlongsideRadio = new RadioButton
            {
                Text = "Create &Output folders alongside the source files",
                AutoSize = true,
                Checked = settings.OutputMode != OutputMode.SingleFolder,
                AccessibleRole = AccessibleRole.RadioButton,
                AccessibleName = "Create Output folders alongside the source files"
            };
            outputPanel.Controls.Add(outputAlongsideRadio);

            outputSingleFolderRadio = new RadioButton
            {
                Text = "Put all cleaned files in &one folder",
                AutoSize = true,
                Checked = settings.OutputMode == OutputMode.SingleFolder,
                AccessibleRole = AccessibleRole.RadioButton,
                AccessibleName = "Put all cleaned files in one folder"
            };
            outputSingleFolderRadio.CheckedChanged += delegate { UpdateOutputFolderControls(); };
            outputPanel.Controls.Add(outputSingleFolderRadio);

            var folderRow = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(22, 8, 0, 8)
            };
            folderRow.Controls.Add(new Label { Text = "Output folder:", AutoSize = true, Padding = new Padding(0, 5, 4, 0), AccessibleRole = AccessibleRole.StaticText });
            outputFolderTextBox = new TextBox
            {
                Text = settings.OutputFolder,
                Width = 390,
                AccessibleRole = AccessibleRole.Text,
                AccessibleName = "Output folder path"
            };
            folderRow.Controls.Add(outputFolderTextBox);
            outputBrowseButton = new Button
            {
                Text = "Browse...",
                AutoSize = true,
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "Browse for output folder"
            };
            outputBrowseButton.Click += delegate { BrowseForOutputFolder(); };
            folderRow.Controls.Add(outputBrowseButton);
            outputPanel.Controls.Add(folderRow);

            addCleanedCheckBox = CreateCheckBox("Add \"cleaned\" to output file &names", settings.AddCleanedToFileNames, "When checked, output files include the word cleaned before the file extension.");
            outputPanel.Controls.Add(addCleanedCheckBox);

            askForOutputLocationCheckBox = CreateCheckBox("&Ask where to save after choosing input", settings.AskForOutputLocationAfterInput, "When checked, MidiCleaner asks for output choices every time files or folders are opened. When unchecked, saved output defaults are used immediately.");
            outputPanel.Controls.Add(askForOutputLocationCheckBox);

            outputPanel.Controls.Add(new Label
            {
                Text = "MIDI output type:",
                AutoSize = true,
                AccessibleRole = AccessibleRole.StaticText
            });
            outputMidiTypeBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 180,
                AccessibleRole = AccessibleRole.ComboBox,
                AccessibleName = "MIDI output type"
            };
            outputMidiTypeBox.Items.AddRange(new object[] { "Type 1", "Type 0" });
            outputMidiTypeBox.SelectedIndex = settings.OutputMidiType == 0 ? 1 : 0;
            outputPanel.Controls.Add(outputMidiTypeBox);

            var automationTab = new TabPage("Automation");
            automationTab.AccessibleName = "Automation";
            tabs.TabPages.Add(automationTab);

            var automationPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                ColumnCount = 1,
                Padding = new Padding(12)
            };
            automationTab.Controls.Add(automationPanel);

            logSilentConversionsCheckBox = CreateCheckBox("&Log silent conversions", settings.LogSilentConversions, "When checked, silent command-line conversions append results to a log file.");
            logSilentConversionsCheckBox.CheckedChanged += delegate { UpdateLogControls(); };
            automationPanel.Controls.Add(logSilentConversionsCheckBox);

            var logRow = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(22, 8, 0, 8)
            };
            logRow.Controls.Add(new Label { Text = "Log file:", AutoSize = true, Padding = new Padding(0, 5, 4, 0), AccessibleRole = AccessibleRole.StaticText });
            logPathTextBox = new TextBox
            {
                Text = settings.LogPath,
                Width = 390,
                AccessibleRole = AccessibleRole.Text,
                AccessibleName = "Silent conversion log path"
            };
            logRow.Controls.Add(logPathTextBox);
            logBrowseButton = new Button
            {
                Text = "Browse...",
                AutoSize = true,
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "Browse for log file"
            };
            logBrowseButton.Click += delegate { BrowseForLogFile(); };
            logRow.Controls.Add(logBrowseButton);
            automationPanel.Controls.Add(logRow);

            sendToCheckBox = CreateCheckBox("Add MidiCleaner to Windows &Send To menu", settings.SendToEnabled, "Adds a Send To entry that runs MidiCleaner silently using current INI preferences.");
            automationPanel.Controls.Add(sendToCheckBox);

            var updatesTab = new TabPage("Updates");
            updatesTab.AccessibleName = "Updates";
            tabs.TabPages.Add(updatesTab);

            var updatesPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                ColumnCount = 1,
                Padding = new Padding(12)
            };
            updatesTab.Controls.Add(updatesPanel);

            updatesPanel.Controls.Add(new Label
            {
                Text = "Check GitHub Releases for updates:",
                AutoSize = true,
                AccessibleRole = AccessibleRole.StaticText
            });
            updateCheckFrequencyBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 260,
                AccessibleRole = AccessibleRole.ComboBox,
                AccessibleName = "Check GitHub Releases for updates"
            };
            updateCheckFrequencyBox.Items.AddRange(UpdateFrequencyLabels());
            updateCheckFrequencyBox.SelectedIndex = UpdateFrequencyIndex(settings.UpdateCheckFrequency);
            updatesPanel.Controls.Add(updateCheckFrequencyBox);

            installUpdatesQuietlyCheckBox = CreateCheckBox("Download and install updates &quietly when available", settings.InstallUpdatesQuietly, "When checked, MidiCleaner downloads and installs a release ZIP without first showing release notes.");
            updatesPanel.Controls.Add(installUpdatesQuietlyCheckBox);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(10)
            };
            Controls.Add(buttons);

            var okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                AutoSize = true,
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "OK"
            };
            buttons.Controls.Add(okButton);
            AcceptButton = okButton;

            var cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "Cancel"
            };
            buttons.Controls.Add(cancelButton);
            CancelButton = cancelButton;

            UpdateOutputFolderControls();
            UpdateLogControls();
        }

        public void ApplyTo(AppSettings settings)
        {
            ApplyRemoveMessageItems(settings);
            settings.OutputMode = outputSingleFolderRadio.Checked ? OutputMode.SingleFolder : OutputMode.AlongsideSourceFiles;
            settings.OutputFolder = outputFolderTextBox.Text.Trim();
            settings.AddCleanedToFileNames = addCleanedCheckBox.Checked;
            settings.AskForOutputLocationAfterInput = askForOutputLocationCheckBox.Checked;
            settings.OutputMidiType = outputMidiTypeBox.SelectedIndex == 1 ? 0 : 1;
            settings.LogSilentConversions = logSilentConversionsCheckBox.Checked;
            settings.LogPath = logPathTextBox.Text.Trim();
            settings.SendToEnabled = sendToCheckBox.Checked;
            settings.UpdateCheckFrequency = UpdateFrequencyFromIndex(updateCheckFrequencyBox.SelectedIndex);
            settings.InstallUpdatesQuietly = installUpdatesQuietlyCheckBox.Checked;
        }

        private void AddRemoveMessageItems(CheckedListBox list, AppSettings settings)
        {
            list.Items.Add(new RemoveMessageItem("Program changes", "ProgramChanges"), settings.RemoveProgramChanges);
            list.Items.Add(new RemoveMessageItem("Bank select (CC 0 and 32)", "BankSelect"), settings.RemoveBankSelect);
            list.Items.Add(new RemoveMessageItem("Volume (CC 7)", "Volume"), settings.RemoveVolume);
            list.Items.Add(new RemoveMessageItem("Pan (CC 10)", "Pan"), settings.RemovePan);
            list.Items.Add(new RemoveMessageItem("Expression (CC 11)", "Expression"), settings.RemoveExpression);
            list.Items.Add(new RemoveMessageItem("Reverb send (CC 91)", "Reverb"), settings.RemoveReverb);
            list.Items.Add(new RemoveMessageItem("Tremolo depth (CC 92)", "Tremolo"), settings.RemoveTremolo);
            list.Items.Add(new RemoveMessageItem("Chorus send (CC 93)", "Chorus"), settings.RemoveChorus);
            list.Items.Add(new RemoveMessageItem("Pitch bend", "PitchBend"), !settings.KeepPitchBend);
            list.Items.Add(new RemoveMessageItem("Channel aftertouch", "ChannelAftertouch"), !settings.KeepChannelAftertouch);
            list.Items.Add(new RemoveMessageItem("Polyphonic aftertouch", "PolyAftertouch"), !settings.KeepPolyAftertouch);
            list.Items.Add(new RemoveMessageItem("Sequencer-specific metadata", "SequencerMetadata"), settings.RemoveSequencerMetadata);
            list.Items.Add(new RemoveMessageItem("Original channel assignments (normalize all channels to 1)", "OriginalChannels"), settings.NormalizeChannelsToOne);
        }

        private void ApplyRemoveMessageItems(AppSettings settings)
        {
            settings.RemoveProgramChanges = IsRemoveItemChecked("ProgramChanges");
            settings.RemoveBankSelect = IsRemoveItemChecked("BankSelect");
            settings.RemoveVolume = IsRemoveItemChecked("Volume");
            settings.RemovePan = IsRemoveItemChecked("Pan");
            settings.RemoveExpression = IsRemoveItemChecked("Expression");
            settings.RemoveReverb = IsRemoveItemChecked("Reverb");
            settings.RemoveTremolo = IsRemoveItemChecked("Tremolo");
            settings.RemoveChorus = IsRemoveItemChecked("Chorus");
            settings.KeepPitchBend = !IsRemoveItemChecked("PitchBend");
            settings.KeepChannelAftertouch = !IsRemoveItemChecked("ChannelAftertouch");
            settings.KeepPolyAftertouch = !IsRemoveItemChecked("PolyAftertouch");
            settings.RemoveSequencerMetadata = IsRemoveItemChecked("SequencerMetadata");
            settings.NormalizeChannelsToOne = IsRemoveItemChecked("OriginalChannels");
        }

        private bool IsRemoveItemChecked(string key)
        {
            for (var i = 0; i < removeMessagesList.Items.Count; i++)
            {
                var item = removeMessagesList.Items[i] as RemoveMessageItem;
                if (item != null && item.Key.Equals(key, StringComparison.Ordinal))
                {
                    return removeMessagesList.GetItemChecked(i);
                }
            }

            return false;
        }

        private void SetAllRemoveItems(bool isChecked)
        {
            for (var i = 0; i < removeMessagesList.Items.Count; i++)
            {
                removeMessagesList.SetItemChecked(i, isChecked);
            }
        }

        private sealed class RemoveMessageItem
        {
            public readonly string Text;
            public readonly string Key;

            public RemoveMessageItem(string text, string key)
            {
                Text = text;
                Key = key;
            }

            public override string ToString()
            {
                return Text;
            }
        }

        private static CheckBox CreateCheckBox(string text, bool isChecked, string description)
        {
            return new CheckBox
            {
                Text = text,
                Checked = isChecked,
                AutoSize = true,
                AccessibleRole = AccessibleRole.CheckButton,
                AccessibleName = text.Replace("&", string.Empty),
                AccessibleDescription = description,
                Margin = new Padding(3, 3, 3, 6)
            };
        }

        private void BrowseForOutputFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose where cleaned MIDI files should be saved by default.";
                dialog.ShowNewFolderButton = true;
                if (Directory.Exists(outputFolderTextBox.Text))
                {
                    dialog.SelectedPath = outputFolderTextBox.Text;
                }
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    outputFolderTextBox.Text = dialog.SelectedPath;
                    outputSingleFolderRadio.Checked = true;
                }
            }
        }

        private void UpdateOutputFolderControls()
        {
            var enabled = outputSingleFolderRadio.Checked;
            outputFolderTextBox.Enabled = enabled;
            outputBrowseButton.Enabled = enabled;
        }

        private void BrowseForLogFile()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Title = "Choose silent conversion log file";
                dialog.Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.FileName = string.IsNullOrWhiteSpace(logPathTextBox.Text) ? "MidiCleaner.log" : Path.GetFileName(logPathTextBox.Text);
                var directory = Path.GetDirectoryName(logPathTextBox.Text);
                if (Directory.Exists(directory))
                {
                    dialog.InitialDirectory = directory;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    logPathTextBox.Text = dialog.FileName;
                    logSilentConversionsCheckBox.Checked = true;
                }
            }
        }

        private void UpdateLogControls()
        {
            var enabled = logSilentConversionsCheckBox.Checked;
            logPathTextBox.Enabled = enabled;
            logBrowseButton.Enabled = enabled;
        }

        private static object[] UpdateFrequencyLabels()
        {
            return new object[]
            {
                "At startup",
                "Every hour",
                "Every 6 hours",
                "Every 12 hours",
                "Daily",
                "Weekly",
                "Never"
            };
        }

        private static int UpdateFrequencyIndex(string value)
        {
            switch (UpdateService.NormalizeUpdateCheckFrequency(value))
            {
                case "Hourly": return 1;
                case "6Hours": return 2;
                case "12Hours": return 3;
                case "Daily": return 4;
                case "Weekly": return 5;
                case "Never": return 6;
                default: return 0;
            }
        }

        private static string UpdateFrequencyFromIndex(int index)
        {
            switch (index)
            {
                case 1: return "Hourly";
                case 2: return "6Hours";
                case 3: return "12Hours";
                case 4: return "Daily";
                case 5: return "Weekly";
                case 6: return "Never";
                default: return "Startup";
            }
        }
    }

    internal sealed class TextReviewForm : Form
    {
        public TextReviewForm(string title, string text)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(720, 520);
            MinimizeBox = false;
            AccessibleName = title;

            var textBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Dock = DockStyle.Fill,
                Text = NormalizeLineEndings(text),
                AccessibleRole = AccessibleRole.Text,
                AccessibleName = title + " text",
                AccessibleDescription = "Read-only text. Press Escape or Alt F4 to close."
            };
            Controls.Add(textBox);

            var closeButton = new Button
            {
                Text = "Close",
                Dock = DockStyle.Bottom,
                Height = 32,
                DialogResult = DialogResult.OK,
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = "Close"
            };
            Controls.Add(closeButton);
            AcceptButton = closeButton;
            CancelButton = closeButton;

            Shown += delegate { textBox.Select(0, 0); textBox.Focus(); };
        }

        private static string NormalizeLineEndings(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
        }
    }

    internal sealed class CleanOptions
    {
        public bool RemoveProgramChanges;
        public HashSet<int> RemoveControllers = new HashSet<int>();
        public bool KeepPitchBend = true;
        public bool KeepChannelAftertouch = true;
        public bool KeepPolyAftertouch = true;
        public bool RemoveSequencerMetadata = true;
        public bool NormalizeChannelsToOne = true;
        public int OutputMidiType = 1;
    }

    internal sealed class CleanResult
    {
        public string OutputPath;
        public int KeptTracks;
        public int RemovedTracks;
        public int NoteCount;
    }

    internal static class MidiFileCleaner
    {
        public static CleanResult CleanFile(string inputPath, OutputMode mode, string selectedOutputFolder, bool addCleanedToFileName, CleanOptions options)
        {
            var data = File.ReadAllBytes(inputPath);
            int division;
            var tracks = ParseMidi(data, out division);
            var cleanedTracks = new List<byte[]>();
            var removedTracks = 0;
            var totalNotes = 0;

            foreach (var track in tracks)
            {
                int notes;
                var cleaned = CleanTrack(track, options, out notes);
                if (cleaned == null)
                {
                    removedTracks++;
                }
                else
                {
                    cleanedTracks.Add(cleaned);
                    totalNotes += notes;
                }
            }

            if (cleanedTracks.Count == 0)
            {
                throw new InvalidDataException("No tracks with MIDI note data were found.");
            }

            var outputPath = GetOutputPath(inputPath, mode, selectedOutputFolder, addCleanedToFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllBytes(outputPath, BuildMidi(division, cleanedTracks, options.OutputMidiType));

            return new CleanResult
            {
                OutputPath = outputPath,
                KeptTracks = cleanedTracks.Count,
                RemovedTracks = removedTracks,
                NoteCount = totalNotes
            };
        }

        private static List<byte[]> ParseMidi(byte[] data, out int division)
        {
            if (data.Length < 14 || Encoding.ASCII.GetString(data, 0, 4) != "MThd")
            {
                throw new InvalidDataException("This is not a standard MIDI file.");
            }

            var headerLength = (int)ReadUInt32(data, 4);
            if (headerLength < 6 || 8 + headerLength > data.Length)
            {
                throw new InvalidDataException("Invalid MIDI header.");
            }

            var trackCount = ReadUInt16(data, 10);
            division = ReadUInt16(data, 12);
            var pos = 8 + headerLength;
            var tracks = new List<byte[]>();

            while (pos < data.Length)
            {
                if (pos + 8 > data.Length)
                {
                    throw new InvalidDataException("A MIDI chunk header is truncated.");
                }

                var id = Encoding.ASCII.GetString(data, pos, 4);
                var length = (int)ReadUInt32(data, pos + 4);
                pos += 8;
                if (pos + length > data.Length)
                {
                    throw new InvalidDataException("A MIDI chunk is truncated.");
                }

                if (id == "MTrk")
                {
                    var track = new byte[length];
                    Buffer.BlockCopy(data, pos, track, 0, length);
                    tracks.Add(track);
                }
                pos += length;
            }

            if (tracks.Count == 0 || (trackCount != 0 && tracks.Count == 0))
            {
                throw new InvalidDataException("The MIDI file has no tracks.");
            }
            return tracks;
        }

        private static byte[] CleanTrack(byte[] track, CleanOptions options, out int noteCount)
        {
            var pos = 0;
            int runningStatus = -1;
            var output = new List<byte>();
            var pendingDelta = 0;
            noteCount = 0;

            while (pos < track.Length)
            {
                int delta;
                pos = ReadVariableLength(track, pos, out delta);
                pendingDelta += delta;
                if (pos >= track.Length)
                {
                    throw new InvalidDataException("A MIDI event is missing its status byte.");
                }

                var first = track[pos];
                int status;
                if (first < 0x80)
                {
                    if (runningStatus < 0)
                    {
                        throw new InvalidDataException("Running status was used before any status byte.");
                    }
                    status = runningStatus;
                }
                else
                {
                    pos++;
                    status = first;
                    if (status < 0xF0)
                    {
                        runningStatus = status;
                    }
                    else if (status == 0xF0 || status == 0xF7 || status == 0xFF)
                    {
                        runningStatus = -1;
                    }
                }

                if (status == 0xFF)
                {
                    if (pos >= track.Length)
                    {
                        throw new InvalidDataException("A meta event is truncated.");
                    }
                    var metaType = track[pos++];
                    int length;
                    pos = ReadVariableLength(track, pos, out length);
                    if (pos + length > track.Length)
                    {
                        throw new InvalidDataException("A meta event value is truncated.");
                    }

                    var value = new byte[length];
                    Buffer.BlockCopy(track, pos, value, 0, length);
                    pos += length;

                    if (metaType != 0x7F || !options.RemoveSequencerMetadata)
                    {
                        WriteVariableLength(output, pendingDelta);
                        output.Add(0xFF);
                        output.Add(metaType);
                        WriteVariableLength(output, length);
                        output.AddRange(value);
                        pendingDelta = 0;
                    }
                    continue;
                }

                if (status == 0xF0 || status == 0xF7)
                {
                    int length;
                    pos = ReadVariableLength(track, pos, out length);
                    if (pos + length > track.Length)
                    {
                        throw new InvalidDataException("A SysEx event is truncated.");
                    }
                    var value = new byte[length];
                    Buffer.BlockCopy(track, pos, value, 0, length);
                    pos += length;

                    WriteVariableLength(output, pendingDelta);
                    output.Add((byte)status);
                    WriteVariableLength(output, length);
                    output.AddRange(value);
                    pendingDelta = 0;
                    continue;
                }

                if (status < 0x80 || status > 0xEF)
                {
                    throw new InvalidDataException("Unsupported MIDI event status 0x" + status.ToString("X2") + ".");
                }

                var eventType = status & 0xF0;
                var dataLength = (eventType == 0xC0 || eventType == 0xD0) ? 1 : 2;
                if (pos + dataLength > track.Length)
                {
                    throw new InvalidDataException("A channel event is truncated.");
                }

                var eventData = new byte[dataLength];
                Buffer.BlockCopy(track, pos, eventData, 0, dataLength);
                pos += dataLength;

                var remove = eventType == 0xC0 && options.RemoveProgramChanges;
                if (eventType == 0xB0 && options.RemoveControllers.Contains(eventData[0]))
                {
                    remove = true;
                }
                if (eventType == 0xE0 && !options.KeepPitchBend)
                {
                    remove = true;
                }
                if (eventType == 0xD0 && !options.KeepChannelAftertouch)
                {
                    remove = true;
                }
                if (eventType == 0xA0 && !options.KeepPolyAftertouch)
                {
                    remove = true;
                }

                if (eventType == 0x90 && eventData.Length >= 2 && eventData[1] > 0)
                {
                    noteCount++;
                }

                if (remove)
                {
                    continue;
                }

                WriteVariableLength(output, pendingDelta);
                output.Add((byte)(options.NormalizeChannelsToOne ? eventType : status));
                output.AddRange(eventData);
                pendingDelta = 0;
            }

            if (noteCount == 0)
            {
                return null;
            }

            if (!EndsWithEndOfTrack(output.ToArray()))
            {
                WriteVariableLength(output, pendingDelta);
                output.Add(0xFF);
                output.Add(0x2F);
                output.Add(0x00);
            }

            return output.ToArray();
        }

        private static bool EndsWithEndOfTrack(byte[] track)
        {
            var pos = 0;
            int runningStatus = -1;
            int lastMeta = -1;

            try
            {
                while (pos < track.Length)
                {
                    int delta;
                    pos = ReadVariableLength(track, pos, out delta);
                    var first = track[pos];
                    int status;
                    if (first < 0x80)
                    {
                        status = runningStatus;
                    }
                    else
                    {
                        pos++;
                        status = first;
                        runningStatus = status < 0xF0 ? status : -1;
                    }

                    if (status == 0xFF)
                    {
                        lastMeta = track[pos++];
                        int length;
                        pos = ReadVariableLength(track, pos, out length);
                        pos += length;
                    }
                    else if (status == 0xF0 || status == 0xF7)
                    {
                        int length;
                        pos = ReadVariableLength(track, pos, out length);
                        pos += length;
                        lastMeta = -1;
                    }
                    else
                    {
                        var eventType = status & 0xF0;
                        pos += (eventType == 0xC0 || eventType == 0xD0) ? 1 : 2;
                        lastMeta = -1;
                    }
                }
            }
            catch
            {
                return false;
            }

            return lastMeta == 0x2F;
        }

        private static byte[] BuildMidi(int division, List<byte[]> tracks, int outputMidiType)
        {
            if (outputMidiType == 0)
            {
                tracks = new List<byte[]> { MergeTracksForType0(tracks) };
            }

            using (var stream = new MemoryStream())
            {
                WriteAscii(stream, "MThd");
                WriteUInt32(stream, 6);
                WriteUInt16(stream, outputMidiType == 0 ? 0 : 1);
                WriteUInt16(stream, tracks.Count);
                WriteUInt16(stream, division);

                foreach (var track in tracks)
                {
                    WriteAscii(stream, "MTrk");
                    WriteUInt32(stream, track.Length);
                    stream.Write(track, 0, track.Length);
                }

                return stream.ToArray();
            }
        }

        private static byte[] MergeTracksForType0(List<byte[]> tracks)
        {
            var events = new List<TimedMidiEvent>();
            var order = 0;
            foreach (var track in tracks)
            {
                foreach (var midiEvent in ReadTimedEvents(track))
                {
                    if (!midiEvent.IsEndOfTrack)
                    {
                        midiEvent.Order = order++;
                        events.Add(midiEvent);
                    }
                }
            }

            events.Sort(delegate(TimedMidiEvent left, TimedMidiEvent right)
            {
                var tickCompare = left.Tick.CompareTo(right.Tick);
                return tickCompare != 0 ? tickCompare : left.Order.CompareTo(right.Order);
            });

            var output = new List<byte>();
            var lastTick = 0;
            foreach (var midiEvent in events)
            {
                WriteVariableLength(output, midiEvent.Tick - lastTick);
                output.AddRange(midiEvent.Data);
                lastTick = midiEvent.Tick;
            }

            WriteVariableLength(output, 0);
            output.Add(0xFF);
            output.Add(0x2F);
            output.Add(0x00);
            return output.ToArray();
        }

        private static List<TimedMidiEvent> ReadTimedEvents(byte[] track)
        {
            var events = new List<TimedMidiEvent>();
            var pos = 0;
            var tick = 0;
            int runningStatus = -1;
            while (pos < track.Length)
            {
                int delta;
                pos = ReadVariableLength(track, pos, out delta);
                tick += delta;
                if (pos >= track.Length)
                {
                    throw new InvalidDataException("A MIDI event is missing its status byte.");
                }

                var start = pos;
                var first = track[pos];
                int status;
                if (first < 0x80)
                {
                    if (runningStatus < 0)
                    {
                        throw new InvalidDataException("Running status was used before any status byte.");
                    }
                    status = runningStatus;
                }
                else
                {
                    pos++;
                    status = first;
                    if (status < 0xF0)
                    {
                        runningStatus = status;
                    }
                    else if (status == 0xF0 || status == 0xF7 || status == 0xFF)
                    {
                        runningStatus = -1;
                    }
                }

                if (status == 0xFF)
                {
                    if (pos >= track.Length)
                    {
                        throw new InvalidDataException("A meta event is truncated.");
                    }
                    var metaType = track[pos++];
                    int length;
                    pos = ReadVariableLength(track, pos, out length);
                    if (pos + length > track.Length)
                    {
                        throw new InvalidDataException("A meta event value is truncated.");
                    }
                    pos += length;
                    events.Add(new TimedMidiEvent(track, start, pos - start, tick, metaType == 0x2F));
                    continue;
                }

                if (status == 0xF0 || status == 0xF7)
                {
                    int length;
                    pos = ReadVariableLength(track, pos, out length);
                    if (pos + length > track.Length)
                    {
                        throw new InvalidDataException("A SysEx event is truncated.");
                    }
                    pos += length;
                    events.Add(new TimedMidiEvent(track, start, pos - start, tick, false));
                    continue;
                }

                if (status < 0x80 || status > 0xEF)
                {
                    throw new InvalidDataException("Unsupported MIDI event status 0x" + status.ToString("X2") + ".");
                }

                var eventType = status & 0xF0;
                var dataLength = (eventType == 0xC0 || eventType == 0xD0) ? 1 : 2;
                if (pos + dataLength > track.Length)
                {
                    throw new InvalidDataException("A channel event is truncated.");
                }
                pos += dataLength;
                events.Add(new TimedMidiEvent(track, start, pos - start, tick, false));
            }

            return events;
        }

        private sealed class TimedMidiEvent
        {
            public readonly int Tick;
            public int Order;
            public readonly byte[] Data;
            public readonly bool IsEndOfTrack;

            public TimedMidiEvent(byte[] source, int offset, int length, int tick, bool isEndOfTrack)
            {
                Tick = tick;
                IsEndOfTrack = isEndOfTrack;
                Data = new byte[length];
                Buffer.BlockCopy(source, offset, Data, 0, length);
            }
        }

        private static string GetOutputPath(string inputPath, OutputMode mode, string selectedOutputFolder, bool addCleanedToFileName)
        {
            var inputDir = Path.GetDirectoryName(inputPath);
            var outputDir = mode == OutputMode.SingleFolder
                ? selectedOutputFolder
                : Path.Combine(inputDir, "Output");

            var stem = Path.GetFileNameWithoutExtension(inputPath);
            var extension = Path.GetExtension(inputPath);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".mid";
            }

            var baseName = addCleanedToFileName ? stem + " cleaned" : stem;
            var candidate = Path.Combine(outputDir, baseName + extension);
            var counter = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(outputDir, baseName + " " + counter + extension);
                counter++;
            }

            return candidate;
        }

        private static int ReadVariableLength(byte[] data, int pos, out int value)
        {
            value = 0;
            for (var i = 0; i < 4; i++)
            {
                if (pos >= data.Length)
                {
                    throw new InvalidDataException("Unexpected end of file while reading a variable-length value.");
                }
                var b = data[pos++];
                value = (value << 7) | (b & 0x7F);
                if ((b & 0x80) == 0)
                {
                    return pos;
                }
            }
            throw new InvalidDataException("Invalid variable-length value.");
        }

        private static void WriteVariableLength(List<byte> output, int value)
        {
            var buffer = value & 0x7F;
            value >>= 7;
            while (value > 0)
            {
                buffer <<= 8;
                buffer |= (value & 0x7F) | 0x80;
                value >>= 7;
            }

            while (true)
            {
                output.Add((byte)(buffer & 0xFF));
                if ((buffer & 0x80) != 0)
                {
                    buffer >>= 8;
                }
                else
                {
                    break;
                }
            }
        }

        private static ushort ReadUInt16(byte[] data, int pos)
        {
            return (ushort)((data[pos] << 8) | data[pos + 1]);
        }

        private static uint ReadUInt32(byte[] data, int pos)
        {
            return (uint)((data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3]);
        }

        private static void WriteUInt16(Stream stream, int value)
        {
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)(value & 0xFF));
        }

        private static void WriteUInt32(Stream stream, int value)
        {
            stream.WriteByte((byte)((value >> 24) & 0xFF));
            stream.WriteByte((byte)((value >> 16) & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)(value & 0xFF));
        }

        private static void WriteAscii(Stream stream, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}

