using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
// 注意：不要 using System.Windows.Forms，会与 WPF 的 Button/HorizontalAlignment 等冲突。
// ColorDialog 采用完全限定名调用（项目已引用 WinForms 框架）。

namespace SuperDucker.App;

/// <summary>
/// 主题编辑器：输入名称 + 6 个色块取色（复用系统 ColorDialog，零额外 NuGet 依赖）。
/// 可「新建」或「编辑已有自定义主题」。保存时把结果通过回调交回 MainViewModel 持久化。
/// </summary>
public partial class ThemeEditorDialog : Window
{
    private readonly ThemePreset _working;
    private readonly List<ThemePreset> _existingCustom; // 用于重名校验（不含正在编辑的自身）
    private readonly bool _isEdit;
    private readonly string? _originalName;

    // 6 个色键顺序
    private static readonly (string Key, string Label, Func<ThemePreset, Color> Get, Action<ThemePreset, Color> Set)[] Fields =
    {
        ("BgDark",      "背景-深",     p => p.BgDark,      (p,c) => p.BgDark = c),
        ("BgMedium",    "背景-中",     p => p.BgMedium,    (p,c) => p.BgMedium = c),
        ("BgCard",      "卡片背景",    p => p.BgCard,      (p,c) => p.BgCard = c),
        ("BgCardHover", "卡片-悬停",   p => p.BgCardHover, (p,c) => p.BgCardHover = c),
        ("TextPrimary", "文字-主",     p => p.TextPrimary, (p,c) => p.TextPrimary = c),
        ("TextSecondary","文字-次",    p => p.TextSecondary,(p,c) => p.TextSecondary = c),
    };

    /// <summary>保存成功后的回调，返回最终 ThemePreset（已命名）。</summary>
    public event Action<ThemePreset>? OnSaved;

    public ThemeEditorDialog(ThemePreset basePreset, List<ThemePreset> existingCustom, bool isEdit)
    {
        InitializeComponent();
        _working = basePreset.Clone();
        _existingCustom = existingCustom;
        _isEdit = isEdit;
        _originalName = isEdit ? basePreset.Name : null;

        TxtTitle.Text = isEdit ? "✏️ 编辑主题" : "🎨 新建主题";
        TxtName.Text = _working.Name;
        if (isEdit) TxtName.IsEnabled = false; // 编辑时不允许改名（保持 key 稳定）

        BuildColorRows();
        RefreshPreview();
    }

    private void BuildColorRows()
    {
        ColorRows.Children.Clear();
        foreach (var f in Fields)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var rect = new Rectangle
            {
                Width = 40, Height = 28, RadiusX = 5, RadiusY = 5,
                Stroke = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x66)),
                StrokeThickness = 1,
                Fill = new SolidColorBrush(f.Get(_working)),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(rect, Dock.Left);
            row.Children.Add(rect);

            var label = new TextBlock
            {
                Text = f.Label,
                Foreground = (SolidColorBrush)FindResource("TextPrimaryBrush"),
                FontSize = 13,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);

            var btn = new Button
            {
                Content = "选择颜色",
                Style = (Style)FindResource("FlatButton"),
                Width = 84,
                Height = 28,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 0)
            };
            DockPanel.SetDock(btn, Dock.Right);
            var capturedRect = rect;
            var capturedField = f;
            btn.Click += (_, _) =>
            {
                using var dlg = new System.Windows.Forms.ColorDialog
                {
                    Color = System.Drawing.Color.FromArgb(
                        capturedField.Get(_working).R,
                        capturedField.Get(_working).G,
                        capturedField.Get(_working).B),
                    FullOpen = true
                };
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var c = Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B);
                    capturedField.Set(_working, c);
                    capturedRect.Fill = new SolidColorBrush(c);
                    RefreshPreview();
                }
            };
            row.Children.Add(btn);
            ColorRows.Children.Add(row);
        }
    }

    /// <summary>
    /// 刷新预览卡片。所有画刷都新建后赋值：全局资源里的画刷是 Frozen 的，
    /// 原地改 Color 会抛异常；且那是共享实例，改它会污染整个应用的主题。
    /// </summary>
    private void RefreshPreview()
    {
        var p = _working;
        PreviewCard.Background   = new SolidColorBrush(p.BgCard);
        PreviewCard.BorderBrush  = new SolidColorBrush(p.BgCardHover);
        PreviewTitle.Foreground  = new SolidColorBrush(p.TextPrimary);
        PreviewSubtitle.Foreground = new SolidColorBrush(p.TextSecondary);
        PreviewButton.Background = new SolidColorBrush(p.BgMedium);
        PreviewButton.Foreground = new SolidColorBrush(p.TextPrimary);
        PreviewButton.BorderBrush = new SolidColorBrush(p.BgCardHover);
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            TxtNameError.Visibility = Visibility.Visible;
            return;
        }
        // 重名校验（跳过自身、跳过内建）
        var dup = _existingCustom.Any(p =>
            !_isEdit && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (dup)
        {
            TxtNameError.Text = "已存在同名主题，请换一个名称";
            TxtNameError.Visibility = Visibility.Visible;
            return;
        }

        _working.Name = name;
        OnSaved?.Invoke(_working);
        DialogResult = true;
    }
}
