using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Hardcodet.Wpf.TaskbarNotification;

namespace MonitorHotKeys
{
    public struct MonitorInfo
    {
        public int Number { get; set; }
        public string DeviceName { get; set; }
        public string FriendlyName { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsEnabled { get; set; }
    }

    internal class HoldState
    {
        private readonly int _hotKeyId;
        private readonly Action _onComplete;
        private readonly int _holdDurationMs;
        private readonly System.Timers.Timer _timer;
        private readonly System.Diagnostics.Stopwatch _stopwatch;
        private bool _isCancelled = false;

        public HoldState(int hotKeyId, Action onComplete, int holdDurationMs)
        {
            _hotKeyId = hotKeyId;
            _onComplete = onComplete;
            _holdDurationMs = holdDurationMs;
            _stopwatch = new System.Diagnostics.Stopwatch();
            _timer = new System.Timers.Timer { Interval = 100, AutoReset = true };
            _timer.Elapsed += OnTimerElapsed;
        }

        public void Start()
        {
            _stopwatch.Restart();
            _timer.Start();
        }

        private void OnTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_isCancelled) return;

            if (_stopwatch.ElapsedMilliseconds >= _holdDurationMs)
            {
                _timer.Stop();
                _onComplete?.Invoke();
            }
        }

        public void Cancel()
        {
            _isCancelled = true;
            _timer?.Stop();
        }
    }

    public class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;
        public bool CanExecute(object parameter) => true;
        public event EventHandler CanExecuteChanged;
        public void Execute(object parameter) => _execute();
    }

    public partial class MainWindow : Window
    {
        private List<MonitorInfo> _monitors = new List<MonitorInfo>();
        private readonly string _monitorInfoFilePath;
        private string _currentLanguage = "ru";

        // === Трей иконка ===
        private TaskbarIcon? _notifyIcon;

        // === Win32 API ===
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DISPLAY_DEVICE
        {
            public uint cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        private const uint DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
        private const uint DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;
        private const uint DISPLAY_DEVICE_ACTIVE = 0x00000001;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private IntPtr _keyboardHook = IntPtr.Zero;
        private LowLevelKeyboardProc _keyboardProc;

        // === ID горячих клавиш ===
        private const int BASE_ID = 100;
        private const int HOTKEY_ALT_F1 = BASE_ID + 1;
        private const int HOTKEY_ALT_F2 = BASE_ID + 2;
        private const int HOTKEY_ALT_F3 = BASE_ID + 3;
        private const int HOTKEY_ALT_F10 = BASE_ID + 4;
        private const int HOTKEY_ALT_F11 = BASE_ID + 5;
        private const int HOTKEY_ALT_F12 = BASE_ID + 6;
        private const int HOTKEY_CTRL_ALT_F1 = BASE_ID + 7;
        private const int HOTKEY_CTRL_ALT_F2 = BASE_ID + 8;
        private const int HOTKEY_CTRL_ALT_F3 = BASE_ID + 9;
        private const int HOTKEY_SHIFT_ALT_F1 = BASE_ID + 13;
        private const int HOTKEY_SHIFT_ALT_F2 = BASE_ID + 14;
        private const int HOTKEY_SHIFT_ALT_F3 = BASE_ID + 15;
        private const int HOTKEY_SHIFT_ALT_F10 = BASE_ID + 16;
        private const int HOTKEY_SHIFT_ALT_F11 = BASE_ID + 17;
        private const int HOTKEY_SHIFT_ALT_F12 = BASE_ID + 18;
        private const int HOTKEY_OVERLAY = 999;

        // === Модификаторы и клавиши ===
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_F1 = 0x70;
        private const uint VK_F2 = 0x71;
        private const uint VK_F3 = 0x72;
        private const uint VK_F10 = 0x79;
        private const uint VK_F11 = 0x7A;
        private const uint VK_F12 = 0x7B;
        private const uint VK_HOME = 0x24;

        // === Настройки удержания ===
        internal const int HOLD_DURATION_MS = 1500;
        private readonly Dictionary<int, HoldState> _holdStates = new();
        private readonly object _holdLock = new object();
        private OverlayWindow? _currentOverlay;

        private readonly HashSet<int> _pressedKeys = new();
        private bool _isAltPressed = false;
        private bool _isCtrlPressed = false;
        private bool _isShiftPressed = false;

        private HwndSource? _hwndSource;

        public MainWindow()
        {
            _monitorInfoFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MonitorInfoList.json");

            InitializeComponent();
            this.WindowState = WindowState.Minimized;
            this.ShowInTaskbar = false;
            this.Visibility = Visibility.Hidden;

            LoadMonitors();

            _currentLanguage = LanguageManager.CurrentLanguage;
            CreateNotifyIcon();
            var helper = new WindowInteropHelper(this);
            helper.EnsureHandle();
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource.AddHook(WndProc);

            RegisterNonProfileHotKeys(helper.Handle);

            _keyboardProc = KeyboardHookProc;
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(null), 0);

        }

        private void LoadMonitors()
        {
            if (File.Exists(_monitorInfoFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_monitorInfoFilePath);
                    var loaded = JsonSerializer.Deserialize<List<MonitorInfo>>(json);
                    if (loaded != null)
                    {
                        _monitors = loaded;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка загрузки MonitorInfoList.json: {ex.Message}");
                }
            }

            LoadMonitorsFromSystem();
        }

        private int ExtractDisplayNumber(string deviceName)
        {
            // Ищем последнюю цифру в строке (например, \\.\DISPLAY5 → 5)
            var match = Regex.Match(deviceName, @"(\d+)$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int number))
                return number;
            return 1; // fallback
        }

        private void LoadMonitorsFromSystem()
        {
            _monitors.Clear();
            uint i = 0;

            while (true)
            {
                DISPLAY_DEVICE device = new DISPLAY_DEVICE();
                device.cb = (uint)Marshal.SizeOf(device);

                if (!EnumDisplayDevices(null, i, ref device, 0))
                    break;

                if ((device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
                {
                    i++;
                    continue;
                }

                bool isPrimary = (device.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;
                bool isEnabled = (device.StateFlags & DISPLAY_DEVICE_ACTIVE) != 0;

                int displayNumber = ExtractDisplayNumber(device.DeviceName);

                _monitors.Add(new MonitorInfo
                {
                    Number = displayNumber,
                    DeviceName = device.DeviceName,
                    FriendlyName = device.DeviceString,
                    IsPrimary = isPrimary,
                    IsEnabled = isEnabled
                });

                i++;
            }
        }

        private void SaveMonitorsToFile()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_monitors, options);
                File.WriteAllText(_monitorInfoFilePath, json, Encoding.UTF8);
                ShowResultOverlay("Список мониторов сохранён!", true);
            }
            catch (Exception ex)
            {
                ShowResultOverlay($"Ошибка сохранения: {ex.Message}", false);
            }
        }

        private void UpdateMonitorsToCurrent()
        {
            LoadMonitorsFromSystem();
            ShowResultOverlay("Актуальная конфигурация загружена!", true);
        }

        private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                bool isKeyDown = (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN);
                bool isKeyUp = (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP);

                if (vkCode == 0x12 || vkCode == 0xA4 || vkCode == 0xA5)
                {
                    if (isKeyDown) _isAltPressed = true;
                    else if (isKeyUp)
                    {
                        _isAltPressed = false;
                        CancelAllHoldProcesses();
                    }
                }
                else if (vkCode == 0x11 || vkCode == 0xA2 || vkCode == 0xA3)
                {
                    if (isKeyDown) _isCtrlPressed = true;
                    else if (isKeyUp) _isCtrlPressed = false;
                }
                else if (vkCode == 0x10 || vkCode == 0xA0 || vkCode == 0xA1)
                {
                    if (isKeyDown) _isShiftPressed = true;
                    else if (isKeyUp) _isShiftPressed = false;
                }

                if (IsFunctionKey(vkCode))
                {
                    if (isKeyDown)
                    {
                        if (!_pressedKeys.Contains(vkCode))
                        {
                            _pressedKeys.Add(vkCode);
                            HandleKeyCombination(vkCode);
                        }
                    }
                    else if (isKeyUp)
                    {
                        _pressedKeys.Remove(vkCode);

                        int hotKeyId = -1;
                        if (_isAltPressed && !_isCtrlPressed && !_isShiftPressed)
                            hotKeyId = GetAltHotKeyId(vkCode);
                        else if (_isCtrlPressed && _isAltPressed && !_isShiftPressed)
                            hotKeyId = GetCtrlAltHotKeyId(vkCode);
                        else if (_isShiftPressed && _isAltPressed && !_isCtrlPressed)
                            hotKeyId = GetShiftAltHotKeyId(vkCode);

                        if (hotKeyId != -1)
                        {
                            CancelHoldProcess(hotKeyId);
                        }
                    }
                }
            }

            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private bool IsFunctionKey(int vkCode)
        {
            return vkCode is >= 0x70 and <= 0x7B;
        }

        private void HandleKeyCombination(int vkCode)
        {
            if (_isAltPressed && !_isCtrlPressed && !_isShiftPressed)
            {
                int hotKeyId = GetAltHotKeyId(vkCode);
                if (hotKeyId != -1)
                {
                    StartHoldProcess(hotKeyId);
                    return;
                }
            }

            if (_isCtrlPressed && _isAltPressed && !_isShiftPressed)
            {
                int hotKeyId = GetCtrlAltHotKeyId(vkCode);
                if (hotKeyId != -1)
                {
                    StartHoldProcess(hotKeyId);
                    return;
                }
            }

            if (_isShiftPressed && _isAltPressed && !_isCtrlPressed)
            {
                int hotKeyId = GetShiftAltHotKeyId(vkCode);
                if (hotKeyId != -1)
                {
                    StartHoldProcess(hotKeyId);
                    return;
                }
            }

            if (_isCtrlPressed && _isAltPressed && vkCode == VK_HOME)
            {
                ShowHelpOverlay();
            }
        }

        private int GetAltHotKeyId(int vkCode)
        {
            return vkCode switch
            {
                0x70 => HOTKEY_ALT_F1,
                0x71 => HOTKEY_ALT_F2,
                0x72 => HOTKEY_ALT_F3,
                0x79 => HOTKEY_ALT_F10,
                0x7A => HOTKEY_ALT_F11,
                0x7B => HOTKEY_ALT_F12,
                _ => -1
            };
        }

        private int GetCtrlAltHotKeyId(int vkCode)
        {
            return vkCode switch
            {
                0x70 => HOTKEY_CTRL_ALT_F1,
                0x71 => HOTKEY_CTRL_ALT_F2,
                0x72 => HOTKEY_CTRL_ALT_F3,
                _ => -1
            };
        }

        private int GetShiftAltHotKeyId(int vkCode)
        {
            return vkCode switch
            {
                0x70 => HOTKEY_SHIFT_ALT_F1,
                0x71 => HOTKEY_SHIFT_ALT_F2,
                0x72 => HOTKEY_SHIFT_ALT_F3,
                0x79 => HOTKEY_SHIFT_ALT_F10,
                0x7A => HOTKEY_SHIFT_ALT_F11,
                0x7B => HOTKEY_SHIFT_ALT_F12,
                _ => -1
            };
        }

        private void CancelHoldProcess(int hotKeyId)
        {
            Dispatcher.Invoke(() =>
            {
                lock (_holdLock)
                {
                    if (_holdStates.TryGetValue(hotKeyId, out var holdState))
                    {
                        holdState.Cancel();
                        _holdStates.Remove(hotKeyId);
                    }

                    if (_holdStates.Count == 0)
                    {
                        _currentOverlay?.Close();
                        _currentOverlay = null;
                    }
                }
            });
        }

        private void CancelAllHoldProcesses()
        {
            Dispatcher.Invoke(() =>
            {
                lock (_holdLock)
                {
                    foreach (var holdState in _holdStates.Values)
                    {
                        holdState.Cancel();
                    }
                    _holdStates.Clear();

                    _currentOverlay?.Close();
                    _currentOverlay = null;
                }
            });
        }

        private void CreateNotifyIcon()
        { 
            if (_notifyIcon != null)
            {
                try
                {
                    var icon = new System.Drawing.Icon("Resources/app_icon.ico");
                    _notifyIcon.Icon = icon;
                }
                catch
                {
                    _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                }

                _notifyIcon.ToolTipText = LanguageManager.GetString("TrayTooltip");
                 
                UpdateContextMenu();
                return;
            }

            // первый запуск
            try
            {
                var icon = new System.Drawing.Icon("Resources/app_icon.ico");
                _notifyIcon = new TaskbarIcon
                {
                    Icon = icon,
                    ToolTipText = LanguageManager.GetString("TrayTooltip"),
                    Visibility = Visibility.Visible
                };
            }
            catch
            {
                _notifyIcon = new TaskbarIcon
                {
                    Icon = System.Drawing.SystemIcons.Application,
                    ToolTipText = LanguageManager.GetString("TrayTooltip"),
                    Visibility = Visibility.Visible
                };
            }

            UpdateContextMenu();
        }

        private void UpdateContextMenu()
        {
            if (_notifyIcon == null) return;

            var contextMenu = new ContextMenu();

            var updateMenuItem = new MenuItem { Header = LanguageManager.GetString("UpdateConfigMenuItem") };
            updateMenuItem.Click += (s, e) => UpdateMonitorsToCurrent();

            var saveMenuItem = new MenuItem { Header = LanguageManager.GetString("SaveMonitorsMenuItem") };
            saveMenuItem.Click += (s, e) => SaveMonitorsToFile();

            var languageMenu = new MenuItem { Header = LanguageManager.GetString("LanguageMenuItem") };

            var russianItem = new MenuItem
            {
                Header = LanguageManager.GetString("RussianMenuItem"),
                IsCheckable = true,
                IsChecked = _currentLanguage == "ru"
            };
            russianItem.Click += (s, e) => ChangeLanguage("ru");

            var englishItem = new MenuItem
            {
                Header = LanguageManager.GetString("EnglishMenuItem"),
                IsCheckable = true,
                IsChecked = _currentLanguage == "en"
            };
            englishItem.Click += (s, e) => ChangeLanguage("en");

            languageMenu.Items.Add(russianItem);
            languageMenu.Items.Add(englishItem);

            var exitMenuItem = new MenuItem { Header = LanguageManager.GetString("ExitMenuItem") };
            exitMenuItem.Click += (s, e) => Application.Current.Shutdown();

            contextMenu.Items.Add(updateMenuItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(saveMenuItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(languageMenu);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitMenuItem);

            _notifyIcon.ContextMenu = contextMenu;
        }

        private void ChangeLanguage(string langCode)
        {
            if (_currentLanguage == langCode) return;

            LanguageManager.SetLanguage(langCode);
            _currentLanguage = langCode;
             
            CreateNotifyIcon();

            ShowResultOverlay(
                langCode == "ru" ? "Язык изменён на русский" : "Language changed to English",
                true
            );
        }

        private void RegisterNonProfileHotKeys(IntPtr hWnd)
        {
            RegisterHotKey(hWnd, HOTKEY_OVERLAY, MOD_CONTROL | MOD_ALT, VK_HOME);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY)
            {
                int hotKeyId = wParam.ToInt32();
                if (hotKeyId == HOTKEY_OVERLAY)
                {
                    ShowHelpOverlay();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void StartHoldProcess(int hotKeyId)
        {
            Dispatcher.Invoke(() =>
            {
                lock (_holdLock)
                {
                    foreach (var holdState in _holdStates.Values)
                    {
                        holdState.Cancel();
                    }
                    _holdStates.Clear();

                    var newState = new HoldState(hotKeyId, () => ExecuteAction(hotKeyId), HOLD_DURATION_MS);
                    _holdStates[hotKeyId] = newState;
                    newState.Start();

                    ShowHoldOverlay(GetActionDescription(hotKeyId));
                }
            });
        }

        private string GetActionDescription(int hotKeyId)
        {
            return hotKeyId switch
            {
                HOTKEY_ALT_F1 => LanguageManager.GetString("ProfileLoad", 1),
                HOTKEY_ALT_F2 => LanguageManager.GetString("ProfileLoad", 2),
                HOTKEY_ALT_F3 => LanguageManager.GetString("ProfileLoad", 3),
                HOTKEY_ALT_F10 => LanguageManager.GetString("ProfileSave", 1),
                HOTKEY_ALT_F11 => LanguageManager.GetString("ProfileSave", 2),
                HOTKEY_ALT_F12 => LanguageManager.GetString("ProfileSave", 3),

                HOTKEY_CTRL_ALT_F1 => LanguageManager.GetString("SetPrimary", 1),
                HOTKEY_CTRL_ALT_F2 => LanguageManager.GetString("SetPrimary", 2),
                HOTKEY_CTRL_ALT_F3 => LanguageManager.GetString("SetPrimary", 3),

                HOTKEY_SHIFT_ALT_F1 => LanguageManager.GetString("EnableMonitor", 1),
                HOTKEY_SHIFT_ALT_F2 => LanguageManager.GetString("EnableMonitor", 2),
                HOTKEY_SHIFT_ALT_F3 => LanguageManager.GetString("EnableMonitor", 3),

                HOTKEY_SHIFT_ALT_F10 => LanguageManager.GetString("DisableMonitor", 1),
                HOTKEY_SHIFT_ALT_F11 => LanguageManager.GetString("DisableMonitor", 2),
                HOTKEY_SHIFT_ALT_F12 => LanguageManager.GetString("DisableMonitor", 3),

                _ => LanguageManager.GetString("ActionCompleted")
            };
        }

        private void ExecuteAction(int hotKeyId)
        {
            Dispatcher.Invoke(() =>
            {
                _currentOverlay?.Close();
                _currentOverlay = null;
            });

            try
            {
                string mmtPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MultiMonitorTool.exe");
                if (!File.Exists(mmtPath))
                {
                    Dispatcher.Invoke(() => ShowResultOverlay("Ошибка: MultiMonitorTool.exe не найден", false));
                    return;
                }

                // Получаем номер монитора из _monitors по индексу
                int GetMonitorNumberByIndex(int index)
                {
                    if (index < _monitors.Count && index >= 0)
                        return _monitors[index].Number;
                    // Если нет такого монитора — используем последний
                    return _monitors.Count > 0 ? _monitors[^1].Number : 1;
                }

                string args = hotKeyId switch
                {
                    HOTKEY_ALT_F1 or HOTKEY_ALT_F2 or HOTKEY_ALT_F3 or
                    HOTKEY_ALT_F10 or HOTKEY_ALT_F11 or HOTKEY_ALT_F12 => GetProfileArgs(hotKeyId),

                    HOTKEY_CTRL_ALT_F1 => $"/SetPrimary {GetMonitorNumberByIndex(0)}",
                    HOTKEY_CTRL_ALT_F2 => $"/SetPrimary {GetMonitorNumberByIndex(1)}",
                    HOTKEY_CTRL_ALT_F3 => $"/SetPrimary {GetMonitorNumberByIndex(2)}",

                    HOTKEY_SHIFT_ALT_F1 => $"/enable {GetMonitorNumberByIndex(0)}",
                    HOTKEY_SHIFT_ALT_F2 => $"/enable {GetMonitorNumberByIndex(1)}",
                    HOTKEY_SHIFT_ALT_F3 => $"/enable {GetMonitorNumberByIndex(2)}",

                    HOTKEY_SHIFT_ALT_F10 => $"/disable {GetMonitorNumberByIndex(0)}",
                    HOTKEY_SHIFT_ALT_F11 => $"/disable {GetMonitorNumberByIndex(1)}",
                    HOTKEY_SHIFT_ALT_F12 => $"/disable {GetMonitorNumberByIndex(2)}",

                    _ => throw new InvalidOperationException()
                };

                var startInfo = new ProcessStartInfo
                {
                    FileName = mmtPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(startInfo);
                process?.WaitForExit(3000);

                bool success = process != null && process.ExitCode == 0;

                Dispatcher.Invoke(() =>
                {
                    if (success)
                    {
                        ShowResultOverlay(GetSuccessMessage(hotKeyId), true);
                    }
                    else
                    {
                        ShowResultOverlay("Ошибка выполнения команды", false);
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => ShowResultOverlay($"Ошибка: {ex.Message}", false));
            }
        }

        private string GetProfileArgs(int hotKeyId)
        {
            string profilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Profiles");
            Directory.CreateDirectory(profilesDir);

            string profilePath = hotKeyId switch
            {
                HOTKEY_ALT_F1 or HOTKEY_ALT_F10 => Path.Combine(profilesDir, "profile1.cfg"),
                HOTKEY_ALT_F2 or HOTKEY_ALT_F11 => Path.Combine(profilesDir, "profile2.cfg"),
                HOTKEY_ALT_F3 or HOTKEY_ALT_F12 => Path.Combine(profilesDir, "profile3.cfg"),
                _ => throw new InvalidOperationException()
            };

            string command = hotKeyId switch
            {
                HOTKEY_ALT_F1 or HOTKEY_ALT_F2 or HOTKEY_ALT_F3 => "LoadConfig",
                HOTKEY_ALT_F10 or HOTKEY_ALT_F11 or HOTKEY_ALT_F12 => "SaveConfig",
                _ => throw new InvalidOperationException()
            };

            return $"/{command} \"{profilePath}\"";
        }

        private string GetSuccessMessage(int hotKeyId)
        {
            return hotKeyId switch
            {
                HOTKEY_ALT_F1 => LanguageManager.GetString("ProfileLoaded", 1),
                HOTKEY_ALT_F2 => LanguageManager.GetString("ProfileLoaded", 2),
                HOTKEY_ALT_F3 => LanguageManager.GetString("ProfileLoaded", 3),
                HOTKEY_ALT_F10 => LanguageManager.GetString("ProfileSaved", 1),
                HOTKEY_ALT_F11 => LanguageManager.GetString("ProfileSaved", 2),
                HOTKEY_ALT_F12 => LanguageManager.GetString("ProfileSaved", 3),

                HOTKEY_CTRL_ALT_F1 or HOTKEY_CTRL_ALT_F2 or HOTKEY_CTRL_ALT_F3 =>
                    LanguageManager.GetString("MonitorSetPrimary"),

                HOTKEY_SHIFT_ALT_F1 or HOTKEY_SHIFT_ALT_F2 or HOTKEY_SHIFT_ALT_F3 =>
                    LanguageManager.GetString("MonitorEnabled"),

                HOTKEY_SHIFT_ALT_F10 or HOTKEY_SHIFT_ALT_F11 or HOTKEY_SHIFT_ALT_F12 =>
                    LanguageManager.GetString("MonitorDisabled"),

                _ => LanguageManager.GetString("ActionCompleted")
            };
        }

        private void ShowHoldOverlay(string message)
        {
            _currentOverlay?.Close();
            _currentOverlay = new OverlayWindow(message, HOLD_DURATION_MS, isHoldOverlay: true);
            _currentOverlay.Topmost = true;
            _currentOverlay.Show();
        }

        private void ShowResultOverlay(string message, bool isSuccess)
        {
            _currentOverlay?.Close();
            string bgColor = isSuccess ? "#8000FF00" : "#80FF0000";
            _currentOverlay = new OverlayWindow(message, 2000, customBackground: bgColor);
            _currentOverlay.Topmost = true;
            _currentOverlay.Show();
        }

        private void ShowHelpOverlay()
        {
            var sb = new StringBuilder();
            sb.AppendLine(LanguageManager.GetString("HelpTitle"));
            sb.AppendLine(LanguageManager.GetString("HelpLoadProfile"));
            sb.AppendLine(LanguageManager.GetString("HelpSaveProfile"));
            sb.AppendLine(LanguageManager.GetString("HelpSetPrimary"));
            sb.AppendLine(LanguageManager.GetString("HelpEnableMonitor"));
            sb.AppendLine(LanguageManager.GetString("HelpDisableMonitor"));
            sb.AppendLine();
            sb.AppendLine(LanguageManager.GetString("HelpMonitorList"));

            if (_monitors.Count == 0)
            {
                sb.AppendLine(LanguageManager.GetString("HelpNoMonitors"));
            }
            else
            {
                foreach (var monitor in _monitors)
                {
                    string status = "";
                    if (monitor.IsPrimary) status += LanguageManager.GetString("PrimaryStatus");
                    if (!monitor.IsEnabled) status += LanguageManager.GetString("DisabledStatus");

                    string name = string.IsNullOrEmpty(monitor.FriendlyName)
                        ? monitor.DeviceName
                        : $"{monitor.FriendlyName} ({monitor.DeviceName})";

                    sb.AppendLine($"  {monitor.Number}. {name}{status}");
                }
            }

            _currentOverlay?.Close();
            _currentOverlay = new OverlayWindow(sb.ToString(), 8000, isHelpOverlay: true);
            _currentOverlay.Topmost = true;
            _currentOverlay.Show();
        }

        protected override void OnClosed(EventArgs e)
        {
            _currentOverlay?.Close();
            _notifyIcon?.Dispose();

            if (_keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }

            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                UnregisterHotKey(_hwndSource.Handle, HOTKEY_OVERLAY);
            }
            base.OnClosed(e);
        }
    }
}