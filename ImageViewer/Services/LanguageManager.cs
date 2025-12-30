using System;
using System.Windows;
using ImageViewer.Models;

namespace ImageViewer.Services
{
    /// <summary>
    /// 语言管理器 - 管理应用程序的多语言支持
    /// Language Manager - Manages application internationalization
    /// </summary>
    public class LanguageManager
    {
        private static LanguageManager? _instance;
        private static readonly object _lock = new();

        /// <summary>
        /// 单例实例 / Singleton instance
        /// </summary>
        public static LanguageManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new LanguageManager();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// 语言变更事件 / Language changed event
        /// </summary>
        public event EventHandler? LanguageChanged;

        /// <summary>
        /// 当前语言 / Current language
        /// </summary>
        public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.ChineseSimplified;

        /// <summary>
        /// 设置语言并应用 / Set language and apply
        /// </summary>
        public void SetLanguage(AppLanguage language)
        {
            if (CurrentLanguage == language) return;

            CurrentLanguage = language;
            ApplyLanguage();
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 初始化语言（应用启动时调用）/ Initialize language (call on app startup)
        /// </summary>
        public void Initialize(AppLanguage language)
        {
            CurrentLanguage = language;
            ApplyLanguage();
        }

        /// <summary>
        /// 应用当前语言资源 / Apply current language resources
        /// </summary>
        private void ApplyLanguage()
        {
            var app = Application.Current;
            if (app == null) return;

            // 移除现有语言资源 / Remove existing language resources
            ResourceDictionary? toRemove = null;
            foreach (var dict in app.Resources.MergedDictionaries)
            {
                if (dict.Source != null &&
                    (dict.Source.OriginalString.Contains("Strings.zh-CN") ||
                     dict.Source.OriginalString.Contains("Strings.en-US")))
                {
                    toRemove = dict;
                    break;
                }
            }
            if (toRemove != null)
            {
                app.Resources.MergedDictionaries.Remove(toRemove);
            }

            // 添加新语言资源 / Add new language resources
            var cultureName = CurrentLanguage switch
            {
                AppLanguage.English => "en-US",
                _ => "zh-CN"
            };

            var newDict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Resources/Strings.{cultureName}.xaml")
            };
            app.Resources.MergedDictionaries.Add(newDict);
        }

        /// <summary>
        /// 获取本地化字符串 / Get localized string
        /// </summary>
        /// <param name="key">资源键名 / Resource key</param>
        /// <returns>本地化字符串，找不到则返回键名 / Localized string, or key if not found</returns>
        public static string GetString(string key)
        {
            if (Application.Current?.TryFindResource(key) is string value)
            {
                return value;
            }
            return key; // 回退到键名 / Fallback to key name
        }

        /// <summary>
        /// 获取格式化的本地化字符串 / Get formatted localized string
        /// </summary>
        /// <param name="key">资源键名 / Resource key</param>
        /// <param name="args">格式化参数 / Format arguments</param>
        /// <returns>格式化后的本地化字符串 / Formatted localized string</returns>
        public static string GetString(string key, params object[] args)
        {
            var format = GetString(key);
            try
            {
                return string.Format(format, args);
            }
            catch
            {
                return format;
            }
        }
    }
}