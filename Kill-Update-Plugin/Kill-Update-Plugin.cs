﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using TaskbarIconHost;
using ZombifyMe;

namespace KillUpdate
{
    public class KillUpdatePlugin : TaskbarIconHost.IPluginClient
    {
        #region Plugin
        public string Name
        {
            get { return PluginDetails.Name; }
        }

        public Guid Guid
        {
            get { return PluginDetails.Guid; }
        }

        public bool RequireElevated
        {
            get { return true; }
        }

        public bool HasClickHandler
        {
            get { return false; }
        }

        public void Initialize(bool isElevated, Dispatcher dispatcher, TaskbarIconHost.IPluginSettings settings, TaskbarIconHost.IPluginLogger logger)
        {
            IsElevated = isElevated;
            Dispatcher = dispatcher;
            Settings = settings;
            Logger = logger;

            InitServiceManager();

            InitializeCommand("Locked",
                              isVisibleHandler: () => true,
                              isEnabledHandler: () => StartType.HasValue && IsElevated,
                              isCheckedHandler: () => IsLockEnabled,
                              commandHandler: OnCommandLock);

            InitZombification();
        }

        private void InitializeCommand(string header, Func<bool> isVisibleHandler, Func<bool> isEnabledHandler, Func<bool> isCheckedHandler, Action commandHandler)
        {
            ICommand Command = new RoutedUICommand();
            CommandList.Add(Command);
            MenuHeaderTable.Add(Command, header);
            MenuIsVisibleTable.Add(Command, isVisibleHandler);
            MenuIsEnabledTable.Add(Command, isEnabledHandler);
            MenuIsCheckedTable.Add(Command, isCheckedHandler);
            MenuHandlerTable.Add(Command, commandHandler);
        }

        public List<ICommand> CommandList { get; private set; } = new List<ICommand>();

        public bool GetIsMenuChanged(bool beforeMenuOpening)
        {
            bool Result = IsMenuChanged;
            IsMenuChanged = false;

            return Result;
        }

        public string GetMenuHeader(ICommand command)
        {
            return MenuHeaderTable[command];
        }

        public bool GetMenuIsVisible(ICommand command)
        {
            return MenuIsVisibleTable[command]();
        }

        public bool GetMenuIsEnabled(ICommand command)
        {
            return MenuIsEnabledTable[command]();
        }

        public bool GetMenuIsChecked(ICommand command)
        {
            return MenuIsCheckedTable[command]();
        }

        public Bitmap GetMenuIcon(ICommand command)
        {
            return null;
        }

        public void OnMenuOpening()
        {
        }

        public void OnExecuteCommand(ICommand command)
        {
            MenuHandlerTable[command]();
        }

        private void OnCommandLock()
        {
            bool LockIt = !IsLockEnabled;

            Settings.SetSettingBool(LockedSettingName, LockIt);
            ChangeLockMode(LockIt);
            IsMenuChanged = true;
        }

        public bool GetIsIconChanged()
        {
            bool Result = IsIconChanged;
            IsIconChanged = false;

            return Result;
        }

        public Icon Icon
        {
            get
            {
                if (IsElevated)
                    if (IsLockEnabled)
                        return LoadEmbeddedResource<Icon>("Locked-Enabled.ico");
                    else
                        return LoadEmbeddedResource<Icon>("Unlocked-Enabled.ico");
                else
                    if (IsLockEnabled)
                        return LoadEmbeddedResource<Icon>("Locked-Disabled.ico");
                    else
                        return LoadEmbeddedResource<Icon>("Unlocked-Disabled.ico");
            }
        }

        public Bitmap SelectionBitmap
        {
            get { return LoadEmbeddedResource<Bitmap>("Kill-Update.png"); }
        }

        public void OnIconClicked()
        {
        }

        public bool GetIsToolTipChanged()
        {
            return false;
        }

        public string ToolTip
        {
            get
            {
                if (IsElevated)
                    return "Lock/Unlock Windows updates";
                else
                    return "Lock/Unlock Windows updates (Requires administrator mode)";
            }
        }

        public void OnActivated()
        {
        }

        public void OnDeactivated()
        {
        }

        public bool CanClose(bool canClose)
        {
            return true;
        }

        public void BeginClose()
        {
            ExitZombification();
            StopServiceManager();
        }

        public bool IsClosed
        {
            get { return true; }
        }

        public bool IsElevated { get; private set; }
        public Dispatcher Dispatcher { get; private set; }
        public TaskbarIconHost.IPluginSettings Settings { get; private set; }
        public TaskbarIconHost.IPluginLogger Logger { get; private set; }

        private T LoadEmbeddedResource<T>(string resourceName)
        {
            // Loads an "Embedded Resource" of type T (ex: Bitmap for a PNG file).
            foreach (string ResourceName in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                if (ResourceName.EndsWith(resourceName))
                {
                    using (Stream rs = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                    {
                        T Result = (T)Activator.CreateInstance(typeof(T), rs);
                        Logger.AddLog($"Resource {resourceName} loaded");

                        return Result;
                    }
                }

            Logger.AddLog($"Resource {resourceName} not found");
            return default(T);
        }

        private Dictionary<ICommand, string> MenuHeaderTable = new Dictionary<ICommand, string>();
        private Dictionary<ICommand, Func<bool>> MenuIsVisibleTable = new Dictionary<ICommand, Func<bool>>();
        private Dictionary<ICommand, Func<bool>> MenuIsEnabledTable = new Dictionary<ICommand, Func<bool>>();
        private Dictionary<ICommand, Func<bool>> MenuIsCheckedTable = new Dictionary<ICommand, Func<bool>>();
        private Dictionary<ICommand, Action> MenuHandlerTable = new Dictionary<ICommand, Action>();
        private bool IsIconChanged;
        private bool IsMenuChanged;
        #endregion

        #region Service Manager
        private void InitServiceManager()
        {
            Logger.AddLog("InitServiceManager starting");

            if (Settings.IsBoolKeySet(LockedSettingName))
                if (IsSettingLock)
                    StartType = ServiceStartMode.Disabled;
                else
                    StartType = ServiceStartMode.Manual;

            UpdateTimer = new Timer(new TimerCallback(UpdateTimerCallback));
            FullRestartTimer = new Timer(new TimerCallback(FullRestartTimerCallback));
            UpdateWatch = new Stopwatch();
            UpdateWatch.Start();

            OnUpdate();

            UpdateTimer.Change(CheckInterval, CheckInterval);
            FullRestartTimer.Change(FullRestartInterval, Timeout.InfiniteTimeSpan);

            Logger.AddLog("InitServiceManager done");
        }

        private bool IsLockEnabled { get { return (StartType.HasValue && StartType == ServiceStartMode.Disabled); } }
        private bool IsSettingLock { get { return Settings.GetSettingBool(LockedSettingName, true); } }

        private void StopServiceManager()
        {
            FullRestartTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            FullRestartTimer = null;
            UpdateTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            UpdateTimer = null;
        }

        private void UpdateTimerCallback(object parameter)
        {
            // Protection against reentering too many times after a sleep/wake up.
            // There must be at most two pending calls to OnUpdate in the dispatcher.
            int NewTimerDispatcherCount = Interlocked.Increment(ref TimerDispatcherCount);
            if (NewTimerDispatcherCount > 2)
            {
                Interlocked.Decrement(ref TimerDispatcherCount);
                return;
            }

            // For debug purpose.
            LastTotalElapsed = Math.Round(UpdateWatch.Elapsed.TotalSeconds, 0);

            Dispatcher.BeginInvoke(new OnUpdateHandler(OnUpdate));
        }

        private void FullRestartTimerCallback(object parameter)
        {
            if (UpdateTimer != null)
            {
                Logger.AddLog("Restarting the timer");

                // Restart the update timer from scratch.
                UpdateTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

                UpdateTimer = new Timer(new TimerCallback(UpdateTimerCallback));
                UpdateTimer.Change(CheckInterval, CheckInterval);

                Logger.AddLog("Timer restarted");
            }
            else
                Logger.AddLog("No timer to restart");

            FullRestartTimer.Change(FullRestartInterval, Timeout.InfiniteTimeSpan);
            Logger.AddLog($"Next check scheduled at {DateTime.UtcNow + FullRestartInterval}");
        }

        private int TimerDispatcherCount = 1;
        private double LastTotalElapsed = double.NaN;

        private delegate void OnUpdateHandler();
        private void OnUpdate()
        {
            try
            {
                Logger.AddLog("%% Running timer callback");

                int LastTimerDispatcherCount = Interlocked.Decrement(ref TimerDispatcherCount);
                UpdateWatch.Restart();

                Logger.AddLog($"Watch restarted, Elapsed = {LastTotalElapsed}, pending count = {LastTimerDispatcherCount}");

                Settings.RenewKey();

                Logger.AddLog("Key renewed");

                ServiceStartMode? PreviousStartType = StartType;
                bool LockIt = IsSettingLock;

                Logger.AddLog("Lock setting read");

                ServiceController[] Services = ServiceController.GetServices();
                if (Services == null)
                    Logger.AddLog("Failed to get services");
                else
                {
                    Logger.AddLog($"Found {Services.Length} service(s)");

                    foreach (ServiceController Service in Services)
                        if (Service.ServiceName == WindowsUpdateServiceName)
                        {
                            Logger.AddLog($"Checking {Service.ServiceName}");

                            StartType = Service.StartType;

                            Logger.AddLog($"Current start type: {StartType}");

                            if (PreviousStartType.HasValue && PreviousStartType.Value != StartType.Value)
                            {
                                Logger.AddLog("Start type changed");

                                ChangeLockMode(Service, LockIt);
                            }

                            StopIfRunning(Service, LockIt);

                            PreviousStartType = StartType;
                            break;
                        }
                }

                Zombification?.SetAlive();

                Logger.AddLog("%% Timer callback completed");
            }
            catch (Exception e)
            {
                Logger.AddLog($"(from OnUpdate) {e.Message}");
            }
        }

        private void ChangeLockMode(bool lockIt)
        {
            Logger.AddLog("ChangeLockMode starting");

            try
            {
                ServiceController Service = new ServiceController(WindowsUpdateServiceName);

                if (IsElevated)
                    ChangeLockMode(Service, lockIt);
                else
                    Logger.AddLog("Not elevated, cannot change");
            }
            catch (Exception e)
            {
                Logger.AddLog($"(from ChangeLockMode) {e.Message}");
            }
        }

        private void ChangeLockMode(ServiceController Service, bool lockIt)
        {
            IsIconChanged = true;
            ServiceStartMode NewStartType = lockIt ? ServiceStartMode.Disabled : ServiceStartMode.Manual;
            ServiceHelper.ChangeStartMode(Service, NewStartType);

            StartType = NewStartType;
            Logger.AddLog($"Service type={StartType}");
        }

        private void StopIfRunning(ServiceController Service, bool lockIt)
        {
            if (lockIt && Service.Status == ServiceControllerStatus.Running && Service.CanStop)
            {
                Logger.AddLog("Stopping service");
                Service.Stop();
                Logger.AddLog("Service stopped");
            }
        }

        private static readonly string WindowsUpdateServiceName = "wuauserv";
        private static readonly string LockedSettingName = "Locked";
        private readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);
        private readonly TimeSpan FullRestartInterval = TimeSpan.FromHours(1);
        private ServiceStartMode? StartType;
        private Timer UpdateTimer;
        private Timer FullRestartTimer;
        private Stopwatch UpdateWatch;
        #endregion

        #region Zombification
        private static bool IsRestart { get { return Zombification.IsRestart; } }

        private void InitZombification()
        {
            Logger.AddLog("InitZombification starting");

            if (IsRestart)
                Logger.AddLog("This process has been restarted");

            Zombification = new Zombification("Kill-Update");
            Zombification.Delay = TimeSpan.FromMinutes(1);
            Zombification.WatchingMessage = null;
            Zombification.RestartMessage = null;
            Zombification.Flags = Flags.NoWindow | Flags.ForwardArguments;
            Zombification.IsSymetric = true;
            Zombification.AliveTimeout = TimeSpan.FromMinutes(1);
            Zombification.ZombifyMe();

            Logger.AddLog("InitZombification done");
        }

        private void ExitZombification()
        {
            Logger.AddLog("ExitZombification starting");

            if (Zombification != null)
            {
                Zombification.Cancel();
                Zombification = null;
            }

            Logger.AddLog("ExitZombification done");
        }

        private Zombification Zombification;
        #endregion
    }
}
