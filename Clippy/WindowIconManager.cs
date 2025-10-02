using System;
using System.IO;
using System.Runtime.InteropServices;

using WinUIEx;
using WinUIEx.Messaging;

namespace Clippy
{
    public class WindowIconManager : IDisposable
    {
        public string Tooltip = "Clippy";

        private readonly TrayFlyoutWindow _trayWindow;
        private WindowMessageMonitor _messageMonitor;
        private IntPtr _hwnd;
        private IntPtr _hIcon = IntPtr.Zero;
        private bool _added;
        private bool _shouldDestroyIcon;

        // Tray constants
        private const int NIM_ADD = 0x00000000;

        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;

        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;

        private const int WM_APP = 0x8000;
        private const int WM_TRAYICON = WM_APP + 1;

        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_CONTEXTMENU = 0x007B;

        // Context menu
        private const uint MF_STRING = 0x00000000;

        private const uint TPM_RIGHTBUTTON = 0x00000002;
        private const uint TPM_RETURNCMD = 0x00000100;
        private const int ID_SHOW_CLIPPY = 1001;
        private const int ID_EXIT = 1002;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;

            // We intentionally keep the struct minimal (v1) as we only use tip, icon and callback.
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadImageW(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x00000010;
        private const uint LR_DEFAULTSIZE = 0x00000040;
        private const int IDI_APPLICATION = 32512;

        public WindowIconManager(TrayFlyoutWindow trayFlyoutWindow)
        {
            _trayWindow = trayFlyoutWindow;
        }

        public Action<object, object> TrayIconClicked { get; set; }

        public string IconPath { get; set; }

        public void Show()
        {
            if (_added) return;

            // Get HWND for the tray window (hidden by default)
            _hwnd = (IntPtr)_trayWindow.GetWindowHandle();

            // Monitor messages to capture tray callbacks
            _messageMonitor = new WindowMessageMonitor(_trayWindow);
            _messageMonitor.WindowMessageReceived += OnWindowMessageReceived;

            // Load icon (prefer packaged Clippy.ico)
            var iconPath = IconPath;
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Clippy", "Clippy.ico");
            }

            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                _hIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                _shouldDestroyIcon = _hIcon != IntPtr.Zero; // LoadImage returns an owned HICON
            }

            if (_hIcon == IntPtr.Zero)
            {
                // Fallback to shared application icon; do not destroy this one
                _hIcon = LoadIconW(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
                _shouldDestroyIcon = false;
            }

            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1u,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
                szTip = string.IsNullOrWhiteSpace(Tooltip) ? "Clippy" : Tooltip
            };

            _added = Shell_NotifyIconW(NIM_ADD, ref data);
        }

        private void OnWindowMessageReceived(object sender, WindowMessageEventArgs e)
        {
            if (e.Message.MessageId == WM_TRAYICON)
            {
                int msg = (int)e.Message.LParam;
                if (msg == WM_LBUTTONUP)
                {
                    TrayIconClicked?.Invoke(this, EventArgs.Empty);
                }
                else if (msg == WM_RBUTTONUP || msg == WM_CONTEXTMENU)
                {
                    ShowContextMenu();
                }
            }
        }

        private void ShowContextMenu()
        {
            try
            {
                var hMenu = CreatePopupMenu();
                if (hMenu == IntPtr.Zero) return;

                AppendMenuW(hMenu, MF_STRING, new IntPtr(ID_SHOW_CLIPPY), "Show flyout menu");
                AppendMenuW(hMenu, MF_STRING, new IntPtr(ID_EXIT), "Exit");

                if (!GetCursorPos(out var pt)) { DestroyMenu(hMenu); return; }

                // Required to ensure menu dismiss works correctly
                SetForegroundWindow(_hwnd);

                int cmd = TrackPopupMenuEx(hMenu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, _hwnd, IntPtr.Zero);

                DestroyMenu(hMenu);

                if (cmd == ID_SHOW_CLIPPY)
                {
                    TrayIconClicked?.Invoke(this, EventArgs.Empty);
                }
                else if (cmd == ID_EXIT)
                {
                    Microsoft.UI.Xaml.Application.Current.Exit();
                }
            }
            catch
            {
                // ignore
            }
        }

        public void Dispose()
        {
            try
            {
                if (_added)
                {
                    var data = new NOTIFYICONDATA
                    {
                        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                        hWnd = _hwnd,
                        uID = 1u
                    };
                    Shell_NotifyIconW(NIM_DELETE, ref data);
                    _added = false;
                }

                _messageMonitor?.Dispose();
                _messageMonitor = null;

                if (_shouldDestroyIcon && _hIcon != IntPtr.Zero)
                {
                    DestroyIcon(_hIcon);
                    _hIcon = IntPtr.Zero;
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}