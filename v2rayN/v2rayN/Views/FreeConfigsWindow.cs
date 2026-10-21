using System.Collections.ObjectModel;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using ServiceLib.Services;

namespace v2rayN.Views;

/// <summary>One line of the free-configs list.</summary>
public sealed class FreeConfigRow : INotifyPropertyChanged
{
    private int _delay;

    public string IndexId { get; init; } = string.Empty;
    public string Remarks { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string Protocol { get; init; } = string.Empty;

    /// <summary>0 = not tested yet, -1 (or less) = failed, more than 0 = milliseconds.</summary>
    public int Delay
    {
        get => _delay;
        set
        {
            if (_delay == value)
            {
                return;
            }
            _delay = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Delay)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DelayText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SortKey)));
        }
    }

    public string DelayText => _delay > 0 ? $"{_delay} ms" : _delay < 0 ? "✕" : "—";

    /// <summary>Working servers first (fastest on top), then not tested, then failed.</summary>
    public int SortKey => _delay > 0 ? _delay : _delay == 0 ? 100000 : 200000;

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Matixray "Free configs" window: gets public configs into a group of the main list, tests them all, sorts them
/// by speed and connects to the chosen (or the best) one. Built in code so it follows the app theme and language.
/// </summary>
public sealed class FreeConfigsWindow : Window
{
    private readonly Config _config = AppManager.Instance.Config;
    private readonly ProfilesViewModel? _profilesVm;
    private readonly bool _fa;

    private readonly ObservableCollection<FreeConfigRow> _rows = [];
    private readonly Dictionary<string, FreeConfigRow> _byId = [];
    private readonly DataGrid _grid = new();
    private readonly TextBlock _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _btnGet;
    private readonly Button _btnTest;
    private readonly Button _btnSort;
    private readonly Button _btnBest;
    private readonly Button _btnUse;
    private readonly Button _btnStop;

    private SpeedtestService? _speedtest;
    private CancellationTokenSource? _cts;
    private string _subId = string.Empty;
    private bool _busy;

    public static string MenuTitle => AppManager.Instance.Config.UiItem.CurrentLanguage == "fa" ? "کانفیگ رایگان" : "Free configs";

    public FreeConfigsWindow(ProfilesViewModel? profilesVm)
    {
        _profilesVm = profilesVm;
        _fa = _config.UiItem.CurrentLanguage == "fa";

        Title = MenuTitle;
        Width = 900;
        Height = 620;
        MinWidth = 700;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = _fa ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        FontSize = 13;
        SetResourceReference(BackgroundProperty, "MaterialDesign.Brush.Background");
        SetResourceReference(ForegroundProperty, "MaterialDesign.Brush.Foreground");

        _btnGet = MakeButton(PackIconKind.CloudDownload, T("دریافت کانفیگ", "Get configs"), OnGetConfig);
        _btnTest = MakeButton(PackIconKind.Speedometer, T("تست همه", "Test all"), OnTestAll);
        _btnSort = MakeButton(PackIconKind.SortAscending, T("مرتب‌سازی", "Sort"), OnSort);
        _btnBest = MakeButton(PackIconKind.LightningBolt, T("اتصال به بهترین", "Connect best"), OnBest);
        _btnUse = MakeButton(PackIconKind.Check, T("اتصال به انتخاب‌شده", "Use selected"), OnUse);
        _btnStop = MakeButton(PackIconKind.Stop, T("توقف", "Stop"), OnStop);
        _btnStop.IsEnabled = false;

        var toolbar = new WrapPanel { Margin = new Thickness(12, 12, 12, 4) };
        foreach (var b in new[] { _btnGet, _btnTest, _btnSort, _btnBest, _btnUse, _btnStop })
        {
            toolbar.Children.Add(b);
        }

        _progress.Height = 3;
        _progress.Margin = new Thickness(12, 6, 12, 0);
        _status.Margin = new Thickness(12, 6, 12, 8);
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Text = T("برای شروع «دریافت کانفیگ» را بزنید.", "Press “Get configs” to start.");

        var statusPanel = new StackPanel();
        statusPanel.Children.Add(_progress);
        statusPanel.Children.Add(_status);

        BuildGrid();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(statusPanel, 1);
        Grid.SetRow(_grid, 2);
        root.Children.Add(toolbar);
        root.Children.Add(statusPanel);
        root.Children.Add(_grid);
        Content = root;

        Loaded += async (_, _) => await OnLoadedAsync();
        Closed += (_, _) =>
        {
            _cts?.Cancel();
            _speedtest?.ExitLoop();
        };
    }

    #region Build

    private string T(string fa, string en) => _fa ? fa : en;

    private Button MakeButton(PackIconKind icon, string text, RoutedEventHandler onClick)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new PackIcon { Kind = icon, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });

        var btn = new Button
        {
            Content = panel,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(14, 4, 14, 4),
            MinHeight = 34,
        };
        if (TryFindResource("MaterialDesignRaisedButton") is Style style)
        {
            btn.Style = style;
        }
        btn.Click += onClick;
        return btn;
    }

    private void BuildGrid()
    {
        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.CanUserAddRows = false;
        _grid.SelectionMode = DataGridSelectionMode.Single;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.Margin = new Thickness(12, 0, 12, 12);
        _grid.ItemsSource = _rows;
        if (TryFindResource("MaterialDesignDataGrid") is Style style)
        {
            _grid.Style = style;
        }

        _grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
        {
            Header = T("نام", "Name"),
            Binding = new Binding(nameof(FreeConfigRow.Remarks)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star),
        });
        _grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
        {
            Header = T("آدرس", "Address"),
            Binding = new Binding(nameof(FreeConfigRow.Address)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star),
        });
        _grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
        {
            Header = T("پروتکل", "Protocol"),
            Binding = new Binding(nameof(FreeConfigRow.Protocol)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
        _grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
        {
            Header = T("تأخیر", "Delay"),
            Binding = new Binding(nameof(FreeConfigRow.DelayText)),
            SortMemberPath = nameof(FreeConfigRow.SortKey),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });

        _grid.MouseDoubleClick += async (_, _) =>
        {
            if (!_busy && _grid.SelectedItem is FreeConfigRow row)
            {
                await UseAsync(row);
            }
        };
    }

    #endregion Build

    #region State

    private void SetStatus(string text)
    {
        _status.Text = text;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _progress.IsIndeterminate = busy;
        foreach (var b in new[] { _btnGet, _btnTest, _btnSort, _btnBest, _btnUse })
        {
            b.IsEnabled = !busy;
        }
        _btnStop.IsEnabled = busy;
    }

    private async Task OnLoadedAsync()
    {
        try
        {
            WindowsUtils.SetDarkBorder(this, _config.UiItem.CurrentTheme);
        }
        catch { }

        try
        {
            _subId = await FreeConfigService.FindGroupAsync();
            await LoadRowsAsync();
            if (_rows.Count > 0)
            {
                SetStatus(T($"{_rows.Count} کانفیگ در فهرست است.", $"{_rows.Count} configs in the list."));
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("FreeConfigsWindow.OnLoaded", ex);
        }
    }

    /// <summary>Reads the group's servers (and the saved test results) and shows the new ones.</summary>
    private async Task LoadRowsAsync()
    {
        if (_subId.Length == 0)
        {
            return;
        }

        var items = await AppManager.Instance.ProfileItems(_subId) ?? [];
        var delays = new Dictionary<string, int>();
        foreach (var ex in await ProfileExManager.Instance.GetProfileExs())
        {
            delays[ex.IndexId] = ex.Delay;
        }

        void Apply()
        {
            foreach (var item in items)
            {
                if (!_byId.TryGetValue(item.IndexId, out var row))
                {
                    row = new FreeConfigRow
                    {
                        IndexId = item.IndexId,
                        Remarks = item.Remarks,
                        Address = $"{item.Address}:{item.Port}",
                        Protocol = item.ConfigType.ToString(),
                    };
                    _byId[item.IndexId] = row;
                    _rows.Add(row);
                }
                if (delays.TryGetValue(item.IndexId, out var delay))
                {
                    row.Delay = delay;
                }
            }
        }

        if (Dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.Invoke(new Action(Apply));
        }
    }

    private void ApplyResult(SpeedTestResult result)
    {
        if (result.IndexId.IsNullOrEmpty())
        {
            if (result.Delay.IsNotEmpty())
            {
                SetStatus(result.Delay);
            }
            return;
        }

        if (_byId.TryGetValue(result.IndexId, out var row) && result.Delay.IsNotEmpty())
        {
            row.Delay = int.TryParse(result.Delay, out var d) ? d : -1;
        }
    }

    private async Task RefreshMainListAsync()
    {
        if (_profilesVm is null)
        {
            return;
        }

        try
        {
            await _profilesVm.RefreshSubscriptions();
            await _profilesVm.RefreshServers();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("FreeConfigsWindow.RefreshMainList", ex);
        }
    }

    #endregion State

    #region Actions

    private async void OnGetConfig(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        _cts = new CancellationTokenSource();
        try
        {
            _subId = await FreeConfigService.EnsureGroupAsync(_config);
            if (_subId.Length == 0)
            {
                SetStatus(T("ساخت گروه کانفیگ رایگان ناموفق بود.", "Could not create the free configs group."));
                return;
            }

            await ConfigHandler.RemoveServersViaSubid(_config, _subId, false);
            _rows.Clear();
            _byId.Clear();
            SetStatus(T("در حال دریافت از منابع…", "Fetching from the sources…"));

            var total = await FreeConfigService.FetchAsync(
                _config,
                _subId,
                async line => await Dispatcher.InvokeAsync(() => SetStatus(line)),
                async () => await LoadRowsAsync(),
                _cts.Token);

            await LoadRowsAsync();
            await RefreshMainListAsync();
            SetStatus(total > 0
                ? T($"پایان دریافت: {total} کانفیگ. حالا «تست همه» را بزنید.", $"Done: {total} configs. Now press “Test all”.")
                : T("کانفیگی دریافت نشد. اینترنت یا فیلترشکن را بررسی کنید و دوباره امتحان کنید.", "No configs were received. Check the connection and try again."));
        }
        catch (OperationCanceledException)
        {
            await LoadRowsAsync();
            await RefreshMainListAsync();
            SetStatus(T("متوقف شد.", "Stopped."));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("FreeConfigsWindow.Get", ex);
            SetStatus(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnTestAll(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        try
        {
            if (_subId.Length == 0)
            {
                _subId = await FreeConfigService.FindGroupAsync();
            }
            var items = _subId.Length == 0 ? null : await AppManager.Instance.ProfileItems(_subId);
            if (items is null || items.Count == 0)
            {
                SetStatus(T("کانفیگی برای تست نیست. اول «دریافت کانفیگ» را بزنید.", "Nothing to test. Press “Get configs” first."));
                return;
            }

            SetBusy(true);
            await LoadRowsAsync();
            SetStatus(T("در حال تست…", "Testing…"));

            _speedtest ??= new SpeedtestService(_config, result =>
            {
                _ = Dispatcher.InvokeAsync(() => ApplyResult(result));
                return Task.CompletedTask;
            });
            await _speedtest.RunLoop(ESpeedActionType.Realping, items);

            await LoadRowsAsync();
            await RefreshMainListAsync();
            var ok = _rows.Count(r => r.Delay > 0);
            SetStatus(T($"پایان تست: {ok} کانفیگ سالم از {_rows.Count}.", $"Test finished: {ok} working out of {_rows.Count}."));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("FreeConfigsWindow.Test", ex);
            SetStatus(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnSort(object sender, RoutedEventArgs e)
    {
        if (_busy || _rows.Count == 0)
        {
            return;
        }

        try
        {
            var ordered = _rows.OrderBy(r => r.SortKey).ToList();
            _rows.Clear();
            foreach (var r in ordered)
            {
                _rows.Add(r);
            }

            if (_subId.Length > 0)
            {
                await ConfigHandler.SortServers(_config, _subId, nameof(EServerColName.DelayVal), true);
                await RefreshMainListAsync();
            }
            SetStatus(T("بر اساس سرعت مرتب شد.", "Sorted by speed."));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("FreeConfigsWindow.Sort", ex);
            SetStatus(ex.Message);
        }
    }

    private async void OnBest(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var best = _rows.Where(r => r.Delay > 0).OrderBy(r => r.Delay).FirstOrDefault();
        if (best is null)
        {
            SetStatus(T("کانفیگ سالمی پیدا نشد. اول «تست همه» را بزنید.", "No working config yet. Press “Test all” first."));
            return;
        }
        await UseAsync(best);
    }

    private async void OnUse(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_grid.SelectedItem is FreeConfigRow row)
        {
            await UseAsync(row);
        }
        else
        {
            SetStatus(T("یک کانفیگ را از فهرست انتخاب کنید.", "Select a config from the list."));
        }
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _speedtest?.ExitLoop();
    }

    private async Task UseAsync(FreeConfigRow row)
    {
        if (_profilesVm is null)
        {
            return;
        }

        try
        {
            await _profilesVm.SetDefaultServer(row.IndexId);
            SetStatus(T($"کانفیگ انتخاب شد: {row.Remarks}", $"Selected: {row.Remarks}"));
        }
        catch (Exception ex)
        {
            Logging.SaveLog("FreeConfigsWindow.Use", ex);
            SetStatus(ex.Message);
        }
    }

    #endregion Actions
}
