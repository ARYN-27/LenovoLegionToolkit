﻿using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Extensions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class StatusWindow
{
    private readonly struct StatusWindowData
    {
        public PowerModeState? PowerModeState { get; init; }
        public string? GodModePresetName { get; init; }
        public GPUStatus? GPUStatus { get; init; }
        public BatteryInformation? BatteryInformation { get; init; }
        public BatteryState? BatteryState { get; init; }
        public bool HasUpdate { get; init; }
    }

    public static async Task<StatusWindow> CreateAsync() => new(await GetStatusWindowDataAsync());

    private static async Task<StatusWindowData> GetStatusWindowDataAsync()
    {
        var powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
        var godModeController = IoCContainer.Resolve<GodModeController>();
        var gpuController = IoCContainer.Resolve<GPUController>();
        var batteryFeature = IoCContainer.Resolve<BatteryFeature>();
        var updateChecker = IoCContainer.Resolve<UpdateChecker>();

        PowerModeState? state = null;
        string? godModePresetName = null;
        GPUStatus? gpuStatus = null;
        BatteryInformation? batteryInformation = null;
        BatteryState? batteryState = null;
        var hasUpdate = false;

        try
        {
            if (await powerModeFeature.IsSupportedAsync().ConfigureAwait(false))
            {
                state = await powerModeFeature.GetStateAsync().ConfigureAwait(false);

                if (state == PowerModeState.GodMode)
                    godModePresetName = await godModeController.GetActivePresetNameAsync().ConfigureAwait(false);
            }
        }
        catch { /* Ignored */ }

        try
        {
            if (gpuController.IsSupported())
                gpuStatus = await gpuController.RefreshNowAsync().ConfigureAwait(false);

        }
        catch { /* Ignored */ }

        try
        {
            batteryInformation = Battery.GetBatteryInformation();
        }
        catch { /* Ignored */ }

        try
        {
            if (await batteryFeature.IsSupportedAsync().ConfigureAwait(false))
                batteryState = await batteryFeature.GetStateAsync().ConfigureAwait(false);

        }
        catch { /* Ignored */ }

        try
        {
            hasUpdate = await updateChecker.CheckAsync().ConfigureAwait(false) is not null;
        }
        catch { /* Ignored */ }

        return new()
        {
            PowerModeState = state,
            GodModePresetName = godModePresetName,
            GPUStatus = gpuStatus,
            BatteryInformation = batteryInformation,
            BatteryState = batteryState,
            HasUpdate = hasUpdate
        };
    }

    private StatusWindow(StatusWindowData data)
    {
        InitializeComponent();

        Loaded += StatusWindow_Loaded;

        WindowStyle = WindowStyle.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowBackdropType = BackgroundType.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;

        Focusable = false;
        Topmost = true;
        ExtendsContentIntoTitleBar = true;
        ShowInTaskbar = false;
        ShowActivated = false;

#if DEBUG
        _title.Text += " [DEBUG]";
#else
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        if (version == new Version(0, 0, 1, 0) || version?.Build == 99)
            _title.Text += " [BETA]";
#endif

        if (Log.Instance.IsTraceEnabled)
            _title.Text += " [LOGGING ENABLED]";

        RefreshPowerMode(data.PowerModeState, data.GodModePresetName);
        RefreshDiscreteGpu(data.GPUStatus);
        RefreshBattery(data.BatteryInformation, data.BatteryState);
        RefreshUpdate(data.HasUpdate);
    }

    private void StatusWindow_Loaded(object sender, RoutedEventArgs e) => MoveBottomRightEdgeOfWindowToMousePosition();

    private void MoveBottomRightEdgeOfWindowToMousePosition()
    {
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        if (!transform.HasValue)
        {
            Left = 0;
            Top = 0;
            return;
        }

        var mousePosition = Control.MousePosition;
        var point = new Point(mousePosition.X, mousePosition.Y);
        var mouse = transform.Value.Transform(point);

        Left = mouse.X - ActualWidth;
        Top = mouse.Y - ActualHeight - 16;
    }

    private void RefreshPowerMode(PowerModeState? powerModeState, string? godModePresetName)
    {
        _powerModeValueLabel.Content = powerModeState?.GetDisplayName() ?? "-";
        _powerModeValueIndicator.Fill = powerModeState?.GetSolidColorBrush() ?? new(Colors.Transparent);

        if (powerModeState == PowerModeState.GodMode)
        {
            _powerModePresetValueLabel.Content = godModePresetName ?? "-";

            _powerModePresetLabel.Visibility = Visibility.Visible;
            _powerModePresetValueLabel.Visibility = Visibility.Visible;
        }
        else
        {
            _powerModePresetLabel.Visibility = Visibility.Collapsed;
            _powerModePresetValueLabel.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshDiscreteGpu(GPUStatus? status)
    {
        if (!status.HasValue)
        {
            _gpuGrid.Visibility = Visibility.Collapsed;
            return;
        }

        if (status.Value.State is GPUState.Active or GPUState.MonitorConnected)
        {
            _gpuPowerStateValueLabel.Content = status.Value.PerformanceState ?? "-";

            _gpuActive.Visibility = Visibility.Visible;
            _gpuInactive.Visibility = Visibility.Collapsed;
            _gpuPoweredOff.Visibility = Visibility.Collapsed;
            _gpuPowerStateValue.Visibility = Visibility.Visible;
            _gpuPowerStateValueLabel.Visibility = Visibility.Visible;
        }
        else if (status.Value.State is GPUState.PoweredOff)
        {
            _gpuPowerStateValueLabel.Content = null;

            _gpuActive.Visibility = Visibility.Collapsed;
            _gpuInactive.Visibility = Visibility.Collapsed;
            _gpuPoweredOff.Visibility = Visibility.Visible;
            _gpuPowerStateValue.Visibility = Visibility.Collapsed;
            _gpuPowerStateValueLabel.Visibility = Visibility.Collapsed;
        }
        else
        {
            _gpuPowerStateValueLabel.Content = status.Value.PerformanceState ?? "-";

            _gpuActive.Visibility = Visibility.Collapsed;
            _gpuInactive.Visibility = Visibility.Visible;
            _gpuPoweredOff.Visibility = Visibility.Collapsed;
            _gpuPowerStateValue.Visibility = Visibility.Visible;
            _gpuPowerStateValueLabel.Visibility = Visibility.Visible;
        }

        _gpuGrid.Visibility = Visibility.Visible;
    }

    private void RefreshBattery(BatteryInformation? batteryInformation, BatteryState? batteryState)
    {
        if (!batteryInformation.HasValue || !batteryState.HasValue)
        {
            _batteryIcon.Symbol = SymbolRegular.Battery024;
            _batteryValueLabel.Content = "-";
            _batteryModeValueLabel.Content = "-";
            _batteryDischargeValueLabel.Content = "-";
            return;
        }

        var symbol = (int)Math.Round(batteryInformation.Value.BatteryPercentage / 10.0) switch
        {
            10 => SymbolRegular.Battery1024,
            9 => SymbolRegular.Battery924,
            8 => SymbolRegular.Battery824,
            7 => SymbolRegular.Battery724,
            6 => SymbolRegular.Battery624,
            5 => SymbolRegular.Battery524,
            4 => SymbolRegular.Battery424,
            3 => SymbolRegular.Battery324,
            2 => SymbolRegular.Battery224,
            1 => SymbolRegular.Battery124,
            _ => SymbolRegular.Battery024,
        };

        if (batteryInformation.Value.IsCharging)
            symbol = batteryState == BatteryState.Conservation ? SymbolRegular.BatterySaver24 : SymbolRegular.BatteryCharge24;

        if (batteryInformation.Value.IsLowBattery)
            _batteryValueLabel.SetResourceReference(ForegroundProperty, "SystemFillColorCautionBrush");

        _batteryIcon.Symbol = symbol;
        _batteryValueLabel.Content = $"{batteryInformation.Value.BatteryPercentage}%";
        _batteryModeValueLabel.Content = batteryState.GetDisplayName();
        _batteryDischargeValueLabel.Content = $"{batteryInformation.Value.DischargeRate / 1000.0:+0.00;-0.00;0.00} W";
    }

    private void RefreshUpdate(bool hasUpdate) => _updateIndicator.Visibility = hasUpdate ? Visibility.Visible : Visibility.Collapsed;
}