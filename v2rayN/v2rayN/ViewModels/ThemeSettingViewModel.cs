using MaterialDesignColors;
using MaterialDesignColors.ColorManipulation;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace v2rayN.ViewModels;

public partial class ThemeSettingViewModel : MyReactiveObject
{
    private readonly PaletteHelper _paletteHelper = new();

    public BulkObservableCollection<Swatch> Swatches { get; } = [];

    [Reactive]
    public partial Swatch SelectedSwatch { get; set; }

    [Reactive] public partial string CurrentTheme { get; set; }

    [Reactive] public partial int CurrentFontSize { get; set; }

    [Reactive] public partial string CurrentLanguage { get; set; }

    public ThemeSettingViewModel()
    {
        _config = AppManager.Instance.Config;

        RegisterSystemColorSet(_config, ModifyTheme);

        BindingUI();
        RestoreUI();
    }

    private void RestoreUI()
    {
        ModifyTheme();
        ModifyFontSize();
        if (!_config.UiItem.ColorPrimaryName.IsNullOrEmpty())
        {
            var swatch = new SwatchesProvider().Swatches.FirstOrDefault(t => t.Name == _config.UiItem.ColorPrimaryName);
            if (swatch?.ExemplarHue?.Color is not null)
            {
                ChangePrimaryColor(swatch.ExemplarHue.Color);
            }
        }
    }

    private void BindingUI()
    {
        Swatches.AddRange(new SwatchesProvider().Swatches);
        if (!_config.UiItem.ColorPrimaryName.IsNullOrEmpty())
        {
            SelectedSwatch = Swatches.FirstOrDefault(t => t.Name == _config.UiItem.ColorPrimaryName);
        }
        CurrentTheme = _config.UiItem.CurrentTheme;
        CurrentFontSize = _config.UiItem.CurrentFontSize;
        CurrentLanguage = _config.UiItem.CurrentLanguage;

        this.WhenAnyValue(x => x.CurrentTheme)
            .Where(y => y != null && !y.IsNullOrEmpty())
            .SubscribeAsync(async _ =>
             {
                 if (_config.UiItem.CurrentTheme != CurrentTheme)
                 {
                     _config.UiItem.CurrentTheme = CurrentTheme;
                     ModifyTheme();
                     await ConfigHandler.SaveConfig(_config);
                 }
             });

        this.WhenAnyValue(x => x.SelectedSwatch)
             .Where(y => y != null && !y.Name.IsNullOrEmpty())
             .SubscribeAsync(async _ =>
             {
                 if (SelectedSwatch == null
                 || SelectedSwatch.Name.IsNullOrEmpty()
                 || SelectedSwatch.ExemplarHue == null
                 || SelectedSwatch.ExemplarHue?.Color == null)
                 {
                     return;
                 }
                 if (_config.UiItem.ColorPrimaryName != SelectedSwatch?.Name)
                 {
                     _config.UiItem.ColorPrimaryName = SelectedSwatch?.Name;
                     ChangePrimaryColor(SelectedSwatch.ExemplarHue.Color);
                     await ConfigHandler.SaveConfig(_config);
                 }
             });

        this.WhenAnyValue(x => x.CurrentFontSize)
              .Where(y => y > 0)
              .SubscribeAsync(async _ =>
              {
                  if (_config.UiItem.CurrentFontSize != CurrentFontSize)
                  {
                      _config.UiItem.CurrentFontSize = CurrentFontSize;
                      ModifyFontSize();
                      await ConfigHandler.SaveConfig(_config);
                  }
              });

        this.WhenAnyValue(x => x.CurrentLanguage)
            .Where(y => y != null && !y.IsNullOrEmpty())
            .SubscribeAsync(async _ =>
            {
                if (CurrentLanguage.IsNotEmpty() && _config.UiItem.CurrentLanguage != CurrentLanguage)
                {
                    _config.UiItem.CurrentLanguage = CurrentLanguage;
                    Thread.CurrentThread.CurrentUICulture = new(CurrentLanguage);
                    await ConfigHandler.SaveConfig(_config);
                    NoticeManager.Instance.Enqueue(ResUI.NeedRebootTips);
                }
            });
    }

    public void ModifyTheme()
    {
        var baseTheme = CurrentTheme switch
        {
            nameof(ETheme.Dark) => BaseTheme.Dark,
            nameof(ETheme.Light) => BaseTheme.Light,
            _ => BaseTheme.Inherit,
        };

        var theme = _paletteHelper.GetTheme();
        theme.SetBaseTheme(baseTheme);
        _paletteHelper.SetTheme(theme);

        ApplyMonochromePalette();

        WindowsUtils.SetDarkBorder(Application.Current.MainWindow, CurrentTheme);
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Matixray default look: black / gray / white (used while the user has not picked a color of their own)
    /// </summary>
    private void ApplyMonochromePalette()
    {
        if (!_config.UiItem.ColorPrimaryName.IsNullOrEmpty())
        {
            return;
        }

        var theme = _paletteHelper.GetTheme();
        var isDark = CurrentTheme switch
        {
            nameof(ETheme.Dark) => true,
            nameof(ETheme.Light) => false,
            _ => IsSystemDark(),
        };

        var black = System.Windows.Media.Color.FromRgb(0, 0, 0);
        var white = System.Windows.Media.Color.FromRgb(255, 255, 255);

        if (isDark)
        {
            theme.PrimaryLight = new ColorPair(System.Windows.Media.Color.FromRgb(250, 250, 250), black);
            theme.PrimaryMid = new ColorPair(System.Windows.Media.Color.FromRgb(238, 238, 238), black);
            theme.PrimaryDark = new ColorPair(System.Windows.Media.Color.FromRgb(189, 189, 189), black);
            theme.SecondaryLight = new ColorPair(System.Windows.Media.Color.FromRgb(189, 189, 189), black);
            theme.SecondaryMid = new ColorPair(System.Windows.Media.Color.FromRgb(158, 158, 158), black);
            theme.SecondaryDark = new ColorPair(System.Windows.Media.Color.FromRgb(117, 117, 117), white);
        }
        else
        {
            theme.PrimaryLight = new ColorPair(System.Windows.Media.Color.FromRgb(66, 66, 66), white);
            theme.PrimaryMid = new ColorPair(System.Windows.Media.Color.FromRgb(33, 33, 33), white);
            theme.PrimaryDark = new ColorPair(black, white);
            theme.SecondaryLight = new ColorPair(System.Windows.Media.Color.FromRgb(117, 117, 117), white);
            theme.SecondaryMid = new ColorPair(System.Windows.Media.Color.FromRgb(97, 97, 97), white);
            theme.SecondaryDark = new ColorPair(System.Windows.Media.Color.FromRgb(66, 66, 66), white);
        }

        _paletteHelper.SetTheme(theme);
    }

    private void ModifyFontSize()
    {
        double size = CurrentFontSize;
        if (size < Global.MinFontSize)
        {
            return;
        }

        Application.Current.Resources["StdFontSize"] = size;
        Application.Current.Resources["StdFontSize1"] = size + 1;
        Application.Current.Resources["StdFontSize-1"] = size - 1;
    }

    public void ChangePrimaryColor(System.Windows.Media.Color color)
    {
        var theme = _paletteHelper.GetTheme();

        theme.PrimaryLight = new ColorPair(color.Lighten());
        theme.PrimaryMid = new ColorPair(color);
        theme.PrimaryDark = new ColorPair(color.Darken());

        _paletteHelper.SetTheme(theme);
    }

    public static void RegisterSystemColorSet(Config config, Action updateFunc)
    {
        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if ((e.Category == UserPreferenceCategory.Color || e.Category == UserPreferenceCategory.General)
                && config.UiItem.CurrentTheme == nameof(ETheme.FollowSystem))
            {
                updateFunc?.Invoke();
            }
        };
    }
}
