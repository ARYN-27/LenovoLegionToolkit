﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers;

public class PowerPlanController
{
    private static readonly Dictionary<PowerModeState, string> defaultPowerModes = new()
    {
        { PowerModeState.Quiet , "16edbccd-dee9-4ec4-ace5-2f0b5f2a8975"},
        { PowerModeState.Balance , "85d583c5-cf2e-4197-80fd-3789a227a72c"},
        { PowerModeState.Performance , "52521609-efc9-4268-b9ba-67dea73f18b2"},
        { PowerModeState.GodMode , "85d583c5-cf2e-4197-80fd-3789a227a72c"},
    };

    private ApplicationSettings _settings;

    private Vantage _vantage;

    public PowerPlanController(ApplicationSettings settings, Vantage vantage)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _vantage = vantage ?? throw new ArgumentNullException(nameof(vantage));
    }

    public async Task<PowerPlan[]> GetPowerPlansAsync()
    {
        var result = await WMI.ReadAsync("root\\CIMV2\\power",
            $"SELECT * FROM Win32_PowerPlan",
            pdc =>
            {
                var instanceId = (string)pdc["InstanceID"].Value;
                var name = (string)pdc["ElementName"].Value;
                var isActive = (bool)pdc["IsActive"].Value;
                return new PowerPlan(instanceId, name, isActive);
            }).ConfigureAwait(false);
        return result.ToArray();
    }

    public async Task ActivatePowerPlanAsync(PowerModeState powerModeState, bool alwaysActivateDefaults = false)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Activating... [powerModeState={powerModeState}, alwaysActivateDefaults={alwaysActivateDefaults}]");

        var powerPlanId = _settings.Store.PowerPlans.GetValueOrDefault(powerModeState);
        var isDefault = false;

        if (powerPlanId is null)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Power plan for power mode {powerModeState} was not found in settings");

            powerPlanId = GetDefaultPowerPlanId(powerModeState);
            isDefault = true;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Power plan to be activated is {powerPlanId} [isDefault={isDefault}]");

        if (!await ShouldActivateAsync(alwaysActivateDefaults, isDefault).ConfigureAwait(false))
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Power plan {powerPlanId} will not be activated [isDefault={isDefault}]");

            return;
        }

        var powerPlans = await GetPowerPlansAsync().ConfigureAwait(false);

        if (Log.Instance.IsTraceEnabled)
        {
            Log.Instance.Trace($"Available power plans:");
            foreach (var powerPlan in powerPlans)
                Log.Instance.Trace($" - {powerPlan.Name} [guid={powerPlan.Guid}, isActive={powerPlan.IsActive}]");
        }

        var powerPlanToActivate = powerPlans.FirstOrDefault(pp => pp.InstanceId.Contains(powerPlanId));
        if (powerPlanToActivate.Equals(default(PowerPlan)))
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Power plan {powerPlanId} was not found");
            return;
        }

        if (powerPlanToActivate.IsActive)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Power plan {powerPlanToActivate.Guid} is already active. [name={powerPlanToActivate.Name}]");
            return;
        }

        await CMD.RunAsync("powercfg", $"/s {powerPlanToActivate.Guid}").ConfigureAwait(false);

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Power plan {powerPlanToActivate.Guid} activated. [name={powerPlanToActivate.Name}]");
    }

    public PowerModeState[] GetMatchingPowerModes(string powerPlanId)
    {
        var powerModes = new Dictionary<PowerModeState, string>(defaultPowerModes);

        foreach (var kv in _settings.Store.PowerPlans)
        {
            if (string.IsNullOrWhiteSpace(kv.Value))
                continue;

            var value = kv.Value;
            value = value[(value.IndexOf('{') + 1)..];
            value = value[..value.IndexOf('}')];

            powerModes[kv.Key] = value;
        }

        return powerModes.Where(kv => kv.Value == powerPlanId)
            .Select(kv => kv.Key)
            .ToArray();
    }

    private async Task<bool> ShouldActivateAsync(bool alwaysActivateDefaults, bool isDefault)
    {
        var activateWhenVantageEnabled = _settings.Store.ActivatePowerProfilesWithVantageEnabled;
        if (activateWhenVantageEnabled)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Activate power profiles with Vantage is enabled");

            return true;
        }

        if (isDefault && alwaysActivateDefaults)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Power plan is default and always active defaults is set");

            return true;
        }

        var status = await _vantage.GetStatusAsync().ConfigureAwait(false);
        if (status is SoftwareStatus.NotFound or SoftwareStatus.Disabled)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"Vantage is active [status={status}]");

            return true;
        }

        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"Criteria for activation not met [activateWhenVantageEnabled={activateWhenVantageEnabled}, isDefault={isDefault}, alwaysActivateDefaults={alwaysActivateDefaults}, status={status}]");

        return false;
    }

    private string GetDefaultPowerPlanId(PowerModeState state)
    {
        if (defaultPowerModes.TryGetValue(state, out var powerPlanId))
            return powerPlanId;

        throw new InvalidOperationException("Unknown state");
    }
}