using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace CH_V03.App.Localization;

/// <summary>Account-scoped display language. Configuration, test data and protocol values are never rewritten.</summary>
public static class UiLanguage
{
    private static readonly Dictionary<string, string> English = LoadEnglish();
    private static readonly (Regex Pattern, Func<Match, string> Translate)[] DynamicEnglish =
    [
        (new Regex(@"^未找到自动登录账号 (.+)，请关闭自动登录后使用账号密码登录。$"),
            m => $"Automatic login account {m.Groups[1].Value} was not found. Disable automatic login and sign in with a username and password."),
        (new Regex(@"^已自动以(.+)身份登录$"),
            m => $"Automatically signed in as {Text(m.Groups[1].Value)}"),
        (new Regex(@"^扫码超时（(\d+) 秒），已尝试发送 DMNG（结果见发送日志），等待下位机新的 DM 信号$"),
            m => $"Scan timed out after {m.Groups[1].Value} seconds. Attempted to send DMNG (see send log); waiting for a new controller DM signal."),
        (new Regex(@"^收到 DM 后等待 (\d+) 秒，未收到有效扫码返回；可能未触发扫码或扫码枪未响应$"),
            m => $"No valid scan arrived within {m.Groups[1].Value} seconds after DM. The scanner may not have been triggered or may not have responded."),
        (new Regex(@"^下位机报文超过 (\d+) 秒未收到 end 或 reset：当前测试数据已作废；等待新的 Item1 表头，缺失时最多请求 3 次$"),
            m => $"No end or reset arrived within {m.Groups[1].Value} seconds. Current test data was discarded; waiting for a new Item1 header, with up to three requests if missing."),
        (new Regex(@"^下位机接收缓存超过 (\d+) 字符，已清空$"),
            m => $"Controller receive buffer exceeded {m.Groups[1].Value} characters and was cleared"),
        (new Regex(@"^连续 NG 已达到 (\d+) 次，报警阈值：(\d+)。请检查治具、下位机、测试线材或产品状态。$"),
            m => $"Consecutive NG reached {m.Groups[1].Value}; alarm threshold is {m.Groups[2].Value}. Check the fixture, controller, test cables and product."),
        (new Regex(@"^无法打开内窥镜摄像头（设备编号 (\d+)）。请确认设备已连接且未被 Supereyes 原厂程序占用。$"),
            m => $"Could not open endoscope camera {m.Groups[1].Value}. Check its connection and close the original Supereyes software."),
        (new Regex(@"^最多同时选择 (\d+) 个测试项。$"),
            m => $"Select no more than {m.Groups[1].Value} test items at once."),
        (new Regex(@"^异常 (\d+) 件$"),
            m => $"Abnormal results: {m.Groups[1].Value}"),
    ];
    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, string>> Originals = new();
    private static readonly ConditionalWeakTable<DependencyObject, HashSet<DependencyProperty>> Observed = new();
    private static bool _applying;

    public static bool IsEnglish { get; private set; }

    public static string HelpHistory { get; } = LoadHelpHistory();

    public static void Register()
    {
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window) ApplyTree(window);
                else Apply(sender as FrameworkElement);
            }), true);
    }

    public static void SetForUser(string? userName)
    {
        var english = string.Equals(userName, "adminEng", StringComparison.OrdinalIgnoreCase);
        if (IsEnglish == english) return;
        IsEnglish = english;
        if (Application.Current is { } application)
            foreach (Window window in application.Windows.Cast<Window>().ToArray())
                ApplyTree(window);
    }

    public static string Text(string? value)
    {
        if (value is null || !IsEnglish) return value ?? string.Empty;
        if (English.TryGetValue(value, out var translated)) return translated;
        foreach (var rule in DynamicEnglish)
        {
            var match = rule.Pattern.Match(value);
            if (match.Success) return rule.Translate(match);
        }
        foreach (var pair in English)
        {
            if (pair.Key.EndsWith('：') && value.StartsWith(pair.Key, StringComparison.Ordinal))
                return pair.Value + Text(value[pair.Key.Length..]);
        }
        // Preserve codes, SNs, file paths and user data; translate only catalogued display text.
        return value;
    }

    public static void ApplyTree(DependencyObject root)
    {
        // Take a snapshot before changing text: WPF can rebuild logical children when Content changes.
        var nodes = new List<DependencyObject>();
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            nodes.Add(node);
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>().ToArray())
                pending.Push(child);
        }

        foreach (var node in nodes)
        {
            if (node is FrameworkElement element) Apply(element);
            if (node is Run run) Translate(run, Run.TextProperty);
        }
    }

    private static void Apply(FrameworkElement? element)
    {
        if (element is null) return;
        if (element is Window) Translate(element, Window.TitleProperty);
        if (element is TextBlock) Observe(element, TextBlock.TextProperty);
        if (element is ContentControl) Observe(element, ContentControl.ContentProperty);
        if (element is HeaderedContentControl) Observe(element, HeaderedContentControl.HeaderProperty);
        if (element is HeaderedItemsControl) Observe(element, HeaderedItemsControl.HeaderProperty);
        Observe(element, FrameworkElement.ToolTipProperty);
        if (element is DataGrid grid)
            foreach (var column in grid.Columns.ToArray()) Translate(column, DataGridColumn.HeaderProperty);
    }

    private static void Observe(DependencyObject element, DependencyProperty property)
    {
        Translate(element, property);
        if (!Observed.GetOrCreateValue(element).Add(property)) return;
        var descriptor = DependencyPropertyDescriptor.FromProperty(property, element.GetType());
        descriptor?.AddValueChanged(element, (_, _) => Translate(element, property));
    }

    private static void Translate(DependencyObject element, DependencyProperty property)
    {
        if (_applying || element.GetValue(property) is not string current) return;
        var originals = Originals.GetOrCreateValue(element);
        if (!originals.TryGetValue(property, out var original) ||
            (current != original && current != English.GetValueOrDefault(original)))
        {
            original = current;
            originals[property] = original;
        }
        var display = Text(original);
        if (current == display) return;
        try
        {
            _applying = true;
            element.SetCurrentValue(property, display);
        }
        finally { _applying = false; }
    }

    private static Dictionary<string, string> LoadEnglish()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CeHao.Localization.en-US.json")
            ?? throw new InvalidOperationException("English language resource is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("English language resource is empty.");
    }

    private static string LoadHelpHistory()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CeHao.Localization.help-history.en.txt")
            ?? throw new InvalidOperationException("English help history is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
