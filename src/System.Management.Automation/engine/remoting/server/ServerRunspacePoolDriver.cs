/********************************************************************++
 * Copyright (c) Microsoft Corporation.  All rights reserved.
 * --********************************************************************/

using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Management.Automation.Runspaces;
using System.Management.Automation.Host;
using System.Management.Automation.Internal;
using System.Management.Automation.Remoting;
using System.Management.Automation.Remoting.Server;
using Dbg = System.Management.Automation.Diagnostics;
using System.Threading;
using Microsoft.PowerShell;
using System.Management.Automation.Security;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Security.Principal;

namespace System.Management.Automation
{
    /// <summary>
    /// Interface exposing driver single thread invoke enter/exit 
    /// nested pipeline.
    /// </summary>
    internal interface IRSPDriverInvoke
    {
        void EnterNestedPipeline();
        void ExitNestedPipeline();
        bool HandleStopSignal();
    }

    /// <summary>
    /// This class wraps a RunspacePoolInternal object. It is used to function
    /// as a server side runspacepool
    /// </summary>
    internal class ServerRunspacePoolDriver : IRSPDriverInvoke
    {
        #region Private Members

        // local runspace pool at the server
        private RunspacePool localRunspacePool;

        // Script to run after a RunspacePool/Runspace is created in this session.
        private ConfigurationDataFromXML configData;

        // application private data to send back to the client in when we get into "opened" state
        private PSPrimitiveDictionary applicationPrivateData;

        // the client runspacepool's guid that is
        // associated with this runspace pool driver
        private Guid clientRunspacePoolId;

        // data structure handler object to handle all communications
        // with the client
        private ServerRunspacePoolDataStructureHandler dsHandler;

        // powershell's associated with this runspace pool
        private Dictionary<Guid, ServerPowerShellDriver> associatedShells
            = new Dictionary<Guid, ServerPowerShellDriver>();

        // remote host associated with this runspacepool
        private ServerDriverRemoteHost remoteHost;

        private bool isClosed;

        // server capability reported to the client during negotiation (not the actual capability)
        private RemoteSessionCapability serverCapability;
        private Runspace rsToUseForSteppablePipeline;

        // steppable pipeline event subscribers exist per-session
        private ServerSteppablePipelineSubscriber eventSubscriber = new ServerSteppablePipelineSubscriber();
        private PSDataCollection<object> inputCollection; // PowerShell driver input collection

        // Object to invoke nested PowerShell drivers on single pipeline worker thread.
        private PowerShellDriverInvoker driverNestedInvoker;

        // Remote wrapper for script debugger.
        private ServerRemoteDebugger serverRemoteDebugger;

        // Version of PowerShell client.
        private Version clientPSVersion;

        // Optional endpoint configuration name.
        // Used in OutOfProc scenarios that do not support PSSession endpoint configuration.
        // Results in a configured remote runspace pushed onto driver host.
        private string configurationName;

        /// <summary>
        /// Event that get raised when the RunspacePool is closed.
        /// </summary>
        internal EventHandler<EventArgs> Closed;

        #endregion Private Members

        #region Constructors
     

#if CORECLR // No ApartmentState In CoreCLR
        /// <summary>
        /// Creates the runspace pool driver
        /// </summary>
        /// <param name="clientRunspacePoolId">client runspace pool id to associate</param>
        /// <param name="transportManager">transport manager associated with this
        /// runspace pool driver</param>
        /// <param name="maxRunspaces">maximum runspaces to open</param>
        /// <param name="minRunspaces">minimum runspaces to open</param>
        /// <param name="threadOptions">threading options for the runspaces in the pool</param>        
        /// <param name="hostInfo">host information about client side host</param>
        /// <param name="configData">
        /// Contains:
        /// 1. Script to run after a RunspacePool/Runspace is created in this session.
        /// For RunspacePool case, every newly created Runspace (in the pool) will run
        /// this script.
        /// 2. ThreadOptions for RunspacePool/Runspace
        /// 3. ThreadApartment for RunspacePool/Runspace
        /// </param>
        /// <param name="initialSessionState">configuration of the runspace</param>
        /// <param name="applicationPrivateData">application private data</param>
        /// <param name="isAdministrator">True if the driver is being created by an administrator</param>
        /// <param name="serverCapability">server capability reported to the client during negotiation (not the actual capability)</param>
        /// <param name="psClientVersion">Client PowerShell version.</param>
        /// <param name="configurationName">Optional endpoint configuration name to create a pushed configured runspace.</param>
        internal ServerRunspacePoolDriver(
            Guid clientRunspacePoolId, 
            int minRunspaces,
            int maxRunspaces, 
            PSThreadOptions threadOptions,
            HostInfo hostInfo, 
            InitialSessionState initialSessionState,
            PSPrimitiveDictionary applicationPrivateData,
            ConfigurationDataFromXML configData,
            AbstractServerSessionTransportManager transportManager, 
            bool isAdministrator,
            RemoteSessionCapability serverCapability,
            Version psClientVersion,
            string configurationName)
#else
        /// <summary>
        /// Creates the runspace pool driver
        /// </summary>
        /// <param name="clientRunspacePoolId">client runspace pool id to associate</param>
        /// <param name="transportManager">transport manager associated with this
        /// runspace pool driver</param>
        /// <param name="maxRunspaces">maximum runspaces to open</param>
        /// <param name="minRunspaces">minimum runspaces to open</param>
        /// <param name="threadOptions">threading options for the runspaces in the pool</param>
        /// <param name="apartmentState">apartment state for the runspaces in the pool</param>
        /// <param name="hostInfo">host information about client side host</param>
        /// <param name="configData">
        /// Contains:
        /// 1. Script to run after a RunspacePool/Runspace is created in this session.
        /// For RunspacePool case, every newly created Runspace (in the pool) will run
        /// this script.
        /// 2. ThreadOptions for RunspacePool/Runspace
        /// 3. ThreadApartment for RunspacePool/Runspace
        /// </param>
        /// <param name="initialSessionState">configuration of the runspace</param>
        /// <param name="applicationPrivateData">application private data</param>
        /// <param name="isAdministrator">True if the driver is being created by an administrator</param>
        /// <param name="serverCapability">server capability reported to the client during negotiation (not the actual capability)</param>
        /// <param name="psClientVersion">Client PowerShell version.</param>
        /// <param name="configurationName">Optional endpoint configuration name to create a pushed configured runspace.</param>
        internal ServerRunspacePoolDriver(
            Guid clientRunspacePoolId, 
            int minRunspaces,
            int maxRunspaces, 
            PSThreadOptions threadOptions, 
            ApartmentState apartmentState, 
            HostInfo hostInfo, 
            InitialSessionState initialSessionState,
            PSPrimitiveDictionary applicationPrivateData,
            ConfigurationDataFromXML configData,
            AbstractServerSessionTransportManager transportManager, 
            bool isAdministrator,
            RemoteSessionCapability serverCapability,
            Version psClientVersion,
            string configurationName)
#endif
        {
            Dbg.Assert(null != configData, "ConfigurationData cannot be null");

            this.serverCapability = serverCapability;
            this.clientPSVersion = psClientVersion;

            this.configurationName = configurationName;

            // Create a new server host and associate for host call
            // integration
            this.remoteHost = new ServerDriverRemoteHost(clientRunspacePoolId,
                Guid.Empty, hostInfo, transportManager, null);

            this.configData = configData;
            this.applicationPrivateData = applicationPrivateData;
            localRunspacePool = RunspaceFactory.CreateRunspacePool(
                  minRunspaces, maxRunspaces, initialSessionState, this.remoteHost);

            // Set ThreadOptions for this RunspacePool
            // The default server settings is to make new commands execute in the calling thread...this saves
            // thread switching time and thread pool pressure on the service.
            // Users can override the server settings only if they are administrators
            PSThreadOptions serverThreadOptions = configData.ShellThreadOptions.HasValue ? configData.ShellThreadOptions.Value : PSThreadOptions.UseCurrentThread;
            if (threadOptions == PSThreadOptions.Default || threadOptions == serverThreadOptions)
            {
                localRunspacePool.ThreadOptions = serverThreadOptions;
            }
            else
            {
                if (!isAdministrator)
                {
                    throw new InvalidOperationException(PSRemotingErrorInvariants.FormatResourceString(RemotingErrorIdStrings.MustBeAdminToOverrideThreadOptions));
                }

                localRunspacePool.ThreadOptions = threadOptions;
            }

#if !CORECLR // No ApartmentState In CoreCLR
            // Set Thread ApartmentState for this RunspacePool
            ApartmentState serverApartmentState = configData.ShellThreadApartmentState.HasValue ? configData.ShellThreadApartmentState.Value : Runspace.DefaultApartmentState;

            if (apartmentState == ApartmentState.Unknown || apartmentState == serverApartmentState)
            {
                localRunspacePool.ApartmentState = serverApartmentState;
            }
            else
            {
                localRunspacePool.ApartmentState = apartmentState;
            }
#endif

            // If we have a runspace pool with a single runspace then we can run nested pipelines on
            // on it in a single pipeline invoke thread.
            if (maxRunspaces == 1 &&
                (localRunspacePool.ThreadOptions == PSThreadOptions.Default ||
                 localRunspacePool.ThreadOptions == PSThreadOptions.UseCurrentThread))
            {
                driverNestedInvoker = new PowerShellDriverInvoker();
            }

            this.clientRunspacePoolId = clientRunspacePoolId;
            this.dsHandler = new ServerRunspacePoolDataStructureHandler(this, transportManager);

            // handle the StateChanged event of the runspace pool
            localRunspacePool.StateChanged +=
                new EventHandler<RunspacePoolStateChangedEventArgs>(HandleRunspacePoolStateChanged);

            // listen for events on the runspace pool
            localRunspacePool.ForwardEvent +=
                new EventHandler<PSEventArgs>(HandleRunspacePoolForwardEvent);

            localRunspacePool.RunspaceCreated += HandleRunspaceCreated;

            // register for all the events from the data structure handler
            dsHandler.CreateAndInvokePowerShell +=
                new EventHandler<RemoteDataEventArgs<RemoteDataObject<PSObject>>>(HandleCreateAndInvokePowerShell);
            dsHandler.GetCommandMetadata +=
                new EventHandler<RemoteDataEventArgs<RemoteDataObject<PSObject>>>(HandleGetCommandMetadata);
            dsHandler.HostResponseReceived +=
                new EventHandler<RemoteDataEventArgs<RemoteHostResponse>>(HandleHostResponseReceived);
            dsHandler.SetMaxRunspacesReceived +=
                new EventHandler<RemoteDataEventArgs<PSObject>>(HandleSetMaxRunspacesReceived);
            dsHandler.SetMinRunspacesReceived +=
                new EventHandler<RemoteDataEventArgs<PSObject>>(HandleSetMinRunspacesReceived);
            dsHandler.GetAvailableRunspacesReceived +=
                new EventHandler<RemoteDataEventArgs<PSObject>>(HandleGetAvailalbeRunspacesReceived);
            dsHandler.ResetRunspaceState +=
                new EventHandler<RemoteDataEventArgs<PSObject>>(HandleResetRunspaceState);
        }

        #endregion Constructors

        #region Internal Methods

        /// <summary>
        /// data structure handler for communicating with client
        /// </summary>
        internal ServerRunspacePoolDataStructureHandler DataStructureHandler
        {
            get
            {
                return dsHandler;
            }
        }

        /// <summary>
        /// The server host associated with the runspace pool.
        /// </summary>
        internal ServerRemoteHost ServerRemoteHost
        {
            get { return remoteHost; }
        }

        /// <summary>
        /// the client runspacepool id
        /// </summary>
        internal Guid InstanceId
        {
            get
            {
                return clientRunspacePoolId;
            }
        }

        /// <summary>
        /// The local runspace pool associated with 
        /// this driver
        /// </summary>
        internal RunspacePool RunspacePool
        {
            get
            {
                return localRunspacePool;
            }
        }

        /// <summary>
        /// Start the RunspacePoolDriver. This will open the 
        /// underlying RunspacePool.
        /// </summary>
        internal void Start()
        {
            // open the runspace pool
            localRunspacePool.Open();
        }

        /// <summary>
        /// Send applicaiton private data to client
        /// will be called during runspace creation 
        /// and each time a new client connects to the server session
        /// </summary>
        internal void SendApplicationPrivateDataToClient()
        {
            // Include Debug mode information.
            if (this.applicationPrivateData == null)
            {
                this.applicationPrivateData = new PSPrimitiveDictionary();
            }

            if (serverRemoteDebugger != null)
            {
                // Current debug mode.
                DebugModes debugMode = serverRemoteDebugger.DebugMode;
                if (this.applicationPrivateData.ContainsKey(RemoteDebugger.DebugModeSetting))
                {
                    this.applicationPrivateData[RemoteDebugger.DebugModeSetting] = (int)debugMode;
                }
                else
                {
                    this.applicationPrivateData.Add(RemoteDebugger.DebugModeSetting, (int)debugMode);
                }

                // Current debug state.
                bool inBreakpoint = serverRemoteDebugger.InBreakpoint;
                if (this.applicationPrivateData.ContainsKey(RemoteDebugger.DebugStopState))
                {
                    this.applicationPrivateData[RemoteDebugger.DebugStopState] = inBreakpoint;
                }
                else
                {
                    this.applicationPrivateData.Add(RemoteDebugger.DebugStopState, inBreakpoint);
                }

                // Current debug breakpoint count.
                int breakpointCount = serverRemoteDebugger.GetBreakpointCount();
                if (this.applicationPrivateData.ContainsKey(RemoteDebugger.DebugBreakpointCount))
                {
                    this.applicationPrivateData[RemoteDebugger.DebugBreakpointCount] = breakpointCount;
                }
                else
                {
                    this.applicationPrivateData.Add(RemoteDebugger.DebugBreakpointCount, breakpointCount);
                }

                // Current debugger BreakAll option setting.
                bool breakAll = serverRemoteDebugger.IsDebuggerSteppingEnabled;
                if (this.applicationPrivateData.ContainsKey(RemoteDebugger.BreakAllSetting))
                {
                    this.applicationPrivateData[RemoteDebugger.BreakAllSetting] = breakAll;
                }
                else
                {
                    this.applicationPrivateData.Add(RemoteDebugger.BreakAllSetting, breakAll);
                }

                // Current debugger PreserveUnhandledBreakpoints setting.
                UnhandledBreakpointProcessingMode bpMode = serverRemoteDebugger.UnhandledBreakpointMode;
                if (this.applicationPrivateData.ContainsKey(RemoteDebugger.UnhandledBreakpointModeSetting))
                {
                    this.applicationPrivateData[RemoteDebugger.UnhandledBreakpointModeSetting] = (int)bpMode;
                }
                else
                {
                    this.applicationPrivateData.Add(RemoteDebugger.UnhandledBreakpointModeSetting, (int)bpMode);
                }
            }

            dsHandler.SendApplicationPrivateDataToClient(this.applicationPrivateData, this.serverCapability);
        }

        /// <summary>
        /// Dispose the runspace pool driver and release all its resources
        /// </summary>
        internal void Close()
        {
            if (!isClosed)
            {
                isClosed = true;

                if ((this.remoteHost != null) && (this.remoteHost.IsRunspacePushed))
                {
                    Runspace runspaceToDispose = this.remoteHost.PushedRunspace;
                    this.remoteHost.PopRunspace();
                    if (runspaceToDispose != null)
                    {
                        runspaceToDispose.Dispose();
                    }
                }

                DisposeRemoteDebugger();

                localRunspacePool.Close();
                localRunspacePool.StateChanged -=
                                new EventHandler<RunspacePoolStateChangedEventArgs>(HandleRunspacePoolStateChanged);
                localRunspacePool.ForwardEvent -=
                                new EventHandler<PSEventArgs>(HandleRunspacePoolForwardEvent);
                localRunspacePool.Dispose();
                localRunspacePool = null;

                if (rsToUseForSteppablePipeline != null)
                {
                    rsToUseForSteppablePipeline.Close();
                    rsToUseForSteppablePipeline.Dispose();
                    rsToUseForSteppablePipeline = null;
                }
                Closed.SafeInvoke(this, EventArgs.Empty);
            }
        }

        #endregion Internal Methods

        #region IRSPDriverInvoke interface methods

        /// <summary>
        /// This method blocks the current thread execution and starts a 
        /// new Invoker pump that will handle invoking client side nested commands.  
        /// This method returns after ExitNestedPipeline is called.
        /// </summary>
        public void EnterNestedPipeline()
        {
            if (driverNestedInvoker == null)
            {
                throw new PSNotSupportedException(RemotingErrorIdStrings.NestedPipelineNotSupported);
            }

            driverNestedInvoker.PushInvoker();
        }

        /// <summary>
        /// Removes current nested command Invoker pump and allows parent command
        /// to continue running.
        /// </summary>
        public void ExitNestedPipeline()
        {
            if (driverNestedInvoker == null)
            {
                throw new PSNotSupportedException(RemotingErrorIdStrings.NestedPipelineNotSupported);
            }

            driverNestedInvoker.PopInvoker();
        }

        /// <summary>
        /// If script execution is currently in debugger stopped mode, this will
        /// release the debugger and terminate script execution, or if processing 
        /// a debug command will stop the debug command.
        /// This is used to implement the remote stop signal and ensures a command
        /// will stop even when in debug stop mode.
        /// </summary>
        public bool HandleStopSignal()
        {
            if (this.serverRemoteDebugger != null)
            {
                return this.serverRemoteDebugger.HandleStopSignal();
            }

            return false;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// RunspaceCreated eventhandler. This is used to set TypeTable for TransportManager.
        /// TransportManager needs TypeTable for Serializing/Deserializing objects.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void HandleRunspaceCreatedForTypeTable(object sender, RunspaceCreatedEventArgs args)
        {
            this.dsHandler.TypeTable = args.Runspace.ExecutionContext.TypeTable;
            this.rsToUseForSteppablePipeline = args.Runspace;

            SetupRemoteDebugger(this.rsToUseForSteppablePipeline);

            if (!string.IsNullOrEmpty(this.configurationName))
            {
                // Client is requesting a configured session.  
                // Create a configured remote runspace and push onto host stack.
                if ((this.remoteHost != null) && !(this.remoteHost.IsRunspacePushed))
                {
                    // Let exceptions propagate.
                    RemoteRunspace remoteRunspace = HostUtilities.CreateConfiguredRunspace(this.configurationName, this.remoteHost);

                    this.remoteHost.AllowPushRunspace = true;
                    this.remoteHost.PropagatePop = true;

                    this.remoteHost.PushRunspace(remoteRunspace);
                }
            }
        }

        private void SetupRemoteDebugger(Runspace runspace)
        {
            CmdletInfo cmdletInfo = runspace.ExecutionContext.SessionState.InvokeCommand.GetCmdlet(ServerRemoteDebugger.SetPSBreakCommandText);
            if (cmdletInfo == null)
            {
                if((runspace.ExecutionContext.LanguageMode != PSLanguageMode.FullLanguage) &&
                    (! runspace.ExecutionContext.UseFullLanguageModeInDebugger))
                {
                    return;
                }
            }
            else
            {
                if(cmdletInfo.Visibility != SessionStateEntryVisibility.Public)
                {
                    return;
                }
            }

            // Remote debugger is created only when client version is PSVersion (4.0)
            // or greater, and remote session supports debugging.
            if ((driverNestedInvoker != null) &&
                (clientPSVersion != null && clientPSVersion >= PSVersionInfo.PSV4Version) &&
                (runspace != null && runspace.Debugger != null))
            {
                this.serverRemoteDebugger = new ServerRemoteDebugger(this, runspace, runspace.Debugger);
                this.remoteHost.ServerDebugger = this.serverRemoteDebugger;
            }
        }

        private void DisposeRemoteDebugger()
        {
            if (serverRemoteDebugger != null)
            {
                serverRemoteDebugger.Dispose();
            }
        }

        /// <summary>
        /// Invokes a script
        /// </summary>
        /// <param name="cmdToRun"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private PSDataCollection<PSObject> InvokeScript(Command cmdToRun, RunspaceCreatedEventArgs args)
        {
            Debug.Assert(cmdToRun != null, "cmdToRun shouldn't be null");

            cmdToRun.CommandOrigin = CommandOrigin.Internal;
            cmdToRun.MergeMyResults(PipelineResultTypes.Error, PipelineResultTypes.Output);
            PowerShell powershell = PowerShell.Create();
            powershell.AddCommand(cmdToRun).AddCommand("out-default");

            return InvokePowerShell(powershell, args);
        }

        /// <summary>
        /// Invokes a PowerShell instance 
        /// </summary>
        /// <param name="powershell"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        private PSDataCollection<PSObject> InvokePowerShell(PowerShell powershell, RunspaceCreatedEventArgs args)
        {
            Debug.Assert(powershell != null, "powershell shouldn't be null");

            // run the startup script on the runspace's host
            HostInfo hostInfo = this.remoteHost.HostInfo;
            ServerPowerShellDriver driver = new ServerPowerShellDriver(
                powershell, 
                null,
                true, 
                Guid.Empty, 
                this.InstanceId, 
                this,
#if !CORECLR // No ApartmentState In CoreCLR
                args.Runspace.ApartmentState,
#endif                
                hostInfo, 
                RemoteStreamOptions.AddInvocationInfo, 
                false, 
                args.Runspace);

            IAsyncResult asyncResult = driver.Start();

            // if there was an exception running the script..this may throw..this will
            // result in the runspace getting closed/broken.
            PSDataCollection<PSObject> results = powershell.EndInvoke(asyncResult);

            // find out if there are any error records reported. If there is one, report the error..
            // this will result in the runspace getting closed/broken.
            ArrayList errorList = (ArrayList)powershell.Runspace.GetExecutionContext.DollarErrorVariable;
            if (errorList.Count > 0)
            {
                string exceptionThrown;
                ErrorRecord lastErrorRecord = errorList[0] as ErrorRecord;
                if (lastErrorRecord != null)
                {
                    exceptionThrown = lastErrorRecord.ToString();
                }
                else
                {
                    Exception lastException = errorList[0] as Exception;
                    if (lastException != null)
                    {
                        exceptionThrown = (lastException.Message != null) ? lastException.Message : string.Empty;
                    }
                    else
                    {
                        exceptionThrown = string.Empty;
                    }
                }

                throw PSTraceSource.NewInvalidOperationException(RemotingErrorIdStrings.StartupScriptThrewTerminatingError, exceptionThrown);
            }

            return results;
        }

        /// <summary>
        /// Raised by RunspacePool whenever a new runspace is created. This is used
        /// by the driver to run startup script as well as set personal folder
        /// as the current working directory.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args">
        /// Runspace that was created by the RunspacePool.
        /// </param>
        private void HandleRunspaceCreated(object sender, RunspaceCreatedEventArgs args)
        {
            this.ServerRemoteHost.Runspace = args.Runspace;

            // If the system lockdown policy says "Enforce", do so (unless it's in the
            // more restrictive NoLanguage mode)
            if ((SystemPolicy.GetSystemLockdownPolicy() == SystemEnforcementMode.Enforce) &&
                (args.Runspace.ExecutionContext.LanguageMode != PSLanguageMode.NoLanguage))
            {
                args.Runspace.ExecutionContext.LanguageMode = PSLanguageMode.ConstrainedLanguage;
            }

            // Set the current location to MyDocuments folder for this runspace.
            // This used to be set to the Personal folder but was changed to MyDocuments folder for 
            // compatibility with PowerShell on Nano Server for PowerShell V5.
            // This is needed because in the remoting scenario, Environment.CurrentDirectory
            // always points to System Folder (%windir%\system32) irrespective of the
            // user as %HOMEDRIVE% and %HOMEPATH% are not available for the logon process.
            // Doing this here than AutomationEngine as I dont want to introduce a dependency
            // on Remoting in PowerShell engine
            try
            {
                string personalfolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                args.Runspace.ExecutionContext.EngineSessionState.SetLocation(personalfolder);
            }
            catch (Exception e)
            {
                // SetLocation API can call 3rd party code and so there is no telling what exception may be thrown.
                // Setting location is not critical and is expected not to work with some account types, so we want
                // to ignore all but critical errors.
                CommandProcessorBase.CheckForSevereException(e);
            }

            // Run startup scripts
            InvokeStartupScripts(args);

            // Now that the server side runspace is set up allow the secondary handler to run.
            HandleRunspaceCreatedForTypeTable(sender, args);
        }

        private void InvokeStartupScripts(RunspaceCreatedEventArgs args)
        {
            Command cmdToRun = null;
            if (!string.IsNullOrEmpty(configData.StartupScript))
            {
                // build the startup script..merge output / error.
                cmdToRun = new Command(configData.StartupScript, false, false);
            }
            else if (!string.IsNullOrEmpty(configData.InitializationScriptForOutOfProcessRunspace))
            {
                cmdToRun = new Command(configData.InitializationScriptForOutOfProcessRunspace, true, false);
            }

            if (null != cmdToRun)
            {
                InvokeScript(cmdToRun, args);

                // if startup script set $PSApplicationPrivateData, then use that value as ApplicationPrivateData
                // instead of using results from PSSessionConfiguration.GetApplicationPrivateData()
                if (localRunspacePool.RunspacePoolStateInfo.State == RunspacePoolState.Opening)
                {
                    object privateDataVariable = args.Runspace.SessionStateProxy.PSVariable.GetValue("global:PSApplicationPrivateData");
                    if (privateDataVariable != null)
                    {
                        this.applicationPrivateData = (PSPrimitiveDictionary)LanguagePrimitives.ConvertTo(
                            privateDataVariable,
                            typeof(PSPrimitiveDictionary),
                            true,
                            CultureInfo.InvariantCulture,
                            null);
                    }
                }
            }
        }

        /// <summary>
        /// handler to the runspace pool state changed events
        /// </summary>
        /// <param name="sender">sender of this events</param>
        /// <param name="eventArgs">arguments which describe the
        /// RunspacePool's StateChanged event</param>
        private void HandleRunspacePoolStateChanged(object sender,
                            RunspacePoolStateChangedEventArgs eventArgs)
        {
            RunspacePoolState state = eventArgs.RunspacePoolStateInfo.State;
            Exception reason = eventArgs.RunspacePoolStateInfo.Reason;

            switch(state)
            {
                case RunspacePoolState.Broken:
                case RunspacePoolState.Closing:
                case RunspacePoolState.Closed:
                    {
                        dsHandler.SendStateInfoToClient(new RunspacePoolStateInfo(state, reason));
                    }
                    break;

                case RunspacePoolState.Opened:
                    {
                        SendApplicationPrivateDataToClient();
                        dsHandler.SendStateInfoToClient(new RunspacePoolStateInfo(state, reason));
                    }
                    break;
            }                
        }

        /// <summary>
        /// handler to the runspace pool psevents
        /// </summary>
        private void HandleRunspacePoolForwardEvent(object sender, PSEventArgs e)
        {
            if (e.ForwardEvent)
            {
                dsHandler.SendPSEventArgsToClient(e);
            }
        }

        /// <summary>
        /// Handle the invocation of powershell
        /// </summary>
        /// <param name="sender">sender of this event, unused</param>
        /// <param name="eventArgs">arguments describing this event</param>
        private void HandleCreateAndInvokePowerShell(object sender, RemoteDataEventArgs<RemoteDataObject<PSObject>> eventArgs)
        {
            RemoteDataObject<PSObject> data = eventArgs.Data;

            // it is sufficient to just construct the powershell
            // driver, the local powershell on server side is
            // invoked from within the driver
            HostInfo hostInfo = RemotingDecoder.GetHostInfo(data.Data);

#if !CORECLR // No ApartmentState In CoreCLR
            ApartmentState apartmentState = RemotingDecoder.GetApartmentState(data.Data);
#endif

            RemoteStreamOptions streamOptions = RemotingDecoder.GetRemoteStreamOptions(data.Data);
            PowerShell powershell = RemotingDecoder.GetPowerShell(data.Data);
            bool noInput = RemotingDecoder.GetNoInput(data.Data);
            bool addToHistory = RemotingDecoder.GetAddToHistory(data.Data);
            bool isNested = false;

            // The server would've dropped the protocol version of an older client was connecting
            if (this.serverCapability.ProtocolVersion >= RemotingConstants.ProtocolVersionWin8RTM)
            {
                isNested = RemotingDecoder.GetIsNested(data.Data);
            }

            // Perform pre-processing of command for over the wire debugging commands.
            if (serverRemoteDebugger != null)
            {
                DebuggerCommandArgument commandArgument;
                bool terminateImmediate = false;
                var result = PreProcessDebuggerCommand(powershell.Commands, serverRemoteDebugger.IsActive, serverRemoteDebugger.IsRemote, out commandArgument);

                switch (result)
                {
                    case PreProcessCommandResult.SetDebuggerAction:
                        // Run this directly on the debugger and terminate the remote command.
                        serverRemoteDebugger.SetDebuggerAction(commandArgument.ResumeAction.Value);
                        terminateImmediate = true;
                        break;

                    case PreProcessCommandResult.SetDebugMode:
                        // Set debug mode directly and terminate remote command.
                        serverRemoteDebugger.SetDebugMode(commandArgument.Mode.Value);
                        terminateImmediate = true;
                        break;

                    case PreProcessCommandResult.SetDebuggerStepMode:
                        // Enable debugger and set to step action, then terminate remote command.
                        serverRemoteDebugger.SetDebuggerStepMode(commandArgument.DebuggerStepEnabled.Value);
                        terminateImmediate = true;
                        break;

                    case PreProcessCommandResult.SetPreserveUnhandledBreakpointMode:
                        serverRemoteDebugger.UnhandledBreakpointMode = commandArgument.UnhandledBreakpointMode.Value;
                        terminateImmediate = true;
                        break;

                    case PreProcessCommandResult.ValidNotProcessed:
                        terminateImmediate = true;
                        break;
                }

                // If we don't want to run or queue a command to run in the server session then
                // terminate the command here by making it a No Op.
                if (terminateImmediate)
                {
                    ServerPowerShellDriver noOpDriver = new ServerPowerShellDriver(
                        powershell, 
                        null,
                        noInput, 
                        data.PowerShellId, 
                        data.RunspacePoolId, 
                        this, 
#if !CORECLR // No ApartmentState In CoreCLR
                        apartmentState,
#endif
                        hostInfo, 
                        streamOptions, 
                        addToHistory, 
                        null);

                    noOpDriver.RunNoOpCommand();
                    return;
                }
            }

            if (remoteHost.IsRunspacePushed)
            {
                // If we have a pushed runspace then execute there.  
                // Ensure debugger is enabled to the original mode it was set to.
                if (serverRemoteDebugger != null)
                {
                    serverRemoteDebugger.CheckDebuggerState();
                }

                StartPowerShellCommandOnPushedRunspace(
                    powershell,
                    null,
                    data.PowerShellId,
                    data.RunspacePoolId,
                    hostInfo,
                    streamOptions,
                    noInput,
                    addToHistory);

                return;
            }
            else if (isNested)
            {
                if (localRunspacePool.GetMaxRunspaces() == 1)
                {
                    if (driverNestedInvoker != null && driverNestedInvoker.IsActive)
                    {
                        if (driverNestedInvoker.IsAvailable == false)
                        {
                            // A nested command is already running.
                            throw new PSInvalidOperationException(
                                StringUtil.Format(RemotingErrorIdStrings.CannotInvokeNestedCommandNestedCommandRunning));
                        }

                        // Handle as nested pipeline invocation.
                        powershell.SetIsNested(true);

                        // Always invoke PowerShell commands on pipeline worker thread
                        // for single runspace case, to support nested invocation requests (debugging scenario).
                        ServerPowerShellDriver srdriver = new ServerPowerShellDriver(
                            powershell, 
                            null,
                            noInput, 
                            data.PowerShellId, 
                            data.RunspacePoolId, 
                            this, 
#if !CORECLR // No ApartmentState In CoreCLR
                            apartmentState,
#endif
                            hostInfo, 
                            streamOptions, 
                            addToHistory, 
                            rsToUseForSteppablePipeline);

                        inputCollection = srdriver.InputCollection;
                        driverNestedInvoker.InvokeDriverAsync(srdriver);
                        return;
                    }
                    else if (this.serverRemoteDebugger != null && 
                             this.serverRemoteDebugger.InBreakpoint &&
                             this.serverRemoteDebugger.IsPushed)
                    {

                        this.serverRemoteDebugger.StartPowerShellCommand(
                            powershell,
                            data.PowerShellId,
                            data.RunspacePoolId,
                            this,
#if !CORECLR // No ApartmentState In CoreCLR
                            apartmentState,
#endif
                            remoteHost,
                            hostInfo,
                            streamOptions,
                            addToHistory);

                        return;
                    }
                    else if (powershell.Commands.Commands.Count == 1 &&
                             !powershell.Commands.Commands[0].IsScript &&
                             ((powershell.Commands.Commands[0].CommandText.IndexOf("Get-PSDebuggerStopArgs", StringComparison.OrdinalIgnoreCase) != -1) ||
                              (powershell.Commands.Commands[0].CommandText.IndexOf("Set-PSDebuggerAction", StringComparison.OrdinalIgnoreCase) != -1)))
                    {
                        // We do not want to invoke debugger commands in the steppable pipeline.
                        // Consider adding IsSteppable message to PSRP to handle this.
                        // This will be caught on the client.
                        throw new PSInvalidOperationException();
                    }

                    ServerPowerShellDataStructureHandler psHandler = this.dsHandler.GetPowerShellDataStructureHandler();
                    if (psHandler != null)
                    {        
                        // Have steppable invocation request.
                        powershell.SetIsNested(false);
                        // Execute command concurrently
                        ServerSteppablePipelineDriver spDriver = new ServerSteppablePipelineDriver(
                            powershell, 
                            noInput,
                            data.PowerShellId, 
                            data.RunspacePoolId, 
                            this, 
#if !CORECLR // No ApartmentState In CoreCLR
                            apartmentState,
#endif
                            hostInfo,
                            streamOptions, 
                            addToHistory, 
                            rsToUseForSteppablePipeline, 
                            eventSubscriber, 
                            inputCollection);

                        spDriver.Start();
                        return;
                    }
                }

                // Allow command to run as non-nested and non-stepping.
                powershell.SetIsNested(false);
            }

            // Invoke command normally.  Ensure debugger is enabled to the 
            // original mode it was set to.
            if (serverRemoteDebugger != null)
            {
                serverRemoteDebugger.CheckDebuggerState();
            }

            // Invoke PowerShell on driver runspace pool.
            ServerPowerShellDriver driver = new ServerPowerShellDriver(
                powershell, 
                null,
                noInput, 
                data.PowerShellId, 
                data.RunspacePoolId, 
                this, 
#if !CORECLR // No ApartmentState In CoreCLR
                apartmentState,
#endif
                hostInfo, 
                streamOptions, 
                addToHistory, 
                null);

            inputCollection = driver.InputCollection;
            driver.Start();
        }

        private bool? _initialSessionStateIncludesGetCommandWithListImportedSwitch;
        private object _initialSessionStateIncludesGetCommandWithListImportedSwitchLock = new object();
        private bool DoesInitialSessionStateIncludeGetCommandWithListImportedSwitch()
        {
            if (!_initialSessionStateIncludesGetCommandWithListImportedSwitch.HasValue)
            {
                lock (_initialSessionStateIncludesGetCommandWithListImportedSwitchLock)
                {
                    if (!_initialSessionStateIncludesGetCommandWithListImportedSwitch.HasValue)
                    {
                        bool newValue = false;

                        InitialSessionState iss = this.RunspacePool.InitialSessionState;
                        if (iss != null)
                        {
                            IEnumerable<SessionStateCommandEntry> publicGetCommandEntries = iss
                                .Commands["Get-Command"]
                                .Where(entry => entry.Visibility == SessionStateEntryVisibility.Public);
                            SessionStateFunctionEntry getCommandProxy = publicGetCommandEntries.OfType<SessionStateFunctionEntry>().FirstOrDefault();
                            if (getCommandProxy != null)
                            {
                                if (getCommandProxy.ScriptBlock.ParameterMetadata.BindableParameters.ContainsKey("ListImported"))
                                {
                                    newValue = true;
                                }
                            }
                            else
                            {
                                SessionStateCmdletEntry getCommandCmdlet = publicGetCommandEntries.OfType<SessionStateCmdletEntry>().FirstOrDefault();
                                if ((getCommandCmdlet != null) && (getCommandCmdlet.ImplementingType.Equals(typeof(Microsoft.PowerShell.Commands.GetCommandCommand))))
                                {
                                    newValue = true;
                                }
                            }
                        }

                        _initialSessionStateIncludesGetCommandWithListImportedSwitch = newValue;
                    }
                }
            }

            return _initialSessionStateIncludesGetCommandWithListImportedSwitch.Value;
        }

        /// <summary>
        /// Handle the invocation of command discovery pipeline
        /// </summary>
        /// <param name="sender">sender of this event, unused</param>
        /// <param name="eventArgs">arguments describing this event</param>
        private void HandleGetCommandMetadata(object sender, RemoteDataEventArgs<RemoteDataObject<PSObject>> eventArgs)
        {
            RemoteDataObject<PSObject> data = eventArgs.Data;

            PowerShell countingPipeline = RemotingDecoder.GetCommandDiscoveryPipeline(data.Data);
            if (this.DoesInitialSessionStateIncludeGetCommandWithListImportedSwitch())
            {
                countingPipeline.AddParameter("ListImported", true);
            }
            countingPipeline
                .AddParameter("ErrorAction", "SilentlyContinue")
                .AddCommand("Measure-Object")
                .AddCommand("Select-Object")
                .AddParameter("Property", "Count");

            PowerShell mainPipeline = RemotingDecoder.GetCommandDiscoveryPipeline(data.Data);
            if (this.DoesInitialSessionStateIncludeGetCommandWithListImportedSwitch())
            {
                mainPipeline.AddParameter("ListImported", true);
            }
            mainPipeline
                .AddCommand("Select-Object")
                .AddParameter("Property", new string[] {
                    "Name", "Namespace", "HelpUri", "CommandType", "ResolvedCommandName", "OutputType", "Parameters" });

            HostInfo useRunspaceHost = new HostInfo(null);
            useRunspaceHost.UseRunspaceHost = true;

            if (remoteHost.IsRunspacePushed)
            {
                // If we have a pushed runspace then execute there.  
                StartPowerShellCommandOnPushedRunspace(
                    countingPipeline,
                    mainPipeline,
                    data.PowerShellId,
                    data.RunspacePoolId,
                    useRunspaceHost,
                    0,
                    true,
                    false);
            }
            else
            {
                // Run on usual driver.
                ServerPowerShellDriver driver = new ServerPowerShellDriver(
                    countingPipeline,
                    mainPipeline,
                    true /* no input */,
                    data.PowerShellId,
                    data.RunspacePoolId,
                    this,
#if !CORECLR // No ApartmentState In CoreCLR
                    ApartmentState.Unknown,
#endif
                    useRunspaceHost,
                    0 /* stream options */,
                    false /* addToHistory */,
                    null /* use default rsPool runspace */);

                driver.Start();
            }
        }
        
        /// <summary>
        /// Handles host responses
        /// </summary>
        /// <param name="sender">sender of this event, unused</param>
        /// <param name="eventArgs">arguments describing this event</param>
        private void HandleHostResponseReceived(object sender, 
            RemoteDataEventArgs<RemoteHostResponse> eventArgs)
        {
            remoteHost.ServerMethodExecutor.HandleRemoteHostResponseFromClient((eventArgs.Data));
        }

        /// <summary>
        /// Sets the maximum runspace of the runspace pool and sends a response back
        /// </summary>
        /// <param name="sender">sender of this event, unused</param>
        /// <param name="eventArgs">contains information about the new maxRunspaces
        /// and the callId at the client</param>
        private void HandleSetMaxRunspacesReceived(object sender, RemoteDataEventArgs<PSObject> eventArgs)
        {
            PSObject data = eventArgs.Data;
            int maxRunspaces = (int)((PSNoteProperty)data.Properties[RemoteDataNameStrings.MaxRunspaces]).Value;
            long callId = (long)((PSNoteProperty)data.Properties[RemoteDataNameStrings.CallId]).Value;

            bool response = localRunspacePool.SetMaxRunspaces(maxRunspaces);
            dsHandler.SendResponseToClient(callId, response);
        }

        /// <summary>
        /// Sets the minimum runspace of the runspace pool and sends a response back
        /// </summary>
        /// <param name="sender">sender of this event, unused</param>
        /// <param name="eventArgs">contains information about the new minRunspaces
        /// and the callId at the client</param>
        private void HandleSetMinRunspacesReceived(object sender, RemoteDataEventArgs<PSObject> eventArgs)
        {
            PSObject data = eventArgs.Data;
            int minRunspaces = (int)((PSNoteProperty)data.Properties[RemoteDataNameStrings.MinRunspaces]).Value;
            long callId = (long)((PSNoteProperty)data.Properties[RemoteDataNameStrings.CallId]).Value;

            bool response = localRunspacePool.SetMinRunspaces(minRunspaces);
            dsHandler.SendResponseToClient(callId, response);            
        }

        /// <summary>
        /// Gets the available runspaces from the server and sends it across
        /// to the client
        /// </summary>
        /// <param name="sender">sender of this event, unused</param>
        /// <param name="eventArgs">contains information on the callid</param>
        private void HandleGetAvailalbeRunspacesReceived(object sender, RemoteDataEventArgs<PSObject> eventArgs)
        {
            PSObject data = eventArgs.Data;
            long callId = (long)((PSNoteProperty)data.Properties[RemoteDataNameStrings.CallId]).Value;

            int availableRunspaces = localRunspacePool.GetAvailableRunspaces();

            dsHandler.SendResponseToClient(callId, availableRunspaces);
        }

        /// <summary>
        /// Forces a state reset on a single runspace runspace pool.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void HandleResetRunspaceState(object sender, RemoteDataEventArgs<PSObject> eventArgs)
        {
            long callId = (long)((PSNoteProperty)(eventArgs.Data).Properties[RemoteDataNameStrings.CallId]).Value;
            bool response = ResetRunspaceState();

            dsHandler.SendResponseToClient(callId, response);
        }

        /// <summary>
        /// Resets the single runspace in the runspace pool.
        /// </summary>
        /// <returns></returns>
        private bool ResetRunspaceState()
        {
            LocalRunspace runspaceToReset = this.rsToUseForSteppablePipeline as LocalRunspace;
            if ((runspaceToReset == null) || (localRunspacePool.GetMaxRunspaces() > 1))
            {
                return false;
            }

            try
            {
                // Local runspace state reset.
                runspaceToReset.ResetRunspaceState();
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Starts the PowerShell command on the currently pushed Runspace
        /// </summary>
        /// <param name="powershell">PowerShell command or script</param>
        /// <param name="extraPowerShell">PowerShell command to run after first completes</param>
        /// <param name="powershellId">PowerShell Id</param>
        /// <param name="runspacePoolId">RunspacePool Id</param>
        /// <param name="hostInfo">Host Info</param>
        /// <param name="streamOptions">Remote stream options</param>
        /// <param name="noInput">False when input is provided</param>
        /// <param name="addToHistory">Add to history</param>
        private void StartPowerShellCommandOnPushedRunspace(
            PowerShell powershell,
            PowerShell extraPowerShell,
            Guid powershellId,
            Guid runspacePoolId,
            HostInfo hostInfo,
            RemoteStreamOptions streamOptions,
            bool noInput,
            bool addToHistory)
        {
            Runspace runspace = this.remoteHost.PushedRunspace;

            ServerPowerShellDriver driver = new ServerPowerShellDriver(
                powershell,
                extraPowerShell,
                noInput,
                powershellId,
                runspacePoolId,
                this,
#if !CORECLR // No ApartmentState In CoreCLR
                ApartmentState.MTA,
#endif
                hostInfo,
                streamOptions,
                addToHistory,
                runspace);

            try
            {
                driver.Start();
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);

                // Pop runspace on error.
                this.remoteHost.PopRunspace();

                throw;
            }
        }

        #endregion Private Methods

        #region Remote Debugger Command Helpers

        /// <summary>
        /// Debugger command pre processing result type.
        /// </summary>
        private enum PreProcessCommandResult
        {
            /// <summary>
            /// No debugger pre-processing
            /// </summary>
            None = 0,

            /// <summary>
            /// This is a valid debugger command but was not processed because
            /// the debugger state was not correct.
            /// </summary>
            ValidNotProcessed,

            /// <summary>
            /// GetDebuggerStopArgs
            /// </summary>
            GetDebuggerStopArgs,

            /// <summary>
            /// SetDebuggerAction
            /// </summary>
            SetDebuggerAction,

            /// <summary>
            /// SetDebugMode
            /// </summary>
            SetDebugMode,

            /// <summary>
            /// SetDebuggerStepMode
            /// </summary>
            SetDebuggerStepMode,

            /// <summary>
            /// SetPreserveUnhandledBreakpointMode
            /// </summary>
            SetPreserveUnhandledBreakpointMode
        };

        private class DebuggerCommandArgument
        {
            public DebugModes? Mode { get; set; }

            public DebuggerResumeAction? ResumeAction { get; set; }

            public bool? DebuggerStepEnabled { get; set; }

            public UnhandledBreakpointProcessingMode? UnhandledBreakpointMode { get; set; }
        }

        /// <summary>
        /// Pre-processor for debugger commands.
        /// Parses special debugger commands and converts to equivalent script for remote execution as needed.
        /// </summary>
        /// <param name="commands">PSCommand</param>
        /// <param name="isDebuggerActive">True if debugger is active.</param>
        /// <param name="isDebuggerRemote">True if active debugger is pushed and is a remote debugger.</param>
        /// <param name="commandArgument">Command argument.</param>
        /// <returns>PreProcessCommandResult type if preprocessing occurred.</returns>
        private static PreProcessCommandResult PreProcessDebuggerCommand(
            PSCommand commands,
            bool isDebuggerActive,
            bool isDebuggerRemote,
            out DebuggerCommandArgument commandArgument)
        {
            commandArgument = new DebuggerCommandArgument();
            PreProcessCommandResult result = PreProcessCommandResult.None;

            if ((commands.Commands.Count == 0) || (commands.Commands[0].IsScript))
            {
                return result;
            }

            var command = commands.Commands[0];
            string commandText = command.CommandText;
            if (commandText.Equals(DebuggerUtils.GetDebuggerStopArgsFunctionName, StringComparison.OrdinalIgnoreCase))
            {
                //
                // __Get-PSDebuggerStopArgs private virtual command.
                // No input parameters.
                // Returns DebuggerStopEventArgs object.
                //

                // Evaluate this command only if the debugger is activated.
                if (!isDebuggerActive) { return PreProcessCommandResult.ValidNotProcessed; }

                // Translate into debugger method call.
                ScriptBlock scriptBlock = ScriptBlock.Create("$host.Runspace.Debugger.GetDebuggerStopArgs()");
                scriptBlock.LanguageMode = PSLanguageMode.FullLanguage;
                commands.Clear();
                commands.AddCommand("Invoke-Command").AddParameter("ScriptBlock", scriptBlock).AddParameter("NoNewScope", true);

                result = PreProcessCommandResult.GetDebuggerStopArgs;
            }
            else if (commandText.Equals(DebuggerUtils.SetDebuggerActionFunctionName, StringComparison.OrdinalIgnoreCase))
            {
                //
                // __Set-PSDebuggerAction private virtual command.
                // DebuggerResumeAction enum input parameter.
                // Returns void.
                //

                // Evaluate this command only if the debugger is activated.
                if (!isDebuggerActive) { return PreProcessCommandResult.ValidNotProcessed; }

                if ((command.Parameters == null) || (command.Parameters.Count == 0) ||
                    (!command.Parameters[0].Name.Equals("ResumeAction", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new PSArgumentException("ResumeAction");
                }

                DebuggerResumeAction? resumeAction = null;
                PSObject resumeObject = command.Parameters[0].Value as PSObject;
                if (resumeObject != null)
                {
                    try
                    {
                        resumeAction = (DebuggerResumeAction)resumeObject.BaseObject;
                    }
                    catch (InvalidCastException) { }
                }

                if (resumeAction == null)
                {
                    throw new PSArgumentException("ResumeAction");
                }

                commandArgument.ResumeAction = resumeAction;
                result = PreProcessCommandResult.SetDebuggerAction;
            }
            else if (commandText.Equals(DebuggerUtils.SetDebugModeFunctionName, StringComparison.OrdinalIgnoreCase))
            {
                //
                // __Set-PSDebugMode private virtual command.
                // DebugModes enum input parameter.
                // Returns void.
                //

                if ((command.Parameters == null) || (command.Parameters.Count == 0) ||
                    (!command.Parameters[0].Name.Equals("Mode", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new PSArgumentException("Mode");
                }

                DebugModes? mode = null;
                PSObject modeObject = command.Parameters[0].Value as PSObject;
                if (modeObject != null)
                {
                    try
                    {
                        mode = (DebugModes)modeObject.BaseObject;
                    }
                    catch (InvalidCastException) { }
                }

                if (mode == null)
                {
                    throw new PSArgumentException("Mode");
                }

                commandArgument.Mode = mode;
                result = PreProcessCommandResult.SetDebugMode;
            }
            else if (commandText.Equals(DebuggerUtils.SetDebuggerStepMode, StringComparison.OrdinalIgnoreCase))
            {
                //
                // __Set-PSDebuggerStepMode private virtual command.
                // Boolean Enabled input parameter.
                // Returns void.
                //

                if ((command.Parameters == null) || (command.Parameters.Count == 0) ||
                   (!command.Parameters[0].Name.Equals("Enabled", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new PSArgumentException("Enabled");
                }

                bool enabled = (bool)command.Parameters[0].Value;
                commandArgument.DebuggerStepEnabled = enabled;
                result = PreProcessCommandResult.SetDebuggerStepMode;
            }
            else if (commandText.Equals(DebuggerUtils.SetPSUnhandledBreakpointMode, StringComparison.OrdinalIgnoreCase))
            {
                //
                // __Set-PSUnhandledBreakpointMode private virtual command.
                // UnhandledBreakpointMode input parameter.
                // Returns void.
                //

                if ((command.Parameters == null) || (command.Parameters.Count == 0) ||
                   (!command.Parameters[0].Name.Equals("UnhandledBreakpointMode", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new PSArgumentException("UnhandledBreakpointMode");
                }

                UnhandledBreakpointProcessingMode? mode = null;
                PSObject modeObject = command.Parameters[0].Value as PSObject;
                if (modeObject != null)
                {
                    try
                    {
                        mode = (UnhandledBreakpointProcessingMode)modeObject.BaseObject;
                    }
                    catch (InvalidCastException) { }
                }

                if (mode == null)
                {
                    throw new PSArgumentException("Mode");
                }

                commandArgument.UnhandledBreakpointMode = mode;
                result = PreProcessCommandResult.SetPreserveUnhandledBreakpointMode;
            }

            return result;
        }

        #endregion

        #region Private Classes

        /// <summary>
        /// Helper class to run ServerPowerShellDriver objects on a single thread.  This is 
        /// needed to support nested pipeline execution and remote debugging.
        /// </summary>
        private sealed class PowerShellDriverInvoker
        {
            #region Private Members

            private ConcurrentStack<InvokePump> _invokePumpStack;

            #endregion

            #region Constructor

            /// <summary>
            /// Constructor
            /// </summary>
            public PowerShellDriverInvoker()
            {
                _invokePumpStack = new ConcurrentStack<InvokePump>();
            }

            #endregion

            #region Properties

            /// <summary>
            /// IsActive
            /// </summary>
            public bool IsActive
            {
                get { return !_invokePumpStack.IsEmpty; }
            }

            /// <summary>
            /// True if thread is ready to invoke a PowerShell driver.
            /// </summary>
            public bool IsAvailable
            {
                get
                {
                    InvokePump pump;
                    if (!_invokePumpStack.TryPeek(out pump))
                    {
                        pump = null;
                    }

                    return (pump != null) ? !(pump.IsBusy) : false;
                }
            }

            #endregion

            #region Public Methods

            /// <summary>
            /// Submit a driver object to be invoked.
            /// </summary>
            /// <param name="driver">ServerPowerShellDriver</param>
            public void InvokeDriverAsync(ServerPowerShellDriver driver)
            {
                InvokePump currentPump;
                if (!_invokePumpStack.TryPeek(out currentPump))
                {
                    throw new PSInvalidOperationException(RemotingErrorIdStrings.PowerShellInvokerInvalidState);
                }

                currentPump.Dispatch(driver);
            }

            /// <summary>
            /// Blocking call that creates a new pump object and pumps
            /// driver invokes until stopped via a PopInvoker call.
            /// </summary>
            public void PushInvoker()
            {
                InvokePump newPump = new InvokePump();
                _invokePumpStack.Push(newPump);

                // Blocking call while new driver invocations are handled on
                // new pump.
                newPump.Start();
            }

            /// <summary>
            /// Stops the current driver invoker and restores the previous
            /// invoker object on the stack, if any, to handle driver invocations.
            /// </summary>
            public void PopInvoker()
            {
                InvokePump oldPump;
                if (_invokePumpStack.TryPop(out oldPump))
                {
                    oldPump.Stop();
                }
                else
                {
                    throw new PSInvalidOperationException(RemotingErrorIdStrings.CannotExitNestedPipeline);
                }
            }

            #endregion

            #region Private classes

            /// <summary>
            /// Class that queues and invokes ServerPowerShellDriver objects 
            /// in sequence.
            /// </summary>
            private sealed class InvokePump
            {
                private Queue<ServerPowerShellDriver> _driverInvokeQueue;
                private ManualResetEvent _processDrivers;
                private object _syncObject;
                private bool _stopPump;
                private bool _busy;
                private bool _isDisposed;

                public InvokePump()
                {
                    _driverInvokeQueue = new Queue<ServerPowerShellDriver>();
                    _processDrivers = new ManualResetEvent(false);
                    _syncObject = new object();
                }

                public void Start()
                {
                    try
                    {
                        while (true)
                        {
                            _processDrivers.WaitOne();

                            // Synchronously invoke one ServerPowerShellDriver at a time.
                            ServerPowerShellDriver driver = null;

                            lock (_syncObject)
                            {
                                if (_stopPump)
                                {
                                    break;
                                }

                                if (_driverInvokeQueue.Count > 0)
                                {
                                    driver = _driverInvokeQueue.Dequeue();
                                }

                                if (_driverInvokeQueue.Count == 0)
                                {
                                    _processDrivers.Reset();
                                }
                            }

                            if (driver != null)
                            {
                                try
                                {
                                    _busy = true;
                                    driver.InvokeMain();
                                }
                                catch (Exception e)
                                {
                                    CommandProcessorBase.CheckForSevereException(e);
                                }
                                finally
                                {
                                    _busy = false;
                                }
                            }
                        }
                    }
                    finally
                    {
                        _isDisposed = true;
                        _processDrivers.Dispose();
                    }
                }

                public void Dispatch(ServerPowerShellDriver driver)
                {
                    CheckDisposed();

                    lock (_syncObject)
                    {
                        _driverInvokeQueue.Enqueue(driver);
                        _processDrivers.Set();
                    }
                }

                public void Stop()
                {
                    CheckDisposed();

                    lock (_syncObject)
                    {
                        _stopPump = true;
                        _processDrivers.Set();
                    }
                }

                public bool IsBusy
                {
                    get { return _busy; }
                }

                private void CheckDisposed()
                {
                    if (_isDisposed)
                    {
                        throw new ObjectDisposedException("InvokePump");
                    }
                }
            }

            #endregion
        }

        #endregion
    }

    /// <summary>
    /// This class wraps the script debugger for a ServerRunspacePoolDriver runspace.
    /// </summary>
    internal sealed class ServerRemoteDebugger : Debugger, IDisposable
    {
        #region Private Members

        private IRSPDriverInvoke _driverInvoker;
        private Runspace _runspace;
        private ObjectRef<Debugger> _wrappedDebugger;
        private bool _inDebugMode;
        private DebuggerStopEventArgs _debuggerStopEventArgs;

        private ManualResetEventSlim _nestedDebugStopCompleteEvent;
        private bool _nestedDebugging;
        private ManualResetEventSlim _processCommandCompleteEvent;
        private ThreadCommandProcessing _threadCommandProcessing;

        private bool _raiseStopEventLocally;

        internal const string SetPSBreakCommandText = "Set-PSBreakpoint";

        #endregion

        #region Constructor

        private ServerRemoteDebugger() { }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="driverInvoker"></param>
        /// <param name="runspace"></param>
        /// <param name="debugger"></param>
        internal ServerRemoteDebugger(
            IRSPDriverInvoke driverInvoker,
            Runspace runspace,
            Debugger debugger)
        {
            if (driverInvoker == null)
            {
                throw new PSArgumentNullException("driverInvoker");
            }
            if (runspace == null)
            {
                throw new PSArgumentNullException("runspace");
            }
            if (debugger == null)
            {
                throw new PSArgumentNullException("debugger");
            }
            _driverInvoker = driverInvoker;
            _runspace = runspace;

            _wrappedDebugger = new ObjectRef<Debugger>(debugger);

            SetDebuggerCallbacks();

            _runspace.Name = "RemoteHost";
            _runspace.InternalDebugger = this;
        }

        #endregion

        #region Debugger overrides

        /// <summary>
        /// True when debugger is stopped at a breakpoint.
        /// </summary>
        public override bool InBreakpoint
        {
            get { return _inDebugMode; }
        }

        /// <summary>
        /// Exits debugger mode with the provided resume action.
        /// </summary>
        /// <param name="resumeAction">DebuggerResumeAction</param>
        public override void SetDebuggerAction(DebuggerResumeAction resumeAction)
        {
            if (!_inDebugMode)
            {
                throw new PSInvalidOperationException(
                    StringUtil.Format(DebuggerStrings.CannotSetRemoteDebuggerAction));
            }

            ExitDebugMode(resumeAction);
        }

        /// <summary>
        /// Returns debugger stop event args if in debugger stop state.
        /// </summary>
        /// <returns>DebuggerStopEventArgs</returns>
        public override DebuggerStopEventArgs GetDebuggerStopArgs()
        {
            return _wrappedDebugger.Value.GetDebuggerStopArgs();
        }

        /// <summary>
        /// ProcessCommand
        /// </summary>
        /// <param name="command">Command</param>
        /// <param name="output">Output</param>
        /// <returns></returns>
        public override DebuggerCommandResults ProcessCommand(PSCommand command, PSDataCollection<PSObject> output)
        {
            if (LocalDebugMode)
            {
                return _wrappedDebugger.Value.ProcessCommand(command, output);
            }

            if (!InBreakpoint || (_threadCommandProcessing != null))
            {
                throw new PSInvalidOperationException(
                    StringUtil.Format(DebuggerStrings.CannotProcessDebuggerCommandNotStopped));
            }

            if (_processCommandCompleteEvent == null)
            {
                _processCommandCompleteEvent = new ManualResetEventSlim(false);
            }

            _threadCommandProcessing = new ThreadCommandProcessing(command, output, _wrappedDebugger.Value, _processCommandCompleteEvent);
            try
            {
                return _threadCommandProcessing.Invoke(_nestedDebugStopCompleteEvent);
            }
            finally
            {
                _threadCommandProcessing = null;
            }
        }

        /// <summary>
        /// StopProcessCommand
        /// </summary>
        public override void StopProcessCommand()
        {
            if (LocalDebugMode)
            {
                _wrappedDebugger.Value.StopProcessCommand();
            }

            ThreadCommandProcessing threadCommandProcessing = _threadCommandProcessing;
            if (threadCommandProcessing != null)
            {
                threadCommandProcessing.Stop();
            }
        }

        /// <summary>
        /// SetDebugMode
        /// </summary>
        /// <param name="mode"></param>
        public override void SetDebugMode(DebugModes mode)
        {
            _wrappedDebugger.Value.SetDebugMode(mode);

            base.SetDebugMode(mode);
        }

        /// <summary>
        /// True when debugger is active with breakpoints.
        /// </summary>
        public override bool IsActive
        {
            get
            {
                return (InBreakpoint || _wrappedDebugger.Value.IsActive || _wrappedDebugger.Value.InBreakpoint);
            }
        }

        /// <summary>
        /// Sets debugger stepping mode.
        /// </summary>
        /// <param name="enabled">True if stepping is to be enabled</param>
        public override void SetDebuggerStepMode(bool enabled)
        {
            // Enable both the wrapper and wrapped debuggers for debugging before setting step mode.
            DebugModes mode = DebugModes.LocalScript | DebugModes.RemoteScript;
            base.SetDebugMode(mode);
            _wrappedDebugger.Value.SetDebugMode(mode);

            _wrappedDebugger.Value.SetDebuggerStepMode(enabled);
        }

        /// <summary>
        /// InternalProcessCommand
        /// </summary>
        /// <param name="command"></param>
        /// <param name="output"></param>
        /// <returns></returns>
        internal override DebuggerCommand InternalProcessCommand(string command, IList<PSObject> output)
        {
            return _wrappedDebugger.Value.InternalProcessCommand(command, output);
        }

        /// <summary>
        /// Sets up debugger to debug provided job or its child jobs.
        /// </summary>
        /// <param name="job">
        /// Job object that is either a debuggable job or a container 
        /// of debuggable child jobs.
        /// </param>
        internal override void DebugJob(Job job)
        {
            _wrappedDebugger.Value.DebugJob(job);
        }

        /// <summary>
        /// Removes job from debugger job list and pops its
        /// debugger from the active debugger stack.
        /// </summary>
        /// <param name="job">Job</param>
        internal override void StopDebugJob(Job job)
        {
            _wrappedDebugger.Value.StopDebugJob(job);
        }

        /// <summary>
        /// Sets up debugger to debug provided Runspace in a nested debug session.
        /// </summary>
        /// <param name="runspace">Runspace to debug</param>
        internal override void DebugRunspace(Runspace runspace)
        {
            _wrappedDebugger.Value.DebugRunspace(runspace);
        }

        /// <summary>
        /// Removes the provided Runspace from the nested "active" debugger state.
        /// </summary>
        /// <param name="runspace">Runspace</param>
        internal override void StopDebugRunspace(Runspace runspace)
        {
            _wrappedDebugger.Value.StopDebugRunspace(runspace);
        }

        /// <summary>
        /// IsPushed
        /// </summary>
        internal override bool IsPushed
        {
            get
            {
                return _wrappedDebugger.Value.IsPushed;
            }
        }

        /// <summary>
        /// IsRemote
        /// </summary>
        internal override bool IsRemote
        {
            get
            {
                return _wrappedDebugger.Value.IsRemote;
            }
        }

        /// <summary>
        /// IsDebuggerSteppingEnabled
        /// </summary>
        internal override bool IsDebuggerSteppingEnabled
        {
            get
            {
                return _wrappedDebugger.Value.IsDebuggerSteppingEnabled;
            }
        }

        /// <summary>
        /// UnhandledBreakpointMode
        /// </summary>
        internal override UnhandledBreakpointProcessingMode UnhandledBreakpointMode
        {
            get
            {
                return _wrappedDebugger.Value.UnhandledBreakpointMode;
            }
            set
            {
                _wrappedDebugger.Value.UnhandledBreakpointMode = value;
                if (value == UnhandledBreakpointProcessingMode.Ignore &&
                    this._inDebugMode)
                {
                    // Release debugger stop hold.
                    ExitDebugMode(DebuggerResumeAction.Continue);
                }
            }
        }

        /// <summary>
        /// IsPendingDebugStopEvent
        /// </summary>
        internal override bool IsPendingDebugStopEvent
        {
            get { return _wrappedDebugger.Value.IsPendingDebugStopEvent; }
        }

        /// <summary>
        /// ReleaseSavedDebugStop
        /// </summary>
        internal override void ReleaseSavedDebugStop()
        {
            _wrappedDebugger.Value.ReleaseSavedDebugStop();
        }

        /// <summary>
        /// Returns IEnumerable of CallStackFrame objects.
        /// </summary>
        /// <returns></returns>
        public override IEnumerable<CallStackFrame> GetCallStack()
        {
            return _wrappedDebugger.Value.GetCallStack();
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            RemoveDebuggerCallbacks();
            if (_inDebugMode)
            {
                ExitDebugMode(DebuggerResumeAction.Stop);
            }

            if (_nestedDebugStopCompleteEvent != null)
            {
                _nestedDebugStopCompleteEvent.Dispose();
            }

            if (_processCommandCompleteEvent != null)
            {
                _processCommandCompleteEvent.Dispose();
            }
        }

        #endregion

        #region Private Classes

        private sealed class ThreadCommandProcessing
        {
            // Members
            private ManualResetEventSlim _commandCompleteEvent;
            private Debugger _wrappedDebugger;
            private PSCommand _command;
            private PSDataCollection<PSObject> _output;
            private DebuggerCommandResults _results;
            private Exception _exception;
            private WindowsIdentity _identityToImpersonate;

            // Constructors
            private ThreadCommandProcessing() { }

            public ThreadCommandProcessing(
                PSCommand command,
                PSDataCollection<PSObject> output,
                Debugger debugger,
                ManualResetEventSlim processCommandCompleteEvent)
            {
                _command = command;
                _output = output;
                _wrappedDebugger = debugger;
                _commandCompleteEvent = processCommandCompleteEvent;
            }

            // Methods
            public DebuggerCommandResults Invoke(ManualResetEventSlim startInvokeEvent)
            {
                // Get impersonation information to flow if any.
                WindowsIdentity currentIdentity = null;
                try
                { 
                    currentIdentity = WindowsIdentity.GetCurrent();
                }
                catch (System.Security.SecurityException) { }
                _identityToImpersonate = ((currentIdentity != null) && (currentIdentity.ImpersonationLevel == TokenImpersonationLevel.Impersonation)) ? currentIdentity : null;

                // Signal thread to process command.
                Dbg.Assert(!_commandCompleteEvent.IsSet, "Command complete event shoulds always be non-signaled here.");
                Dbg.Assert(!startInvokeEvent.IsSet, "The event should always be in non-signaled state here.");
                startInvokeEvent.Set();

                // Wait for completion.
                _commandCompleteEvent.Wait();
                _commandCompleteEvent.Reset();

                _identityToImpersonate = null;

                // Propagate exception.
                if (_exception != null)
                {
                    throw _exception;
                }

                // Return command processing results.
                return _results;
            }

            public void Stop()
            {
                Debugger debugger = _wrappedDebugger;
                if (debugger != null)
                {
                    debugger.StopProcessCommand();
                }
            }

            internal void DoInvoke()
            {
#if !CORECLR // TODO:CORECLR - WindowsIdentity.Impersonate() is not available. Use WindowsIdentity.RunImplemented to replace it.
                // Flow impersonation onto thread if needed.
                WindowsImpersonationContext impersonationContext = null;
                if ((_identityToImpersonate != null) &&
                    (_identityToImpersonate.ImpersonationLevel == TokenImpersonationLevel.Impersonation))
                {
                    impersonationContext = _identityToImpersonate.Impersonate();
                }
#endif

                try
                {
                    _results = _wrappedDebugger.ProcessCommand(_command, _output);
                }
                catch (Exception e)
                {
                    CommandProcessorBase.CheckForSevereException(e);
                    _exception = e;
                }
                finally
                {
                    _commandCompleteEvent.Set();

#if !CORECLR // TODO:CORECLR - WindowsIdentity.Impersonate() is not available. Use WindowsIdentity.RunImplemented to replace it.
                    // Restore previous context to thread.
                    if (impersonationContext != null)
                    {
                        try
                        {
                            impersonationContext.Undo();
                            impersonationContext.Dispose();
                        }
                        catch (System.Security.SecurityException) { }
                    }
#endif
                }
            }
        }

        #endregion

        #region Private methods

        /// <summary>
        /// Add Debugger suspend execution callback.
        /// </summary>
        private void SetDebuggerCallbacks()
        {
            if (_runspace != null &&
                _runspace.ExecutionContext != null &&
                _wrappedDebugger.Value != null)
            {
                SubscribeWrappedDebugger(_wrappedDebugger.Value);

                // Register debugger events for remote forwarding.
                var eventManager = _runspace.ExecutionContext.Events;

                if (!eventManager.GetEventSubscribers(RemoteDebugger.RemoteDebuggerStopEvent).GetEnumerator().MoveNext())
                {
                    eventManager.SubscribeEvent(
                        source: null,
                        eventName: null,
                        sourceIdentifier: RemoteDebugger.RemoteDebuggerStopEvent,
                        data: null,
                        action: null,
                        supportEvent: true,
                        forwardEvent: true);
                }

                if (!eventManager.GetEventSubscribers(RemoteDebugger.RemoteDebuggerBreakpointUpdatedEvent).GetEnumerator().MoveNext())
                {
                    eventManager.SubscribeEvent(
                        source: null,
                        eventName: null,
                        sourceIdentifier: RemoteDebugger.RemoteDebuggerBreakpointUpdatedEvent,
                        data: null,
                        action: null,
                        supportEvent: true,
                        forwardEvent: true);
                }
            }
        }

        /// <summary>
        /// Remove the suspend execution callback.
        /// </summary>
        private void RemoveDebuggerCallbacks()
        {
            if (_runspace != null &&
                _runspace.ExecutionContext != null &&
                _wrappedDebugger.Value != null)
            {
                UnsubscribeWrappedDebugger(_wrappedDebugger.Value);

                // Unregister debugger events for remote forwarding.
                var eventManager = _runspace.ExecutionContext.Events;

                foreach (var subscriber in eventManager.GetEventSubscribers(RemoteDebugger.RemoteDebuggerStopEvent))
                {
                    eventManager.UnsubscribeEvent(subscriber);
                }

                foreach (var subscriber in eventManager.GetEventSubscribers(RemoteDebugger.RemoteDebuggerBreakpointUpdatedEvent))
                {
                    eventManager.UnsubscribeEvent(subscriber);
                }
            }
        }

        /// <summary>
        /// Handler for debugger events
        /// </summary>
        private void HandleDebuggerStop(object sender, DebuggerStopEventArgs e)
        {
            // Ignore if we are in restricted mode.
            if (!IsDebuggingSupported()) { return; }

            if (LocalDebugMode)
            {
                // Forward event locally.
                RaiseDebuggerStopEvent(e);
                return;
            }

            if ((DebugMode & DebugModes.RemoteScript) != DebugModes.RemoteScript)
            {
                return;
            }

            _debuggerStopEventArgs = e;
            PSHost contextHost = null;

            try
            {
                // Save current context remote host.
                contextHost = _runspace.ExecutionContext.InternalHost.ExternalHost;

                // Forward event to remote client.
                Dbg.Assert(_runspace != null, "Runspace cannot be null.");
                _runspace.ExecutionContext.Events.GenerateEvent(
                    sourceIdentifier: RemoteDebugger.RemoteDebuggerStopEvent,
                    sender: null,
                    args: new object[] { e },
                    extraData: null);

                //
                // Start the debug mode.  This is a blocking call and will return only
                // after ExitDebugMode() is called.
                //
                EnterDebugMode(_wrappedDebugger.Value.IsPushed);

                // Restore original context remote host.
                _runspace.ExecutionContext.InternalHost.SetHostRef(contextHost);
            }
            catch (Exception ex)
            {
                CommandProcessor.CheckForSevereException(ex);
            }
            finally
            {
                _debuggerStopEventArgs = null;
            }
        }

        /// <summary>
        /// HandleBreakpointUpdated
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void HandleBreakpointUpdated(object sender, BreakpointUpdatedEventArgs e)
        {
            // Ignore if we are in restricted mode.
            if (!IsDebuggingSupported()) { return; }

            if (LocalDebugMode)
            {
                // Forward event locally.
                RaiseBreakpointUpdatedEvent(e);
                return;
            }

            try
            {
                // Forward event to remote client.
                Dbg.Assert(_runspace != null, "Runspace cannot be null.");
                _runspace.ExecutionContext.Events.GenerateEvent(
                    sourceIdentifier: RemoteDebugger.RemoteDebuggerBreakpointUpdatedEvent,
                    sender: null,
                    args: new object[] { e },
                    extraData: null);
            }
            catch (Exception ex)
            {
                CommandProcessor.CheckForSevereException(ex);
            }
        }

        private void HandleNestedDebuggingCancelEvent(object sender, EventArgs e)
        {
            // Forward cancel event from wrapped debugger.
            RaiseNestedDebuggingCancelEvent();

            // Release debugger.
            if (_inDebugMode)
            {
                ExitDebugMode(DebuggerResumeAction.Continue);
            }
        }

        /// <summary>
        /// Sends a DebuggerStop event to the client and enters a nested pipeline.
        /// </summary>
        private void EnterDebugMode(bool isNestedStop)
        {
            _inDebugMode = true;

            try
            {
                _runspace.ExecutionContext.SetVariable(SpecialVariables.NestedPromptCounterVarPath, 1);

                if (isNestedStop)
                {
                    // Blocking call for nested debugger execution (Workflow) stop events.
                    // The root debugger never makes two EnterDebugMode calls without an ExitDebugMode.
                    if (_nestedDebugStopCompleteEvent == null)
                    {
                        _nestedDebugStopCompleteEvent = new ManualResetEventSlim(false);
                    }
                    _nestedDebugging = true;
                    OnEnterDebugMode(_nestedDebugStopCompleteEvent);
                }
                else
                {
                    // Blocking call.
                    // Process all client commands as nested until nested pipeline is exited at
                    // which point this call returns.
                    _driverInvoker.EnterNestedPipeline();
                }
            }
            catch (Exception e)
            {
                CommandProcessor.CheckForSevereException(e);
            }
            finally
            {
                _inDebugMode = false;
                _nestedDebugging = false;
            }

            // Check to see if we should re-raise the stop event locally.
            if (_raiseStopEventLocally)
            {
                _raiseStopEventLocally = false;
                LocalDebugMode = true;
                HandleDebuggerStop(this, _debuggerStopEventArgs);
            }
        }

        /// <summary>
        /// Blocks DebugerStop event thread until exit debug mode is 
        /// received from the client.
        /// </summary>
        private void OnEnterDebugMode(ManualResetEventSlim debugModeCompletedEvent)
        {
            Dbg.Assert(!debugModeCompletedEvent.IsSet, "Event should always be non-signaled here.");

            while (true)
            {
                debugModeCompletedEvent.Wait();
                debugModeCompletedEvent.Reset();

                if (_threadCommandProcessing != null)
                {
                    // Process command.
                    _threadCommandProcessing.DoInvoke();
                    _threadCommandProcessing = null;
                }
                else
                {
                    // No command to process.  Exit debug mode.
                    break;
                }
            }
        }

        /// <summary>
        /// Exits the server side nested pipeline.
        /// </summary>
        private void ExitDebugMode(DebuggerResumeAction resumeAction)
        {
            _debuggerStopEventArgs.ResumeAction = resumeAction;

            try
            {
                if (_nestedDebugging)
                {
                    // Release nested debugger.
                    _nestedDebugStopCompleteEvent.Set();
                }
                else
                {
                    // Release EnterDebugMode blocking call.
                    _driverInvoker.ExitNestedPipeline();
                }

                _runspace.ExecutionContext.SetVariable(SpecialVariables.NestedPromptCounterVarPath, 0);
            }
            catch (Exception e)
            {
                CommandProcessor.CheckForSevereException(e);
            }
        }

        private void SubscribeWrappedDebugger(Debugger wrappedDebugger)
        {
            wrappedDebugger.DebuggerStop += HandleDebuggerStop; ;
            wrappedDebugger.BreakpointUpdated += HandleBreakpointUpdated; ;
            wrappedDebugger.NestedDebuggingCancelledEvent += HandleNestedDebuggingCancelEvent;
        }

        private void UnsubscribeWrappedDebugger(Debugger wrappedDebugger)
        {
            wrappedDebugger.DebuggerStop -= HandleDebuggerStop; ;
            wrappedDebugger.BreakpointUpdated -= HandleBreakpointUpdated; ;
            wrappedDebugger.NestedDebuggingCancelledEvent -= HandleNestedDebuggingCancelEvent;
        }

        private bool IsDebuggingSupported()
        {
            // Restriction only occurs on a (non-pushed) local runspace.
            LocalRunspace localRunspace = _runspace as LocalRunspace;
            if (localRunspace != null)
            {
                CmdletInfo cmdletInfo = localRunspace.ExecutionContext.EngineSessionState.GetCmdlet(SetPSBreakCommandText);
                if ((cmdletInfo != null) && (cmdletInfo.Visibility != SessionStateEntryVisibility.Public))
                {
                    return false;
                }
            }

            return true;
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// HandleStopSignal
        /// </summary>
        /// <returns>True if stop signal is handled.</returns>
        internal bool HandleStopSignal()
        {
            // If in pushed mode then stop any running command.
            if (IsPushed && (_threadCommandProcessing != null))
            {
                StopProcessCommand();
                return true;
            }

            // Set debug mode to "None" so that current command can stop and not
            // potentially hang in a debugger stop.  Use RestoreDebugger() to
            // restore debugger to original mode.
            _wrappedDebugger.Value.SetDebugMode(DebugModes.None);
            if (InBreakpoint)
            {
                try
                {
                    SetDebuggerAction(DebuggerResumeAction.Continue);
                }
                catch (PSInvalidOperationException) { }
            }

            return false;
        }

        // Sets the wrapped debugger to the same mode as the wrapper
        // server remote debugger, enabling it if remote debugging is enabled.
        internal void CheckDebuggerState()
        {
            if ((_wrappedDebugger.Value.DebugMode == DebugModes.None &&
                (DebugMode & DebugModes.RemoteScript) == DebugModes.RemoteScript))
            {
                _wrappedDebugger.Value.SetDebugMode(DebugMode);
            }
        }

        internal void StartPowerShellCommand(
            PowerShell powershell,
            Guid powershellId,
            Guid runspacePoolId,
            ServerRunspacePoolDriver runspacePoolDriver,
#if !CORECLR // No ApartmentState In CoreCLR
            ApartmentState apartmentState,
#endif
            ServerRemoteHost remoteHost,
            HostInfo hostInfo,
            RemoteStreamOptions streamOptions,
            bool addToHistory)
        {
            // For nested debugger command processing, invoke command on new local runspace since
            // the root script debugger runspace is unavailable (it is running a PS script or a 
            // workflow function script).
            Runspace runspace = (remoteHost != null) ?
                RunspaceFactory.CreateRunspace(remoteHost) : RunspaceFactory.CreateRunspace();

            runspace.Open();

            try
            {
                powershell.InvocationStateChanged += HandlePowerShellInvocationStateChanged;
                powershell.SetIsNested(false);

                string script = @"
                    param ($Debugger, $Commands, $output)
                    trap { throw $_ }
                    $Debugger.ProcessCommand($Commands, $output)
                    ";

                PSDataCollection<PSObject> output = new PSDataCollection<PSObject>();
                PSCommand Commands = new PSCommand(powershell.Commands);
                powershell.Commands.Clear();
                powershell.AddScript(script).AddParameter("Debugger", this).AddParameter("Commands", Commands).AddParameter("output", output);
                ServerPowerShellDriver driver = new ServerPowerShellDriver(
                    powershell,
                    null,
                    true,
                    powershellId,
                    runspacePoolId,
                    runspacePoolDriver,
#if !CORECLR // No ApartmentState In CoreCLR
                    apartmentState,
#endif
                    hostInfo,
                    streamOptions,
                    addToHistory,
                    runspace,
                    output);

                driver.Start();
            }
            catch (Exception e)
            {
                CommandProcessorBase.CheckForSevereException(e);

                runspace.Close();
                runspace.Dispose();
            }
        }

        private void HandlePowerShellInvocationStateChanged(object sender, PSInvocationStateChangedEventArgs e)
        {
            if (e.InvocationStateInfo.State == PSInvocationState.Completed ||
                e.InvocationStateInfo.State == PSInvocationState.Stopped ||
                e.InvocationStateInfo.State == PSInvocationState.Failed)
            {
                PowerShell powershell = sender as PowerShell;
                powershell.InvocationStateChanged -= HandlePowerShellInvocationStateChanged;

                Runspace runspace = powershell.GetRunspaceConnection() as Runspace;
                runspace.Close();
                runspace.Dispose();
            }
        }

        internal int GetBreakpointCount()
        {
            ScriptDebugger scriptDebugger = _wrappedDebugger.Value as ScriptDebugger;
            if (scriptDebugger != null)
            {
                return scriptDebugger.GetBreakpoints().Count;
            }
            else
            {
                return 0;
            }
        }

        internal void PushDebugger(Debugger debugger)
        {
            if (debugger == null)
            {
                return;
            }

            if (debugger.Equals(this))
            {
                throw new PSInvalidOperationException(DebuggerStrings.RemoteServerDebuggerCannotPushSelf);
            }

            if (_wrappedDebugger.IsOverridden)
            {
                throw new PSInvalidOperationException(DebuggerStrings.RemoteServerDebuggerAlreadyPushed);
            }

            // Swap wrapped debugger.
            UnsubscribeWrappedDebugger(_wrappedDebugger.Value);
            _wrappedDebugger.Override(debugger);
            SubscribeWrappedDebugger(_wrappedDebugger.Value);
        }

        internal void PopDebugger()
        {
            if (!_wrappedDebugger.IsOverridden) { return; }

            // Swap wrapped debugger.
            UnsubscribeWrappedDebugger(_wrappedDebugger.Value);
            _wrappedDebugger.Revert();
            SubscribeWrappedDebugger(_wrappedDebugger.Value);
        }

        internal void ReleaseAndRaiseDebugStopLocal()
        {
            if (_inDebugMode)
            {
                // Release debugger stop and signal to re-raise locally.
                _raiseStopEventLocally = true;
                ExitDebugMode(DebuggerResumeAction.Continue);
            }
        }

        #endregion

        #region Internal Properties

        /// <summary>
        /// When true, this debugger is being used for local debugging (not remote debugging)
        /// via the Debug-Runspace cmdlet.
        /// </summary>
        internal bool LocalDebugMode
        {
            get;
            set;
        }

        #endregion
    }
}
