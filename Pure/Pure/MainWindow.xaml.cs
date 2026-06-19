using DiscordRPC;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Pure
{
    public partial class MainWindow : Window
    {
        private string scriptsPath;
        private string execworkspacePath;
        private string themesPath;
        private bool isWorkspaceVisible = true;
        private bool _safeModeEnabled = false;
        private DiscordRpcClient client;
        private bool showingDocs = true;
        private bool _isUpdating = true;
        private bool _isChatLoaded = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializeDiscordPresence();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await UpdateModule();
        }

        private void StartApplication()
        {
            var fadeOut = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromSeconds(0.5) };
            fadeOut.Completed += async (s, e) =>
            {
                UpdaterOverlay.Visibility = Visibility.Collapsed;
                await InitializeAsync();
                StartStatusIndicatorLoop();

                await Task.Delay(200);

                Storyboard sb = (Storyboard)this.Resources["Open"];
                sb.Begin();

                await Task.Delay(500);


                Workspace.Visibility = Visibility.Visible;
                Editor.Visibility = Visibility.Visible;

            };
            UpdaterOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        #region Updater Logic
        private void UpdateProgressBars(double percentage, string status)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateProgressBar.Value = percentage;
                PercentageText.Text = $"{percentage:F0}%";
                StatusText.Text = status;
            });
        }

        private async Task UpdateModule()
        {
            try
            {
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string binDirectory = System.IO.Path.Combine(baseDirectory, "bin");
                Directory.CreateDirectory(binDirectory);

                string dllPath = System.IO.Path.Combine(binDirectory, "module.dll");
                string injPath = System.IO.Path.Combine(binDirectory, "injector.exe");

                UpdateProgressBars(5, "Initializing download");
                await Task.Delay(500);

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Pure");
                    client.Timeout = TimeSpan.FromMinutes(5);

                    UpdateProgressBars(10, "Downloading module.dll");
                    var moduleBytes = await DownloadWithProgress(client,
                        "https://github.com/c4rguy/Pure/releases/download/release/Module.dll",
                        10, 45);

                    UpdateProgressBars(50, "Writing module.dll");
                    await File.WriteAllBytesAsync(dllPath, moduleBytes);
                    await Task.Delay(200);

                    UpdateProgressBars(55, "Downloading injector.exe");
                    var injectorBytes = await DownloadWithProgress(client,
                        "https://github.com/c4rguy/Pure/releases/download/release/Injector.exe",
                        55, 90);

                    UpdateProgressBars(95, "Writing injector.exe");
                    await File.WriteAllBytesAsync(injPath, injectorBytes);
                    await Task.Delay(200);

                    UpdateProgressBars(100, "Update completed successfully!");
                    await Task.Delay(500);
                }

                _isUpdating = false;
                StartApplication();
            }
            catch (Exception ex)
            {
                HandleError(ex);
            }
        }

        private async Task<byte[]> DownloadWithProgress(HttpClient client, string url, double startProgress, double endProgress)
        {
            try
            {
                using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var memoryStream = new MemoryStream())
                    {
                        var buffer = new byte[8192];
                        var totalRead = 0L;
                        int bytesRead;

                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await memoryStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes > 0)
                            {
                                var percentage = (double)totalRead / totalBytes;
                                var currentProgress = startProgress + (percentage * (endProgress - startProgress));
                                UpdateProgressBars(currentProgress, StatusText.Text);
                            }
                        }

                        return memoryStream.ToArray();
                    }
                }
            }
            catch (Exception)
            {
                return await client.GetByteArrayAsync(url);
            }
        }





        private void HandleError(Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                SubtitleText.Text = "Update failed";
                SubtitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));
                StatusText.Text = "An error occurred during update";
                PercentageText.Text = "Error";
                PercentageText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));
                UpdateProgressBar.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));
                ActionButton.Visibility = Visibility.Visible;
                _isUpdating = false;
            });

            MessageBox.Show($"Error updating files: {ex.Message}", "Update Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            ActionButton.Visibility = Visibility.Collapsed;
            ResetUI();
            UpdateModule();
        }

        private void ResetUI()
        {
            SubtitleText.Text = "Checking for updates...";
            SubtitleText.Foreground = (Brush)FindResource("SecondaryForeground");
            StatusText.Text = "Downloading files";
            PercentageText.Text = "0%";
            PercentageText.Foreground = (Brush)FindResource("AccentColor");
            UpdateProgressBar.Foreground = (Brush)FindResource("AccentColor");
            UpdateProgressBar.Value = 0;
            _isUpdating = true;
        }
        #endregion


        private async Task InitializeAsync()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

            scriptsPath = System.IO.Path.Combine(baseDirectory, "scripts");
            execworkspacePath = System.IO.Path.Combine(baseDirectory, "workspace");
            themesPath = System.IO.Path.Combine(baseDirectory, "Themes");

            await Editor.EnsureCoreWebView2Async();
            string editorPath = System.IO.Path.Combine(baseDirectory, "bin", "index.html");
            Editor.Source = new Uri(editorPath);
            Editor.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            Editor.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Editor.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            Editor.AllowExternalDrop = false;



            if (!Directory.Exists(scriptsPath))
            {
                Directory.CreateDirectory(scriptsPath);
            }


            if (!Directory.Exists(execworkspacePath))
            {
                Directory.CreateDirectory(execworkspacePath);
            }

            if (!Directory.Exists(themesPath))
            {
                Directory.CreateDirectory(themesPath);
            }

            await Workspace.EnsureCoreWebView2Async();
            string workspacePath = System.IO.Path.Combine(baseDirectory, "bin", "Workspace.html");
            Workspace.Source = new Uri(workspacePath);

            Editor.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            Editor.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            Workspace.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            Workspace.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            Editor.CoreWebView2.WebMessageReceived += EditorWebMessageReceived;
            Workspace.CoreWebView2.WebMessageReceived += WorkspaceWebMessageReceived;
            Workspace.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            Workspace.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Workspace.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            Workspace.AllowExternalDrop = false;
            LoadCustomThemes();

            string decompPath = System.IO.Path.Combine(baseDirectory, "bin", "decompiler.exe");

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = decompPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = System.IO.Path.GetDirectoryName(decompPath)
            };

            Process process = new Process { StartInfo = startInfo };
            process.Start();

        }


        private void StartStatusIndicatorLoop()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    bool portOpen = false;
                    try
                    {
                        using (var client = new TcpClient())
                        {
                            var result = client.BeginConnect("127.0.0.1", 6767, null, null);
                            portOpen = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1)) && client.Connected;
                        }
                    }
                    catch { portOpen = false; }

                    Dispatcher.Invoke(() =>
                    {
                        StatusIndicator.Fill = portOpen
                            ? (SolidColorBrush)(new BrushConverter().ConvertFrom("#7F78CE79")) 
                            : (SolidColorBrush)(new BrushConverter().ConvertFrom("#7FA56A6A"));
                    });

                    await Task.Delay(2000);
                }
            });
        }

        private void InitializeDiscordPresence()
        {
            var client = new DiscordRpcClient("1411808554741927956");

            client.Initialize();

            client.SetPresence(new RichPresence
            {
                Details = "Using Pure",
                State = "discord.gg/vygepHax4v",
                Timestamps = Timestamps.Now,
                Assets = new Assets
                {
                    LargeImageKey = "purenobg",
                    LargeImageText = "Pure"
                }
            });

            this.Closed += (s, e) => client.Dispose();
        }

        private void LoadTheme(string themeName)
        {
            switch (themeName.ToLower())
            {
                case "default":
                    SetDefaultTheme(null, null);
                    break;
                case "light":
                    SetLightTheme(null, null);
                    break;
                case "blue":
                    SetBlueTheme(null, null);
                    break;
                case "green":
                    SetGreenTheme(null, null);
                    break;
                case "purple":
                    SetPurpleTheme(null, null);
                    break;
                case "red":
                    SetRedTheme(null, null);
                    break;
                default:
                    var themeFile = System.IO.Path.Combine(themesPath, $"{themeName}.json");
                    if (File.Exists(themeFile))
                    {
                        string json = File.ReadAllText(themeFile);
                        var theme = JsonSerializer.Deserialize<Theme>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (theme?.Colors != null)
                        {
                            ApplyTheme(theme.Colors, theme.HtmlThemeName ?? theme.ThemeName.ToLower().Replace(" ", "-"));
                        }
                    }
                    break;
            }
        }

        private async void EnableChat(object sender, RoutedEventArgs e)
        {
            if (Workspace.CoreWebView2 == null)
                await Workspace.EnsureCoreWebView2Async();

            if (_isChatLoaded)
            {
                // Return to file browser mode
                WorkspaceBorder.Width = 160;
                WorkspaceBorder.Margin = new Thickness(0, 54, 540, 58);
                EditorBorder.Width = 532;
                EditorBorder.Margin = new Thickness(168, 54, 0, 58);
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string workspacePath = System.IO.Path.Combine(baseDirectory, "bin", "Workspace.html");
                if (File.Exists(workspacePath))
                    Workspace.Source = new Uri(workspacePath);
                Workspace.ZoomFactor = 0.9;
            }
            else
            {
                // Switch to chat mode
                WorkspaceBorder.Width = 260;
                WorkspaceBorder.Margin = new Thickness(0, 54, 440, 58);
                EditorBorder.Width = 432;
                EditorBorder.Margin = new Thickness(268, 54, 0, 58);
                Workspace.Source = new Uri("https://chat.getpure.xyz/");
                Workspace.ZoomFactor = 0.8;
            }

            _isChatLoaded = !_isChatLoaded;
        }



        private async void WorkspaceWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var message = JsonSerializer.Deserialize<WebMessage>(e.WebMessageAsJson);

            switch (message?.type)
            {
                case "requestScriptList":
                    LoadAndSendScriptList();
                    break;

                case "requestEditorContent":
                    string script = await Editor.CoreWebView2.ExecuteScriptAsync("window.GetText()");
                    string unescapedScript = JsonSerializer.Deserialize<string>(script);
                    var contentMessage = new { type = "recentScript", content = unescapedScript };
                    Workspace.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(contentMessage));
                    break;

                case "loadScript":
                    string scriptFilePath = System.IO.Path.Combine(scriptsPath, message.name);
                    if (File.Exists(scriptFilePath))
                    {
                        string content = await File.ReadAllTextAsync(scriptFilePath);
                        await Editor.CoreWebView2.ExecuteScriptAsync($"window.SetText({JsonSerializer.Serialize(content)})");
                    }
                    break;

                case "loadScriptContent":
                    await Editor.CoreWebView2.ExecuteScriptAsync($"window.SetText({JsonSerializer.Serialize(message.content)})");
                    break;
            }
        }

        private void EditorWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {

        }

        private void LoadAndSendScriptList()
        {
            try
            {
                var scriptFiles = Directory.GetFiles(scriptsPath, "*.lua").Select(System.IO.Path.GetFileName).ToArray();
                var message = new { type = "scriptList", files = scriptFiles };
                Workspace.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading scripts: {ex.Message}");
            }
        }

        private void ToggleWorkspace(object sender, RoutedEventArgs e)
        {
            if (isWorkspaceVisible)
            {
                DoubleAnimation fadeOut = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromSeconds(0.2)
                };

                fadeOut.Completed += (s, a) =>
                {
                    WorkspaceBorder.Visibility = Visibility.Collapsed;
                    WorkspaceBorder.Opacity = 1;

                    ThicknessAnimation expandEditor = new ThicknessAnimation
                    {
                        From = EditorBorder.Margin,
                        To = new Thickness(0, 54, 0, 58),
                        Duration = TimeSpan.FromSeconds(0.1)
                    };
                    EditorBorder.BeginAnimation(Border.MarginProperty, expandEditor);

                    DoubleAnimation widthAnimation = new DoubleAnimation
                    {
                        To = 700,
                        Duration = TimeSpan.FromSeconds(0.1)
                    };
                    EditorBorder.BeginAnimation(Border.WidthProperty, widthAnimation);
                };

                WorkspaceBorder.BeginAnimation(Border.OpacityProperty, fadeOut);
            }
            else
            {
                WorkspaceBorder.Visibility = Visibility.Visible;
                WorkspaceBorder.Opacity = 0;

                DoubleAnimation fadeIn = new DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = TimeSpan.FromSeconds(0.2)
                };
                WorkspaceBorder.BeginAnimation(Border.OpacityProperty, fadeIn);

                ThicknessAnimation shrinkEditor = new ThicknessAnimation
                {
                    From = EditorBorder.Margin,
                    To = new Thickness(168, 54, 0, 58),
                    Duration = TimeSpan.FromSeconds(0.2)
                };
                EditorBorder.BeginAnimation(Border.MarginProperty, shrinkEditor);

                DoubleAnimation widthAnimation = new DoubleAnimation
                {
                    To = 532,
                    Duration = TimeSpan.FromSeconds(0.2)
                };
                EditorBorder.BeginAnimation(Border.WidthProperty, widthAnimation);
            }

            isWorkspaceVisible = !isWorkspaceVisible;
        }


        private void TopMost(object sender, RoutedEventArgs e)
        {
            this.Topmost = !this.Topmost;
        }

        private DispatcherTimer robloxTimer;
        private bool robloxOpen;

        private void AutoAttach(object sender, RoutedEventArgs e)
        {
            if (robloxTimer == null)
            {
                robloxTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                robloxTimer.Tick += (s, ev) =>
                {
                    bool running = System.Diagnostics.Process.GetProcessesByName("RobloxPlayerBeta").Length > 0;

                    if (running && !robloxOpen)
                    {
                        robloxOpen = true;
                        Attach();
                    }
                    else if (!running && robloxOpen)
                    {
                        robloxOpen = false;
                    }
                };
            }

            if (robloxTimer.IsEnabled)
            {
                robloxTimer.Stop();
            }
            else
            {
                robloxTimer.Start();
            }
        }
        private void Attach()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string injectorPath = System.IO.Path.Combine(baseDirectory, "bin", "injector.exe");

            try
            {
                if (File.Exists(injectorPath))
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = injectorPath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        WorkingDirectory = System.IO.Path.GetDirectoryName(injectorPath)
                    };

                    Process.Start(startInfo);
                }
                else
                {
                    MessageBox.Show("injector.exe not found in the bin folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to run injector.exe:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.ExecuteScriptAsync("editor.setValue(' ');");
        }

        private async void SaveFile(object sender, RoutedEventArgs e)
        {
            try
            {
                string rawResult = await Editor.ExecuteScriptAsync("editor.getValue()");
                string editorContent = JsonSerializer.Deserialize<string>(rawResult);

                if (string.IsNullOrWhiteSpace(editorContent))
                {
                    MessageBox.Show("Editor is empty. Nothing to save.");
                    return;
                }

                string[] words = editorContent
                    .Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                string fileNamePart = string.Join("_", words.Take(3));
                if (string.IsNullOrWhiteSpace(fileNamePart))
                {
                    fileNamePart = "untitled";
                }

                string fileName = $"{fileNamePart}.lua";

                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                {
                    fileName = fileName.Replace(c, '_');
                }
                string fullPath = System.IO.Path.Combine(scriptsPath, fileName);
                File.WriteAllText(fullPath, editorContent);
                LoadAndSendScriptList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file: {ex.Message}");
            }
        }
        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void OpenFile(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                InitialDirectory = scriptsPath,
                Filter = "Script files (*.txt;*.lua)|*.txt;*.lua|All files (*.*)|*.*",
                Title = "Open Script File"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    string fileContent = File.ReadAllText(dlg.FileName);

                    string jsSafeContent = fileContent
                        .Replace("\\", "\\\\")
                        .Replace("'", "\\'")
                        .Replace("\r", "")
                        .Replace("\n", "\\n");

                    Editor.ExecuteScriptAsync($"editor.setValue('{jsSafeContent}');");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error opening file: {ex.Message}");
                }
            }
        }

        private void Discord(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://discord.gg/JRKtVK4G8K") { UseShellExecute = true });
        }

        private void OpenFolder(object sender, RoutedEventArgs e)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{scriptsPath}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        private void WorkspaceFolder(object sender, RoutedEventArgs e)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{execworkspacePath}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        private async Task FadeOutAsync(UIElement element, double durationSeconds = 0.1)
        {
            if (Workspace != null) Workspace.Visibility = Visibility.Collapsed;
            if (Editor != null) Editor.Visibility = Visibility.Collapsed;

            var tcs = new TaskCompletionSource<bool>();

            if (!(element.RenderTransform is ScaleTransform scaleTransform))
            {
                scaleTransform = new ScaleTransform(1, 1);
                element.RenderTransform = scaleTransform;
                element.RenderTransformOrigin = new Point(0, 1);
            }

            var fadeOut = new DoubleAnimation
            {
                From = element.Opacity,
                To = 0.0,
                Duration = TimeSpan.FromSeconds(durationSeconds)
            };

            var scaleXAnim = new DoubleAnimation { From = 1.0, To = 0.0, Duration = TimeSpan.FromSeconds(durationSeconds) };
            var scaleYAnim = new DoubleAnimation { From = 1.0, To = 0.0, Duration = TimeSpan.FromSeconds(durationSeconds) };

            fadeOut.Completed += (s, e) => tcs.SetResult(true);

            element.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);

            await tcs.Task;

            this.Close();
        }

        private async Task FadeInAsync(UIElement element, double durationSeconds = 0.2)
        {
            var fadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(durationSeconds)
            };
            element.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds));
        }

        private async void Close(object sender, RoutedEventArgs e)
        {
            await FadeOutAsync(WindowGrid);
        }

        private void Minimise(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Attach_Click(object sender, RoutedEventArgs e)
        {
            Attach();
        }

        private async void ExecuteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string scriptText = await Editor.CoreWebView2.ExecuteScriptAsync("editor.getValue();");
                scriptText = JsonSerializer.Deserialize<string>(scriptText);

                await Execute(scriptText);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error retrieving editor text: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetDefaultTheme(object sender, RoutedEventArgs e)
        {
            var colors = new ThemeColors
            {
                PrimaryBg = "#FF353535",
                SecondaryBg = "#FF2D2D2D",
                TertiaryBg = "#FF1e1e1e",
                PrimaryFg = "#fefefe",
                SecondaryFg = "#FF444444",
                Accent = "#abe1f3",
                Border = "#FF444444",
                Hover = "#FF3A3A3A",
                Pressed = "#FF4A4A4A"
            };
            ApplyTheme(colors, "default");
        }

        private void SetLightTheme(object sender, RoutedEventArgs e)
        {
            var colors = new ThemeColors
            {
                PrimaryBg = "#FFFFFF",
                SecondaryBg = "#F5F5F5",
                TertiaryBg = "#E5E5E5",
                PrimaryFg = "#000000",
                SecondaryFg = "#666666",
                Accent = "#0066CC",
                Border = "#CCCCCC",
                Hover = "#E0E0E0",
                Pressed = "#D0D0D0"
            };
            ApplyTheme(colors, "light");
        }

        private void SetBlueTheme(object sender, RoutedEventArgs e)
        {
            var colors = new ThemeColors
            {
                PrimaryBg = "#FF1e3a5f",
                SecondaryBg = "#FF1a2f4a",
                TertiaryBg = "#FF152535",
                PrimaryFg = "#E6F3FF",
                SecondaryFg = "#FF5a8fb8",
                Accent = "#FF66B2FF",
                Border = "#FF4a7db8",
                Hover = "#FF2a4a6f",
                Pressed = "#FF3a5a7f"
            };
            ApplyTheme(colors, "blue");
        }

        private void SetGreenTheme(object sender, RoutedEventArgs e)
        {
            var colors = new ThemeColors
            {
                PrimaryBg = "#FF1a4a2e",
                SecondaryBg = "#FF153f26",
                TertiaryBg = "#FF10351e",
                PrimaryFg = "#E6FFE6",
                SecondaryFg = "#FF5ab85a",
                Accent = "#FF66FF66",
                Border = "#FF4ab84a",
                Hover = "#FF2a5a3a",
                Pressed = "#FF3a6a4a"
            };
            ApplyTheme(colors, "green");
        }

        private void SetPurpleTheme(object sender, RoutedEventArgs e)
        {
            var colors = new ThemeColors
            {
                PrimaryBg = "#FF4a1a4a",
                SecondaryBg = "#FF3f153f",
                TertiaryBg = "#FF35103e",
                PrimaryFg = "#FFE6FF",
                SecondaryFg = "#FFb85ab8",
                Accent = "#FFBB66FF",
                Border = "#FFb84ab8",
                Hover = "#FF5a2a5a",
                Pressed = "#FF6a3a6a"
            };
            ApplyTheme(colors, "purple");
        }

        private void SetRedTheme(object sender, RoutedEventArgs e)
        {
            var colors = new ThemeColors
            {
                PrimaryBg = "#FF4a1a1a",
                SecondaryBg = "#FF3f1515",
                TertiaryBg = "#FF351010",
                PrimaryFg = "#FFE6E6",
                SecondaryFg = "#FFb85a5a",
                Accent = "#FFFF6666",
                Border = "#FFb84a4a",
                Hover = "#FF5a2a2a",
                Pressed = "#FF6a3a3a"
            };
            ApplyTheme(colors, "red");
        }

        private async void ApplyTheme(ThemeColors colors, string htmlThemeName)
        {

            var resources = this.Resources;
            resources["PrimaryBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.PrimaryBg));
            resources["SecondaryBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.SecondaryBg));
            resources["TertiaryBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.TertiaryBg));
            resources["PrimaryForeground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.PrimaryFg));
            resources["SecondaryForeground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.SecondaryFg));
            resources["AccentColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Accent));
            resources["BorderColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Border));
            resources["HoverColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Hover));
            resources["PressedColor"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors.Pressed));

            var cardGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };
            cardGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(colors.TertiaryBg), 0));
            cardGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(colors.SecondaryBg), 1));
            resources["CardBackground"] = cardGradient;

            await SendThemeToWebViews(colors, htmlThemeName);
        }

        private async Task SendThemeToWebViews(ThemeColors colors, string htmlThemeName)
        {
            object themePayload;
            string[] builtInThemes = { "default", "light", "blue", "green", "purple", "red" };

            if (builtInThemes.Contains(htmlThemeName))
            {
                themePayload = htmlThemeName;
            }
            else
            {
                var cssVars = new Dictionary<string, string>
        {
            { "--bg-color", ConvertWpfHexToCssHex(colors.SecondaryBg) },
            { "--text-color", ConvertWpfHexToCssHex(colors.PrimaryFg) },
            { "--accent-color", ConvertWpfHexToCssHex(colors.Accent) },
            { "--hover-color", ConvertWpfHexToCssHex(colors.Hover) },
            { "--border-color", ConvertWpfHexToCssHex(colors.Border) },
            { "--secondary-bg", ConvertWpfHexToCssHex(colors.PrimaryBg) },
            { "--secondary-text", ConvertWpfHexToCssHex(colors.SecondaryFg) },
            { "--pressed-color", ConvertWpfHexToCssHex(colors.Pressed) }
        };
                themePayload = new { type = "custom", colors = cssVars };
            }

            string jsonPayloadForTheme = JsonSerializer.Serialize(themePayload);

            try
            {
                string script = $"window.applyTheme({jsonPayloadForTheme});";

                if (Workspace?.CoreWebView2 != null)
                {
                    await Workspace.CoreWebView2.ExecuteScriptAsync(script);
                }

                if (Editor?.CoreWebView2 != null)
                {
                    await Editor.CoreWebView2.ExecuteScriptAsync(script);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Theme script execution error: {ex.Message}");
            }
        }

        private void LoadCustomThemes()
        {
            try
            {
                for (int i = ThemesMenu.Items.Count - 1; i >= 0; i--)
                {
                    if (ThemesMenu.Items[i] is MenuItem menuItem && menuItem.Tag?.ToString() == "CustomTheme")
                    {
                        ThemesMenu.Items.RemoveAt(i);
                    }
                    else if (ThemesMenu.Items[i] is Separator separator && separator.Tag?.ToString() == "CustomThemeSeparator")
                    {
                        ThemesMenu.Items.RemoveAt(i);
                    }
                }

                var themeFiles = Directory.GetFiles(themesPath, "*.json");
                if (themeFiles.Length == 0) return;

                var customSeparator = new Separator { Tag = "CustomThemeSeparator" };
                ThemesMenu.Items.Add(customSeparator);

                foreach (var file in themeFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(file);
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        Theme theme = JsonSerializer.Deserialize<Theme>(json, options);

                        if (theme?.Colors != null && !string.IsNullOrEmpty(theme.ThemeName))
                        {
                            var menuItem = new MenuItem
                            {
                                Header = theme.ThemeName,
                                Style = (Style)FindResource("SubMenuItemStyle"),
                                Tag = "CustomTheme"
                            };

                            var canvas = new Canvas { Width = 14, Height = 14 };
                            var ellipse = new Ellipse
                            {
                                Width = 10,
                                Height = 10,
                                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme.Colors.SecondaryBg ?? "#FF3f1515")),
                                Stroke = (Brush)FindResource("SecondaryForeground"),
                                StrokeThickness = 1
                            };
                            canvas.Children.Add(ellipse);
                            menuItem.Icon = canvas;

                            menuItem.Click += (s, e) =>
                            {
                                ApplyTheme(theme.Colors, theme.HtmlThemeName ?? theme.ThemeName.ToLower().Replace(" ", "-"));
                            };

                            ThemesMenu.Items.Add(menuItem);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to load theme {System.IO.Path.GetFileName(file)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading custom themes: {ex.Message}");
            }
        }


        private string ConvertWpfHexToCssHex(string wpfHex)
        {
            if (string.IsNullOrEmpty(wpfHex) || !wpfHex.StartsWith("#") || wpfHex.Length != 9)
            {
                return wpfHex;
            }

            string alpha = wpfHex.Substring(1, 2);
            string color = wpfHex.Substring(3);

            return $"#{color}{alpha}";
        }

        private async Task Execute(string scriptText)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    await client.ConnectAsync("127.0.0.1", 6767);

                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] data = Encoding.UTF8.GetBytes(scriptText);
                        await stream.WriteAsync(data, 0, data.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending script: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

        private const uint WDA_NONE = 0x0;
        private const uint WDA_MONITOR = 0x1;

        public void ToggleSafeMode(object sender, RoutedEventArgs e)
        {
            _safeModeEnabled = !_safeModeEnabled;

            var hwnd = new WindowInteropHelper(this).Handle;

            if (_safeModeEnabled)
            {
                this.ShowInTaskbar = false;
                var currentStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, currentStyle | NativeMethods.WS_EX_TOOLWINDOW);
                SetWindowDisplayAffinity(hwnd, WDA_MONITOR);
            }
            else
            {
                this.ShowInTaskbar = true;

                var currentStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, (currentStyle & ~NativeMethods.WS_EX_TOOLWINDOW));
                SetWindowDisplayAffinity(hwnd, WDA_NONE);
            }
        }

        private void OpenThemesFolder(object sender, RoutedEventArgs e)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{themesPath}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
        }


        private void Menu_DragOver(object sender, DragEventArgs e)
        {
            MessageBox.Show("DragOver");
        }

        private void ToolbarBorder_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Any(f => f.EndsWith(".lua") || f.EndsWith(".txt")))
                {
                    e.Effects = DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            e.Handled = true;
        }

        private async void ToolbarBorder_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                string file = files.FirstOrDefault(f => f.EndsWith(".lua") || f.EndsWith(".txt"));
                if (file != null)
                {
                    string content = await File.ReadAllTextAsync(file);
                    string jsSafeContent = System.Text.Json.JsonSerializer.Serialize(content);

                    await Editor.ExecuteScriptAsync($"editor.setValue({jsSafeContent});");
                }
            }
        }

        private void ThemesMenu_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files.Any(f => System.IO.Path.GetExtension(f).Equals(".json", System.StringComparison.OrdinalIgnoreCase)))
                {
                    e.Effects = System.Windows.DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = System.Windows.DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void ThemesMenu_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);

                foreach (string file in files)
                {
                    if (System.IO.Path.GetExtension(file).Equals(".json", System.StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            string destFile = System.IO.Path.Combine(themesPath, System.IO.Path.GetFileName(file));

                            System.IO.Directory.CreateDirectory(themesPath);
                            System.IO.File.Copy(file, destFile, true);
                        }
                        catch (System.Exception ex)
                        {
                            System.Windows.MessageBox.Show($"Error copying file: {ex.Message}");
                        }
                    }
                }
            }
            LoadCustomThemes();

        }
    }


    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }


    public class WebMessage
    {
        public string type { get; set; }
        public string name { get; set; }
        public string content { get; set; }
    }
    public class Theme
    {
        public string ThemeName { get; set; }
        public ThemeColors Colors { get; set; }
        public string HtmlThemeName { get; set; }
    }

    public class ThemeColors
    {
        public string PrimaryBg { get; set; }
        public string SecondaryBg { get; set; }
        public string TertiaryBg { get; set; }
        public string PrimaryFg { get; set; }
        public string SecondaryFg { get; set; }
        public string Accent { get; set; }
        public string Border { get; set; }
        public string Hover { get; set; }
        public string Pressed { get; set; }
    }
}