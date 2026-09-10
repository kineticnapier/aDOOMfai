using System;
using JALib.Core;

namespace ADOOMFAIAccelerator;

public sealed class Main : JAMod
{
    public static Main? Instance;

    protected override void OnSetup()
    {
        Instance = this;
        Log("ADOOMFAIAccelerator setup");
    }

    protected override void OnEnable()
    {
        AcceleratorRuntime.Enable(this);
    }

    protected override void OnDisable()
    {
        AcceleratorRuntime.Disable();
    }

    protected override void OnUpdate(float deltaTime)
    {
        AcceleratorRuntime.Tick(deltaTime);
    }

    internal void Info(string message) => Log(message);
    internal void Warn(string message) => Warning(message);
    internal void Fail(Exception e) => LogException(e);
}
