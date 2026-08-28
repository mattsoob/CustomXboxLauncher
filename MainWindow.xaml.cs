using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace XboxLauncherApp
{
    public class GameItem
    {
        public string Title { get; set; } = string.Empty;
        public string LaunchUri { get; set; } = string.Empty;
    }

    public partial class MainWindow : Window
    {
        public ObservableCollection<GameItem> Games { get; set; } = new ObservableCollection<GameItem>();
        private int lastTabIndex = 1;

        public MainWindow()
        {
            InitializeComponent();

            // Setup Startup Splash Screen Video immediately
            string videoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "xbox360_startup.mp4");
            if (File.Exists(videoPath))
            {
                StartupMedia.Source = new Uri(videoPath, UriKind.Absolute);
            }
            else
            {
                StartupMedia.Visibility = Visibility.Collapsed;
            }

            GameLibraryItemsControl.ItemsSource = Games;
            ScanSteamGames();
            ScanRegistryApps();
        }

        private void PlaySound(string fileName)
        {
            try
            {
                string soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName);
                if (File.Exists(soundPath))
                {
                    MediaPlayer player = new MediaPlayer();
                    player.Open(new Uri(soundPath, UriKind.Absolute));
                    player.Play();
                }
            }
            catch
            {
                /* Ignore playback issues */
            }
        }

        private void BladesTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BladesTabControl == null) return;

            int newIndex = BladesTabControl.SelectedIndex;
            if (newIndex == lastTabIndex) return;

            // Play navigation audio cue
            PlaySound("nav_move.wav");

            double startX = (newIndex > lastTabIndex) ? 120 : -120;
            lastTabIndex = newIndex;

            Grid? contentHolder = BladesTabControl.Template.FindName("ContentHolder", BladesTabControl) as Grid;
            if (contentHolder != null)
            {
                TranslateTransform? transform = contentHolder.RenderTransform as TranslateTransform;
                if (transform != null)
                {
                    DoubleAnimation slideAnim = new DoubleAnimation
                    {
                        From = startX,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(200),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };

                    DoubleAnimation fadeAnim = new DoubleAnimation
                    {
                        From = 0.3,
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(200)
                    };

                    transform.BeginAnimation(TranslateTransform.XProperty, slideAnim);
                    contentHolder.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
                }
            }
        }

        private void GameCard_Click(object sender, RoutedEventArgs e)
        {
            PlaySound("nav_select.wav");

            if (sender is Button btn && btn.Tag is string launchUri)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = launchUri,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not launch item: {ex.Message}");
                }
            }
        }

        private void ScanGamesButton_Click(object sender, RoutedEventArgs e)
        {
            PlaySound("nav_select.wav");
            Games.Clear();
            ScanSteamGames();
            ScanRegistryApps();
        }

        private void StartupMedia_MediaEnded(object sender, RoutedEventArgs e)
        {
            // 1. Hide the startup splash screen layer
            StartupMedia.Visibility = Visibility.Collapsed;

            // 2. Play the home screen welcome sound immediately
            PlaySound("welcome.wav");

            // 3. Trigger the welcome sign-in banner & sound
            ShowWelcomeNotification();
        }

        private void ShowWelcomeNotification()
        {
            // Play the sign-in notification sound (e.g., log_in.wav) along with the banner
            PlaySound("signin.wav");

            // Set active Windows username
            WelcomeUserNameText.Text = Environment.UserName;

            // Banner slide-in and slide-out animations
            DoubleAnimation slideIn = new DoubleAnimation
            {
                From = 0,
                To = -360,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            DoubleAnimation slideOut = new DoubleAnimation
            {
                From = -360,
                To = 0,
                BeginTime = TimeSpan.FromSeconds(4),
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            Storyboard story = new Storyboard();
            story.Children.Add(slideIn);
            story.Children.Add(slideOut);

            Storyboard.SetTargetName(slideIn, "BannerTransform");
            Storyboard.SetTargetProperty(slideIn, new PropertyPath(TranslateTransform.XProperty));

            Storyboard.SetTargetName(slideOut, "BannerTransform");
            Storyboard.SetTargetProperty(slideOut, new PropertyPath(TranslateTransform.XProperty));

            story.Begin(this);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.Back)
            {
                PlaySound("nav_back.wav");
            }
        }

        private void AddGameToLibrary(string title, string uri)
        {
            foreach (var game in Games)
            {
                if (game.Title.Equals(title, StringComparison.OrdinalIgnoreCase)) return;
            }
            Games.Add(new GameItem { Title = title, LaunchUri = uri });
        }

        private void ScanSteamGames()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        string? steamPath = key.GetValue("SteamPath") as string;
                        if (!string.IsNullOrEmpty(steamPath) && Directory.Exists(Path.Combine(steamPath, "steamapps")))
                        {
                            string manifestsPath = Path.Combine(steamPath, "steamapps");
                            foreach (string file in Directory.GetFiles(manifestsPath, "appmanifest_*.acf"))
                            {
                                string[] lines = File.ReadAllLines(file);
                                string appId = "";
                                string name = "";

                                foreach (string line in lines)
                                {
                                    if (line.Contains("\"appid\"")) appId = ExtractValue(line);
                                    if (line.Contains("\"name\"")) name = ExtractValue(line);
                                }

                                name = name.Replace("\"", "").Trim();

                                if (!string.IsNullOrEmpty(appId) && !string.IsNullOrEmpty(name))
                                {
                                    if (!IsSteamToolOrRuntime(name, appId))
                                    {
                                        AddGameToLibrary(name, $"steam://run/{appId}");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private bool IsSteamToolOrRuntime(string title, string appId)
        {
            string[] ignoredAppIds = { "250820", "228980", "105600" };
            foreach (string id in ignoredAppIds)
            {
                if (appId == id) return true;
            }

            string[] toolKeywords = { "Steamworks", "Redistributable", "SteamVR", "Soundtrack", "Dedicated Server", "SDK", "Tool", "Benchmark" };
            foreach (string keyword in toolKeywords)
            {
                if (title.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        private void ScanRegistryApps()
        {
            string[] registryKeys = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            string[] blacklist = { "Cisco", "Webex", "DiscordBot", "Opera", "Elevate", "Uninstall", "Host", "Launcher", "Helper", "Redistributable", "Runtime", "Microsoft", "Windows", "Driver", "Update", "Codec", "Python", "Node", "VS Code", "Visual Studio", "Adobe", "Chrome", "Edge" };

            foreach (string keyPath in registryKeys)
            {
                using (RegistryKey? rootKey = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (rootKey == null) continue;

                    foreach (string subkeyName in rootKey.GetSubKeyNames())
                    {
                        using (RegistryKey? subkey = rootKey.OpenSubKey(subkeyName))
                        {
                            if (subkey == null) continue;

                            string? displayName = subkey.GetValue("DisplayName") as string;
                            string? installLocation = subkey.GetValue("InstallLocation") as string;

                            if (string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(installLocation)) continue;

                            displayName = displayName.Replace("\"", "").Trim();

                            bool isBlacklisted = false;
                            foreach (string item in blacklist)
                            {
                                if (displayName.Contains(item, StringComparison.OrdinalIgnoreCase))
                                {
                                    isBlacklisted = true;
                                    break;
                                }
                            }
                            if (isBlacklisted) continue;

                            if (Directory.Exists(installLocation))
                            {
                                string[] exes = Directory.GetFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly);
                                foreach (string exe in exes)
                                {
                                    string exeName = Path.GetFileName(exe);
                                    if (!exeName.StartsWith("unins", StringComparison.OrdinalIgnoreCase) &&
                                        !exeName.Contains("setup", StringComparison.OrdinalIgnoreCase) &&
                                        !exeName.Contains("host", StringComparison.OrdinalIgnoreCase))
                                    {
                                        AddGameToLibrary(displayName, exe);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private string ExtractValue(string line)
        {
            string[] parts = line.Split('\t');
            if (parts.Length > 1)
            {
                return parts[parts.Length - 1].Replace("\"", "").Trim();
            }
            return string.Empty;
        }
    }
}