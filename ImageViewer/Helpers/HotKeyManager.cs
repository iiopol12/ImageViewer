using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ImageViewer.Helpers
{
    /// <summary>
    /// 全局快捷键管理器 - 使用 Windows API 实现更可靠的快捷键响应
    /// </summary>
    public class HotKeyManager : IDisposable
    {
        #region Win32 API

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        private const int WM_HOTKEY = 0x0312;

        #endregion

        private readonly Window _window;
        private IntPtr _windowHandle;
        private HwndSource _source;
        private readonly Dictionary<int, Action> _hotKeyActions = new Dictionary<int, Action>();
        private int _currentId = 1000; // 起始 ID

        public HotKeyManager(Window window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));

            // 窗口加载后初始化
            if (_window.IsLoaded)
            {
                InitializeHwndSource();
            }
            else
            {
                _window.Loaded += (s, e) => InitializeHwndSource();
            }
        }

        /// <summary>
        /// 初始化窗口句柄和消息钩子
        /// </summary>
        private void InitializeHwndSource()
        {
            try
            {
                var helper = new WindowInteropHelper(_window);
                _windowHandle = helper.Handle;

                if (_windowHandle != IntPtr.Zero)
                {
                    _source = HwndSource.FromHwnd(_windowHandle);
                    _source?.AddHook(HwndHook);
                    System.Diagnostics.Debug.WriteLine("HotKeyManager: 窗口句柄初始化成功");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("HotKeyManager: 获取窗口句柄失败");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HotKeyManager: 初始化错误 - {ex.Message}");
            }
        }

        /// <summary>
        /// Windows 消息钩子
        /// </summary>
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_hotKeyActions.TryGetValue(id, out var action))
                {
                    // 在 UI 线程上执行操作
                    _window.Dispatcher.BeginInvoke(action);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// 注册快捷键
        /// </summary>
        /// <param name="modifiers">修饰键 (Ctrl, Alt, Shift, Win)</param>
        /// <param name="key">主键</param>
        /// <param name="action">触发时执行的操作</param>
        /// <param name="noRepeat">是否禁止重复触发 (Windows 7+)</param>
        /// <returns>快捷键 ID,失败返回 -1</returns>
        public int RegisterHotKey(ModifierKeys modifiers, Key key, Action action, bool noRepeat = true)
        {
            try
            {
                // 检查键是否有效
                if (key == Key.None)
                {
                    System.Diagnostics.Debug.WriteLine("HotKeyManager: 跳过注册 - 热键为 None");
                    return -1;
                }

                // 确保窗口句柄可用
                if (_windowHandle == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("HotKeyManager: 窗口句柄不可用");
                    return -1;
                }

                // 转换修饰键
                uint mod = 0;
                if (modifiers.HasFlag(ModifierKeys.Alt)) mod |= MOD_ALT;
                if (modifiers.HasFlag(ModifierKeys.Control)) mod |= MOD_CONTROL;
                if (modifiers.HasFlag(ModifierKeys.Shift)) mod |= MOD_SHIFT;
                if (modifiers.HasFlag(ModifierKeys.Windows)) mod |= MOD_WIN;
                if (noRepeat) mod |= MOD_NOREPEAT;

                // 转换虚拟键码
                var vk = KeyInterop.VirtualKeyFromKey(key);

                // 生成唯一 ID
                int id = _currentId++;

                // 注册热键
                if (RegisterHotKey(_windowHandle, id, mod, (uint)vk))
                {
                    _hotKeyActions[id] = action;

                    string keyString = GetKeyString(modifiers, key);
                    System.Diagnostics.Debug.WriteLine($"HotKeyManager: 注册成功 - ID: {id}, Key: {keyString}");

                    return id;
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    System.Diagnostics.Debug.WriteLine($"HotKeyManager: 注册失败 - 错误码: {error}");
                    return -1;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HotKeyManager: 注册异常 - {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// 注销指定 ID 的快捷键
        /// </summary>
        public void UnregisterHotKey(int id)
        {
            try
            {
                if (id < 0) return;

                if (_windowHandle != IntPtr.Zero && _hotKeyActions.ContainsKey(id))
                {
                    UnregisterHotKey(_windowHandle, id);
                    _hotKeyActions.Remove(id);
                    System.Diagnostics.Debug.WriteLine($"HotKeyManager: 注销快捷键 - ID: {id}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HotKeyManager: 注销失败 - {ex.Message}");
            }
        }

        /// <summary>
        /// 注销所有快捷键
        /// </summary>
        public void UnregisterAll()
        {
            try
            {
                if (_windowHandle != IntPtr.Zero)
                {
                    var ids = new List<int>(_hotKeyActions.Keys);
                    foreach (var id in ids)
                    {
                        UnregisterHotKey(_windowHandle, id);
                    }
                    _hotKeyActions.Clear();
                    System.Diagnostics.Debug.WriteLine("HotKeyManager: 已注销所有快捷键");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"HotKeyManager: 注销所有快捷键失败 - {ex.Message}");
            }
        }

        /// <summary>
        /// 获取快捷键的字符串表示
        /// </summary>
        private string GetKeyString(ModifierKeys modifiers, Key key)
        {
            var parts = new List<string>();

            if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

            parts.Add(key.ToString());

            return string.Join("+", parts);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            UnregisterAll();

            if (_source != null)
            {
                _source.RemoveHook(HwndHook);
                _source = null;
            }
        }
    }
}