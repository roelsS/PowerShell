/********************************************************************++
 * Copyright (c) Microsoft Corporation.  All rights reserved.
 * --********************************************************************/

using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Runtime.Serialization;
using System.Management.Automation.Host;
using System.Management.Automation.Remoting;
using Microsoft.PowerShell.Commands;
using System.Management.Automation.Internal;
using System.Diagnostics.CodeAnalysis; // for fxcop
using Dbg = System.Management.Automation.Diagnostics;
using System.Diagnostics;
using System.Linq;
using Microsoft.PowerShell.Telemetry.Internal;


#if CORECLR
// Use stubs for SerializableAttribute and ISerializable related types
using Microsoft.PowerShell.CoreClr.Stubs;
#endif

#pragma warning disable 1634, 1691 // Stops compiler from warning about unknown warnings

namespace System.Management.Automation.Runspaces
{
    /// <summary>
    /// Runspace class for local runspace
    /// </summary>
    internal sealed partial class LocalRunspace : RunspaceBase
    {
        #region constructors

        /// <summary>
        /// Construct an instance of an Runspace using a custom implementation 
        /// of PSHost.
        /// </summary>
        /// <param name="host">
        /// The explicit PSHost implementation
        /// </param>
        /// <param name="runspaceConfig">
        /// configuration information for this minshell.
        /// </param>
        [SuppressMessage("Microsoft.Maintainability", "CA1505:AvoidUnmaintainableCode")]
        internal LocalRunspace (PSHost host, RunspaceConfiguration runspaceConfig)
            : base( host,runspaceConfig)
        {
        }

        /// <summary>
        /// Construct an instance of an Runspace using a custom implementation 
        /// of PSHost.
        /// </summary>
        /// <param name="host">
        /// The explicit PSHost implementation
        /// </param>
        /// <param name="initialSessionState">
        /// configuration information for this minshell.
        /// </param>
        /// <param name="suppressClone">
        /// If true, don't make a copy of the initial session state object
        /// </param>
        [SuppressMessage("Microsoft.Maintainability", "CA1505:AvoidUnmaintainableCode")]
        internal LocalRunspace(PSHost host, InitialSessionState initialSessionState, bool suppressClone)
            : base(host, initialSessionState, suppressClone)
        {
        }

        /// <summary>
        /// Construct an instance of an Runspace using a custom implementation 
        /// of PSHost.
        /// </summary>
        /// <param name="host">
        /// The explicit PSHost implementation
        /// </param>
        /// <param name="initialSessionState">
        /// configuration information for this minshell.
        /// </param>
        [SuppressMessage("Microsoft.Maintainability", "CA1505:AvoidUnmaintainableCode")]
        internal LocalRunspace(PSHost host, InitialSessionState initialSessionState)
            : base(host, initialSessionState)
        {
        }
        #endregion  constructors

        /// <summary>
        /// Private data to be used by applications built on top of PowerShell.  
        /// 
        /// Local runspace pool is created with application private data set to an empty <see cref="PSPrimitiveDictionary"/>.
        /// 
        /// Runspaces that are part of a <see cref="RunspacePool"/> inherit application private data from the pool.
        /// </summary>
        public override PSPrimitiveDictionary GetApplicationPrivateData()
        {
            // if we didn't get applicationPrivateData from a runspace pool,
            // then we create a new one

            if (applicationPrivateData == null)
            {
                lock (this.SyncRoot)
                {
                    if (applicationPrivateData == null)
                    {
                        applicationPrivateData = new PSPrimitiveDictionary();
                    }
                }
            }

            return applicationPrivateData;
        }

        /// <summary>
        /// A method that runspace pools can use to propagate application private data into runspaces
        /// </summary>
        /// <param name="applicationPrivateData"></param>
        internal override void SetApplicationPrivateData(PSPrimitiveDictionary applicationPrivateData)
        {
            this.applicationPrivateData = applicationPrivateData;
        }

        private PSPrimitiveDictionary applicationPrivateData;

        /// <summary>
        /// Gets the event manager
        /// </summary>
        public override PSEventManager Events
        {
            get
            {
                System.Management.Automation.ExecutionContext context = this.GetExecutionContext;

                if (context == null)
                {
                    return null;
                }

                return context.Events;
            }
        }

        /// <summary>
        /// This property determines whether a new thread is create for each invocation
        /// </summary>
        /// <remarks>
        /// Any updates to the value of this property must be done before the Runspace is opened
        /// </remarks>
        /// <exception cref="InvalidRunspaceStateException">
        /// An attempt to change this property was made after opening the Runspace
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The thread options cannot be changed to the requested value
        /// </exception>
        public override PSThreadOptions ThreadOptions
        {
            get
            {
                return this.createThreadOptions;
            }

            set
            {
                lock (this.SyncRoot)
                {
                    if (value != this.createThreadOptions)
                    {
                        if (this.RunspaceStateInfo.State != RunspaceState.BeforeOpen)
                        {
#if CORECLR                 // No ApartmentState.STA Support In CoreCLR
                            bool allowed = value == PSThreadOptions.ReuseThread;
#else
                            // if the runspace is already opened we only allow changing the options if 
                            // the apartment state is MTA and the new value is ReuseThread
                            bool allowed = (this.ApartmentState == ApartmentState.MTA || this.ApartmentState == ApartmentState.Unknown) // Unknown is the same as MTA
                                           &&
                                           value == PSThreadOptions.ReuseThread;
#endif

                            if (!allowed)
                            {
                                throw new InvalidOperationException(StringUtil.Format(RunspaceStrings.InvalidThreadOptionsChange));
                            }
                        }

                        this.createThreadOptions = value;
                    }
                }
            }
        }
        private PSThreadOptions createThreadOptions = PSThreadOptions.Default;

        /// <summary>
        /// Resets the runspace state to allow for fast reuse. Not all of the runspace
        /// elements are reset. The goal is to minimize the chance of the user taking
        /// accidental dependencies on prior runspace state.
        /// </summary>
        public override void ResetRunspaceState()
        {
            PSInvalidOperationException invalidOperation = null;

            if (this.InitialSessionState == null)
            {
                invalidOperation = PSTraceSource.NewInvalidOperationException();

            }
            else if (this.RunspaceState != Runspaces.RunspaceState.Opened)
            {
                invalidOperation = PSTraceSource.NewInvalidOperationException(
                        RunspaceStrings.RunspaceNotInOpenedState, this.RunspaceState);
            }
            else if (this.RunspaceAvailability != Runspaces.RunspaceAvailability.Available)
            {
                invalidOperation = PSTraceSource.NewInvalidOperationException(
                        RunspaceStrings.ConcurrentInvokeNotAllowed);
            }

            if (invalidOperation != null)
            {
                invalidOperation.Source = "ResetRunspaceState";
                throw invalidOperation;
            }

            this.InitialSessionState.ResetRunspaceState(this.ExecutionContext);

            // Finally, reset history for this runspace. This needs to be done
            // last to so that changes to the default MaximumHistorySize will be picked up.
            _history = new History(this.ExecutionContext);
        }

        #region protected_methods

        /// <summary>
        /// Create a pipeline from a command string 
        /// </summary>
        /// <param name="command">A valid command string. Can be null</param>
        /// <param name="addToHistory">if true command is added to history</param>
        /// <param name="isNested">True for nested pipeline</param>
        /// <returns>
        /// A pipeline pre-filled with Commands specified in commandString.
        /// </returns>
        protected override Pipeline CoreCreatePipeline (string command, bool addToHistory, bool isNested)
        {
            // NTRAID#Windows Out Of Band Releases-915851-2005/09/13
            if (_disposed)
            {
                throw PSTraceSource.NewObjectDisposedException("runspace");
            }

            return (Pipeline)new LocalPipeline(this, command, addToHistory, isNested);
        }

        #endregion protected_methods

        #region protected_properties

        /// <summary>
        /// Gets the execution context
        /// </summary>
        internal override System.Management.Automation.ExecutionContext GetExecutionContext
        {
            get
            {
                if (_engine == null)
                    return null;
                else
                    return _engine.Context;
            }
        }

        /// <summary>
        /// Returns true if the internal host is in a nested prompt
        /// </summary>
        internal override bool InNestedPrompt
        {
            get
            {
                System.Management.Automation.ExecutionContext context = this.GetExecutionContext;

                if (context == null)
                {
                    return false;
                }

                return context.InternalHost.HostInNestedPrompt();
            }
        }

        #endregion protected_properties

        #region internal_properties
        /// <summary>
        /// CommandFactory object for this runspace.
        /// </summary>
        internal CommandFactory CommandFactory
        {
            get
            {
                return _commandFactory;
            }
        }

        /// <summary>
        /// Gets history manager for this runspace
        /// </summary>
        /// <value></value>
        internal History History
        {
            get
            {
                return _history;
            }
        }

        /// <summary>
        /// Gets transcription data for this runspace
        /// </summary>
        /// <value></value>
        internal TranscriptionData TranscriptionData
        {
            get
            {
                return _transcriptionData;
            }
        }
        TranscriptionData _transcriptionData = null;


        JobRepository _jobRepository;
        /// <summary>
        /// List of jobs in this runspace
        /// </summary>
        internal JobRepository JobRepository
        {
            get
            {
                return _jobRepository;
            }
        }

        JobManager _jobManager;

        /// <summary>
        /// Manager for JobSourceAdapters registered in this runspace.
        /// </summary>
        public override JobManager  JobManager
        {
            get
            {
                return _jobManager;
            }
        }

        RunspaceRepository _runspaceRepository;

        /// <summary>
        /// List of remote runspaces in this runspace
        /// </summary>
        internal RunspaceRepository RunspaceRepository
        {
            get
            {
                return _runspaceRepository;
            }
        }

        #endregion internal_properties

        #region Debugger

        /// <summary>
        /// Debugger
        /// </summary>
        public override Debugger Debugger
        {
            get
            {
                return InternalDebugger ?? base.Debugger;
            }
        }

        private static string DebugPreferenceCachePath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsPowerShell"), "DebugPreference.clixml");
        private static object DebugPreferenceLockObject = new object();

        /// <summary>
        /// DebugPreference serves as a property bag to keep 
        /// track of all process specific debug preferences.
        /// </summary>
        public class DebugPreference
        {
            public string[] AppDomainNames;
        }

        /// <summary>
        /// CreateDebugPerfStruct is a helper method to populate DebugPreference
        /// </summary>
        /// <param name="AppDomainNames">App Domain Names</param>
        /// <returns>DebugPreference</returns>
        private static DebugPreference CreateDebugPreference(string[] AppDomainNames)
        {
            DebugPreference DebugPreference = new DebugPreference();
            DebugPreference.AppDomainNames = AppDomainNames;
            return DebugPreference;
        }

        /// <summary>
        /// SetDebugPreference is a helper method used to enable and disable debug preference.
        /// </summary>
        /// <param name="processName">Process Name</param>
        /// <param name="appDomainName">App Domain Name</param>
        /// <param name="enable">Indicates if the debug preference has to be enabled or disabled.</param>
        internal static void SetDebugPreference(string processName, List<string> appDomainName, bool enable)
        {
            lock(DebugPreferenceLockObject)
            {
                bool iscacheUpdated = false;
                Hashtable debugPreferenceCache = null;

                string[] appDomainNames = null;
                if (appDomainName != null)
                {
                    appDomainNames = appDomainName.ToArray();
                }

                if (!File.Exists(LocalRunspace.DebugPreferenceCachePath))
                {
                    if (enable)
                    {
                        DebugPreference DebugPreference = CreateDebugPreference(appDomainNames);
                        debugPreferenceCache = new Hashtable();
                        debugPreferenceCache.Add(processName, DebugPreference);
                        iscacheUpdated = true;
                    }
                }
                else
                {
                    debugPreferenceCache = GetDebugPreferenceCache(null);
                    if (debugPreferenceCache != null)
                    {
                        if (enable)
                        { 
                            // Debug preference is set to enable.
                            // If the cache does not contain the process name, then we just update the cache.
                            if (!debugPreferenceCache.ContainsKey(processName))
                            {
                                DebugPreference DebugPreference = CreateDebugPreference(appDomainNames);
                                debugPreferenceCache.Add(processName, DebugPreference);
                                iscacheUpdated = true;
                            }
                            else
                            {
                                // In this case, the cache contains the process name, hence we check the list of 
                                // app domains for which the debug preference is set to enable.
                                DebugPreference processDebugPreference = GetProcessSpecificDebugPreference(debugPreferenceCache[processName]);

                                // processDebugPreference would point to null if debug preference is enabled for all app domains.
                                // If processDebugPreference is not null then it means that user has selected specific 
                                // appdomins for which the debug preference has to be enabled.
                                if (processDebugPreference != null)
                                {
                                    List<string> chachedAppDomainNames = null;
                                    if (processDebugPreference.AppDomainNames != null && processDebugPreference.AppDomainNames.Length > 0)
                                    {
                                        chachedAppDomainNames = new List<string>(processDebugPreference.AppDomainNames);

                                        foreach (string currentAppDomainName in appDomainName)
                                        {
                                            if (!chachedAppDomainNames.Contains(currentAppDomainName, StringComparer.OrdinalIgnoreCase))
                                            {
                                                chachedAppDomainNames.Add(currentAppDomainName);
                                                iscacheUpdated = true;
                                            }
                                        }
                                    }

                                    if (iscacheUpdated)
                                    {
                                        DebugPreference DebugPreference = CreateDebugPreference(chachedAppDomainNames.ToArray());
                                        debugPreferenceCache[processName] = DebugPreference;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Debug preference is set to disable.
                            if (debugPreferenceCache.ContainsKey(processName))
                            {
                                if (appDomainName == null)
                                {
                                    debugPreferenceCache.Remove(processName);
                                    iscacheUpdated = true;
                                }
                                else
                                {
                                    DebugPreference processDebugPreference = GetProcessSpecificDebugPreference(debugPreferenceCache[processName]);

                                    // processDebugPreference would point to null if debug preference is enabled for all app domains.
                                    // If processDebugPreference is not null then it means that user has selected specific 
                                    // appdomins for which the debug preference has to be enabled.
                                    if (processDebugPreference != null)
                                    {
                                        List<string> chachedAppDomainNames = null;
                                        if (processDebugPreference.AppDomainNames != null && processDebugPreference.AppDomainNames.Length > 0)
                                        {
                                            chachedAppDomainNames = new List<string>(processDebugPreference.AppDomainNames);

                                            foreach (string currentAppDomainName in appDomainName)
                                            {
                                                if (chachedAppDomainNames.Contains(currentAppDomainName, StringComparer.OrdinalIgnoreCase))
                                                {
                                                    // remove requested appdomains debug preference details. 
                                                    chachedAppDomainNames.Remove(currentAppDomainName);
                                                    iscacheUpdated = true;
                                                }
                                            }
                                        }

                                        if (iscacheUpdated)
                                        {
                                            DebugPreference DebugPreference = CreateDebugPreference(chachedAppDomainNames.ToArray());
                                            debugPreferenceCache[processName] = DebugPreference;
                                        }
                                    }
                                }

                            }
                        }
                    }
                    else
                    {
                        // For wahtever reason, cache is corrupted. Hence override the cache content.
                        if (enable)
                        {
                            debugPreferenceCache = new Hashtable();
                            DebugPreference DebugPreference = CreateDebugPreference(appDomainNames);
                            debugPreferenceCache.Add(processName, DebugPreference);
                            iscacheUpdated = true;
                        }
                    }
                }

                if (iscacheUpdated)
                {
                    using (PowerShell ps = PowerShell.Create())
                    {
                        ps.AddCommand("Export-Clixml").AddParameter("Path", LocalRunspace.DebugPreferenceCachePath).AddParameter("InputObject", debugPreferenceCache);
                        ps.Invoke();
                    }
                }
            }
        }

        /// <summary>
        /// GetDebugPreferenceCache is a helper method used to fetch 
        /// the debug preference cache contents as a Hashtable.
        /// </summary>
        /// <param name="runspace">Runspace</param>
        /// <returns>If the Debug preference is persisted then a hashtable containing 
        /// the debug preference is returned or else Null is returned.</returns>
        private static Hashtable GetDebugPreferenceCache(Runspace runspace)
        {
            Hashtable debugPreferenceCache = null;
            using (PowerShell ps = PowerShell.Create())
            {
                if (runspace != null)
                {
                    ps.Runspace = runspace;
                }

                ps.AddCommand("Import-Clixml").AddParameter("Path", LocalRunspace.DebugPreferenceCachePath);
                Collection<PSObject> psObjects = ps.Invoke();

                if (psObjects != null && psObjects.Count == 1)
                {
                    debugPreferenceCache = psObjects[0].BaseObject as Hashtable;
                }
            }
            return debugPreferenceCache;
        }

        /// <summary>
        /// GetProcessSpecificDebugPreference is a helper method used to fetch persisted process specific debug preference.
        /// </summary>
        /// <param name="debugPreference"></param>
        /// <returns></returns>
        private static DebugPreference GetProcessSpecificDebugPreference(object debugPreference)
        {
            DebugPreference processDebugPreference = null;
            if(debugPreference != null)
            {
                PSObject debugPreferencePsObject = debugPreference as PSObject;
                if (debugPreferencePsObject != null)
                {
                    processDebugPreference = LanguagePrimitives.ConvertTo<DebugPreference>(debugPreferencePsObject);
                }   
            }
            return processDebugPreference;
        }

        #endregion

        /// <summary>
        /// Open the runspace
        /// </summary>
        /// <param name="syncCall">
        /// paramter which control if Open is done synchronously or asynchronously
        /// </param>
        override protected void OpenHelper(bool syncCall)
        {
            if (syncCall)
            {
                //Open runspace synchronously
                DoOpenHelper();
            }
            else
            {
                //Open runspace in another thread
                Thread asyncThread = new Thread(new ThreadStart(this.OpenThreadProc));

                asyncThread.Start();
            }
        }

        /// <summary>
        /// Start method for asynchronous open
        /// </summary>
        private void OpenThreadProc()
        {
#pragma warning disable 56500
            try
            {
                DoOpenHelper();
            }
            catch (Exception exception) // ignore non-severe exceptions
            {
                CommandProcessorBase.CheckForSevereException(exception);
                //This exception is reported by raising RunspaceState
                //change event.
            }
#pragma warning restore 56500
        }

        /// <summary>
        /// Helper function used for opening a runspace
        /// </summary>
        void DoOpenHelper ()
        {
            // NTRAID#Windows Out Of Band Releases-915851-2005/09/13
            if (_disposed)
            {
                throw PSTraceSource.NewObjectDisposedException("runspace");
            }

            bool startLifeCycleEventWritten = false;
            runspaceInitTracer.WriteLine("begin open runspace");
            try
            {
                _transcriptionData = new TranscriptionData();

                if (InitialSessionState != null)
                {
                    // All ISS-based configuration of the engine itself is done by AutomationEngine,
                    // which calls InitialSessionState.Bind(). Anything that doesn't
                    // require an active and open runspace should be done in ISS.Bind()
                    _engine = new AutomationEngine(Host, null, InitialSessionState);
                }
                else
                {
                    _engine = new AutomationEngine(Host, RunspaceConfiguration, null);
                }
                _engine.Context.CurrentRunspace = this;

                //Log engine for start of engine life
                MshLog.LogEngineLifecycleEvent(_engine.Context, EngineState.Available);
                startLifeCycleEventWritten = true;

                _commandFactory = new CommandFactory(_engine.Context);
                _history = new History(_engine.Context);
                _jobRepository = new JobRepository();
                _jobManager = new JobManager();
                _runspaceRepository = new RunspaceRepository();

                runspaceInitTracer.WriteLine("initializing built-in aliases and variable information");
                InitializeDefaults();
            }
            catch (Exception exception)
            {
                CommandProcessorBase.CheckForSevereException(exception);
                runspaceInitTracer.WriteLine("Runspace open failed");

                //Log engine health event
                LogEngineHealthEvent(exception);

                //Log engine for end of engine life
                if (startLifeCycleEventWritten)
                {
                    Dbg.Assert(_engine.Context != null, "if startLifeCycleEventWritten is true, ExecutionContext must be present");
                    MshLog.LogEngineLifecycleEvent(_engine.Context, EngineState.Stopped);
                }

                //Open failed. Set the RunspaceState to Broken.
                SetRunspaceState (RunspaceState.Broken, exception);

                //Raise the event
                RaiseRunspaceStateEvents ();

                //Rethrow the exception. For asynchronous execution, 
                //OpenThreadProc will catch it. For synchronous execution
                //caller of open will catch it.
                throw ;
            }

            SetRunspaceState (RunspaceState.Opened);
            RunspaceOpening.Set();

            //Raise the event
            RaiseRunspaceStateEvents() ;
            runspaceInitTracer.WriteLine("runspace opened successfully");

            // Now do initial state configuration that requires an active runspace
            if (InitialSessionState != null)
            {
                Exception initError = InitialSessionState.BindRunspace(this, runspaceInitTracer);
                if(initError != null)
                {
                    // Log engine health event
                    LogEngineHealthEvent(initError);

                    // Log engine for end of engine life
                    Debug.Assert(_engine.Context != null,
                                "if startLifeCycleEventWritten is true, ExecutionContext must be present");
                    MshLog.LogEngineLifecycleEvent(_engine.Context, EngineState.Stopped);

                    // Open failed. Set the RunspaceState to Broken.
                    SetRunspaceState(RunspaceState.Broken, initError);

                    // Raise the event
                    RaiseRunspaceStateEvents();

                    // Throw the exception. For asynchronous execution, 
                    // OpenThreadProc will catch it. For synchronous execution
                    // caller of open will catch it.
                    throw initError;
                }
            }

            TelemetryAPI.ReportLocalSessionCreated(InitialSessionState, TranscriptionData);
        }

        /// <summary>
        /// Logs engine health event
        /// </summary>
        internal void LogEngineHealthEvent(Exception exception)
        {
            LogEngineHealthEvent(
                exception,
                Severity.Error,
                MshLog.EVENT_ID_CONFIGURATION_FAILURE,
                null);
        }

        /// <summary>
        /// Logs engine health event
        /// </summary>
        internal void LogEngineHealthEvent(Exception exception,
                             Severity severity,
                             int id,
                             Dictionary<String, String> additionalInfo)
        {
            Dbg.Assert(exception != null, "Caller should validate the parameter");

            LogContext logContext = new LogContext();
            logContext.EngineVersion = Version.ToString();
            logContext.HostId = Host.InstanceId.ToString();
            logContext.HostName = Host.Name;
            logContext.HostVersion = Host.Version.ToString();
            logContext.RunspaceId = InstanceId.ToString();
            logContext.Severity = severity.ToString();
            if (this.RunspaceConfiguration == null)
            {
                logContext.ShellId = Utils.DefaultPowerShellShellID;
            }
            else
            {
                logContext.ShellId = this.RunspaceConfiguration.ShellId;
            }
            MshLog.LogEngineHealthEvent(
                logContext,
                id,
                exception,
                additionalInfo);
        }

        /// <summary>
        /// Returns the thread that must be used to execute pipelines when CreateThreadOptions is ReuseThread
        /// </summary>
        /// <remarks>
        /// The pipeline calls this function after ensuring there is a single thread in the pipeline, so no locking is neccesary
        /// </remarks>
        internal PipelineThread GetPipelineThread()
        {
            if (this.pipelineThread == null)
            {
#if CORECLR     // No ApartmentState In CoreCLR
                this.pipelineThread = new PipelineThread();
#else
                this.pipelineThread = new PipelineThread(this.ApartmentState);
#endif
            }

            return this.pipelineThread;
        }

        private PipelineThread pipelineThread = null;

        override protected void CloseHelper(bool syncCall)
        {
            if (syncCall)
            {
                //Do close synchronously
                DoCloseHelper ();
            }
            else
            {
                //Do close asynchronously
                Thread asyncThread = new Thread (new ThreadStart(this.CloseThreadProc));

                asyncThread.Start ();
            }
        }

        /// <summary>
        /// Start method for asynchronous close
        /// </summary>
        private void CloseThreadProc ()
        {
#pragma warning disable 56500
            try
            {
                DoCloseHelper ();
            }
            catch (Exception exception) // ignore non-severe exceptions
            {
                CommandProcessorBase.CheckForSevereException(exception);
            }
#pragma warning restore 56500
        }

        /// <summary>
        /// Close the runspace.
        /// </summary>
        /// <remarks>
        /// Attempts to create/execute pipelines after a call to 
        /// close will fail.
        /// </remarks>
        void DoCloseHelper ()
        {
            // Stop any transcription if we're the last runspace to exit
            ExecutionContext executionContext = this.GetExecutionContext;
            if (executionContext != null)
            {
                Runspace hostRunspace = null;
                try
                {
                    hostRunspace = executionContext.EngineHostInterface.Runspace;
                }
                catch (PSNotImplementedException)
                {
                    // EngineHostInterface.Runspace throws PSNotImplementedException if there
                    // is no interactive host.
                }

                if ((hostRunspace == null) || (this == hostRunspace))
                {
                    PSHostUserInterface host = executionContext.EngineHostInterface.UI;
                    if (host != null)
                    {
                        host.StopAllTranscribing();
                    }
                }
            }

            // Generate the shutdown event
            if (Events != null)
                Events.GenerateEvent(PSEngineEvent.Exiting, null, new object[] { }, null, true, false);

            //Stop all running pipelines

            //Note:Do not perform the Cancel in lock. Reason is 
            //Pipeline executes in separate thread, say threadP. 
            //When pipeline is canceled/failed/completed in 
            //Pipeline.ExecuteThreadProc it removes the pipeline 
            //from the list of running pipelines. threadP will need
            //lock to remove the pipelines from the list of running pipelines
            //And we will deadlock.
            //Note:It is possible that one or more pipelines in the list
            //of active pipelines have completed before we call cancel.
            //That is fine since Pipeline.Cancel handles that( It ignores
            //the cancel request if pipeline execution has already
            //completed/failed/canceled.
            StopPipelines();

            // Disconnect all disconnectable jobs in the job repository.
            StopOrDisconnectAllJobs();

            // Close or disconnect all the remote runspaces available in the 
            // runspace repository.
            CloseOrDisconnectAllRemoteRunspaces(() =>
                {
                    List<RemoteRunspace> runspaces = new List<RemoteRunspace>();
                    foreach (PSSession psSession in this.RunspaceRepository.Runspaces)
                    {
                        runspaces.Add(psSession.Runspace as RemoteRunspace);
                    }

                    return runspaces;
                });

            //Notify Engine components that that runspace is closing. 
            _engine.Context.RunspaceClosingNotification();

            //Log engine lifecycle event.
            MshLog.LogEngineLifecycleEvent(_engine.Context, EngineState.Stopped);

            // Uninitialize the AMSI scan interface
            AmsiUtils.Uninitialize();

            //All pipelines have been canceled. Close the runspace.               
            _engine = null;
            _commandFactory = null;

            SetRunspaceState (RunspaceState.Closed);

            //Raise Event
            RaiseRunspaceStateEvents ();

            // Report telemetry if we have no more open runspaces.
            bool allRunspacesClosed = true;
            bool hostProvidesExitTelemetry = false;
            foreach (var r in Runspace.RunspaceList)
            {
                if (r.RunspaceStateInfo.State != RunspaceState.Closed)
                {
                    allRunspacesClosed = false;
                    break;
                }
                var localRunspace = r as LocalRunspace;
                if (localRunspace != null && localRunspace.Host is IHostProvidesTelemetryData)
                {
                    hostProvidesExitTelemetry = true;
                    break;
                }
            }
            if (allRunspacesClosed && !hostProvidesExitTelemetry)
            {
                TelemetryAPI.ReportExitTelemetry(null);
            }
        }

        /// <summary>
        /// Closes or disconnects all the remote runspaces passed in by the getRunspace
        /// function.  If a remote runspace supports disconnect then it will be disconnected 
        /// rather than closed.
        /// </summary>
        private void CloseOrDisconnectAllRemoteRunspaces(Func<List<RemoteRunspace>> getRunspaces)
        {
            List<RemoteRunspace> runspaces = getRunspaces();
            if (runspaces.Count == 0) { return; }

            // whether the close of all remoterunspaces completed
            using (ManualResetEvent remoteRunspaceCloseCompleted = new ManualResetEvent(false))
            {
                ThrottleManager throttleManager = new ThrottleManager();
                throttleManager.ThrottleComplete += delegate(object sender, EventArgs e)
                {
                    remoteRunspaceCloseCompleted.Set();
                };

                foreach (RemoteRunspace remoteRunspace in runspaces)
                {
                    IThrottleOperation operation = new CloseOrDisconnectRunspaceOperationHelper(remoteRunspace);
                    throttleManager.AddOperation(operation);
                }
                throttleManager.EndSubmitOperations();

                remoteRunspaceCloseCompleted.WaitOne();
            }
        }

        /// <summary>
        /// Disconnects all disconnectable jobs listed in the JobRepository.
        /// </summary>
        private void StopOrDisconnectAllJobs()
        {
            if (JobRepository.Jobs.Count == 0) { return; }
            List<RemoteRunspace> disconnectRunspaces = new List<RemoteRunspace>();

            using (ManualResetEvent jobsStopCompleted = new ManualResetEvent(false))
            {
                ThrottleManager throttleManager = new ThrottleManager();
                throttleManager.ThrottleComplete += delegate(object sender, EventArgs e)
                {
                    jobsStopCompleted.Set();
                };

                foreach (Job job in this.JobRepository.Jobs)
                {
                    // Only stop or disconnect PowerShell jobs.
                    if (job is PSRemotingJob == false)
                    {
                        continue;
                    }

                    if (!job.CanDisconnect)
                    {
                        // If the job cannot be disconnected then add it to
                        // the stop list.
                        throttleManager.AddOperation(new StopJobOperationHelper(job));
                    }
                    else if (job.JobStateInfo.State == JobState.Running)
                    {
                        // Otherwise add disconnectable runspaces to list so that
                        // they can be disconnected.
                        IEnumerable<RemoteRunspace> jobRunspaces = job.GetRunspaces();
                        if (jobRunspaces != null)
                        {
                            disconnectRunspaces.AddRange(jobRunspaces);
                        }
                    }
                } // foreach job

                // Stop jobs.
                throttleManager.EndSubmitOperations();
                jobsStopCompleted.WaitOne();

            } // using jobsStopCompleted

            // Disconnect all disconnectable job runspaces found.
            CloseOrDisconnectAllRemoteRunspaces(() =>
                {
                    return disconnectRunspaces;
                });
        }

        internal void ReleaseDebugger()
        {
            Debugger debugger = Debugger;
            if (debugger != null)
            {
                try
                {
                    if (debugger.UnhandledBreakpointMode == UnhandledBreakpointProcessingMode.Wait)
                    {
                        // Sets the mode and also releases a held debug stop.
                        debugger.UnhandledBreakpointMode = UnhandledBreakpointProcessingMode.Ignore;
                    }
                }
                catch (PSNotImplementedException) { }
            }
        }

        #region SessionStateProxy

        protected override void DoSetVariable(string name, object value)
        {
            // NTRAID#Windows Out Of Band Releases-915851-2005/09/13
            if (_disposed)
            {
                throw PSTraceSource.NewObjectDisposedException("runspace");
            }

            _engine.Context.EngineSessionState.SetVariableValue(name, value, CommandOrigin.Internal);
        }

        protected override object DoGetVariable(string name)
        {
            // NTRAID#Windows Out Of Band Releases-915851-2005/09/13
            if (_disposed)
            {
                throw PSTraceSource.NewObjectDisposedException("runspace");
            }

            return _engine.Context.EngineSessionState.GetVariableValue(name);
        }

        protected override List<string> DoApplications
        {
            get
            {
                if (_disposed)
                {
                    throw PSTraceSource.NewObjectDisposedException("runspace");
                }

                return _engine.Context.EngineSessionState.Applications;
            }
        }

        protected override List<string> DoScripts
        {
            get
            {
                if (_disposed)
                {
                    throw PSTraceSource.NewObjectDisposedException("runspace");
                }

                return _engine.Context.EngineSessionState.Scripts;
            }
        }

        protected override DriveManagementIntrinsics DoDrive
        {
            get
            {
                if (_disposed)
                {
                    throw PSTraceSource.NewObjectDisposedException("runspace");
                }

                return _engine.Context.SessionState.Drive;
            }
        }

        protected override PSLanguageMode DoLanguageMode
        {
            get
            {
                if (_disposed)
                {
                    throw PSTraceSource.NewObjectDisposedException("runspace");
                }

                return _engine.Context.SessionState.LanguageMode;
            }
            set
            {
                if (_disposed)
                {
                    throw PSTraceSource.NewObjectDisposedException("runspace");
                }

                _engine.Context.SessionState.LanguageMode = value;
            }
        }

        protected override PSModuleInfo DoModule
        {
            get
            {
                if (_disposed)
                {
                    throw PSTraceSource.NewObjectDisposedException("runspace");
                }

                return _engine.Context.EngineSessionState.Module;
            }
        }

        protected override PathIntrinsics DoPath
        {
            get
            {
                if (_disposed)
                {
                    throw PSTraceSource.NewObjectDisposedException("runspace");
                }

                return _engine.Context.SessionState.Path;
            }
        }

        protected override CmdletProviderManagementIntrinsics DoProvider
        {
            get
            {
                if (_disposed)
                {
                    throw PSTraceSource.NewObjectDisposedException("runspace");
                }

                return _engine.Context.SessionState.Provider;
            }
        }

        protected override PSVariableIntrinsics DoPSVariable
        {
            get
            {
                if (_disposed)
                {
                    throw PSTraceSource.NewObjectDisposedException("runspace");
                }

                return _engine.Context.SessionState.PSVariable;
            }
        }

        protected override CommandInvocationIntrinsics DoInvokeCommand
        {
            get
            {
                if (_disposed)
                {
                    throw PSTraceSource.NewObjectDisposedException("runspace");
                }

                return _engine.Context.EngineIntrinsics.InvokeCommand;
            }
        }

        protected override ProviderIntrinsics DoInvokeProvider
        {
            get
            {
                if (_disposed)
                {
                    throw PSTraceSource.NewObjectDisposedException("runspace");
                }

                return _engine.Context.EngineIntrinsics.InvokeProvider;
            }
        }


        #endregion SessionStateProxy

        #region IDisposable Members

        /// <summary>
        /// Set to true when object is disposed
        /// </summary>
        bool _disposed;

        /// <summary>
        /// Protected dispose which can be overridden by derived classes.
        /// </summary>
        /// <param name="disposing"></param>
        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "pipelineThread", Justification = "pipelineThread is disposed in Close()")]
        protected override void Dispose(bool disposing)
        {
            try
            {
                if (_disposed)
                {
                    return;
                }
                lock (SyncRoot)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                }

                if (disposing)
                {
                    Close();
                    _engine = null;
                    _history = null;
                    _transcriptionData = null;
                    _jobManager = null;
                    _jobRepository = null;
                    _runspaceRepository = null;
                    if (RunspaceOpening != null)
                    {
                        RunspaceOpening.Dispose();
                        RunspaceOpening = null;
                    }

                    // Dispose the event manager
                    if (this.ExecutionContext != null && this.ExecutionContext.Events != null)
                    {
                        try
                        {
                            this.ExecutionContext.Events.Dispose();
                        }
                        catch (ObjectDisposedException)
                        {
                            ;
                        }
                    }
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Close the runspace
        /// </summary>
        override public void Close()
        {
            // Do not put cleanup activities in here, as they aren't
            // captured in CloseAsync() case. Instead, put them in
            // DoCloseHelper()

            base.Close(); // call base.Close() first to make it stop the pipeline

            if (this.pipelineThread != null)
            {
                this.pipelineThread.Close();
            }
        }

        #endregion IDisposable Members

        #region private fields

        /// <summary>
        /// CommandFactory for creating Command objects
        /// </summary>
        private CommandFactory _commandFactory;

        /// <summary>
        /// AutomationEngine instance for this runspace
        /// </summary>
        private AutomationEngine _engine;

        internal AutomationEngine Engine
        {
            get
            {
                return _engine;
            }
        }

        /// <summary>
        /// Manages history for this runspace
        /// </summary>
        History _history;

        [TraceSource("RunspaceInit", "Initialization code for Runspace")]
        private static
        PSTraceSource runspaceInitTracer =
            PSTraceSource.GetTracer("RunspaceInit", "Initialization code for Runspace", false);

        /// <summary>
        /// This ensures all processes have a server/listener.
        /// </summary>
        private static RemoteSessionNamedPipeServer IPCNamedPipeServer = RemoteSessionNamedPipeServer.IPCNamedPipeServer;

        #endregion private fields
    }

    #region Helper Class

    /// <summary>
    /// Helper class to stop a running job.
    /// </summary>
    internal sealed class StopJobOperationHelper : IThrottleOperation
    {
        private Job job;

        /// <summary>
        /// Internal constructor
        /// </summary>
        /// <param name="job">Job object to stop.</param>
        internal StopJobOperationHelper(Job job)
        {
            this.job = job;
            this.job.StateChanged += new EventHandler<JobStateEventArgs>(HandleJobStateChanged);
        }

        /// <summary>
        /// Handles the Job state change event.
        /// </summary>
        /// <param name="sender">Originator of event, unused</param>
        /// <param name="eventArgs">Event arguments containing Job state.</param>
        private void HandleJobStateChanged(object sender, JobStateEventArgs eventArgs)
        {
            if (job.IsFinishedState(job.JobStateInfo.State))
            {
                // We are done when the job is in the finished state.
                RaiseOperationCompleteEvent();
            }
        }

        /// <summary>
        /// Override method to start the operation.
        /// </summary>
        internal override void StartOperation()
        {
            if (job.IsFinishedState(job.JobStateInfo.State))
            {
                // The job is already in the finished state and so cannot be stopped.
                RaiseOperationCompleteEvent();
            }
            else
            {
                // Otherwise stop the job.
                job.StopJob();
            }
        }

        /// <summary>
        /// Override method to stop the operation.  Not used, stop operation must
        /// run to completion.
        /// </summary>
        internal override void StopOperation()
        {
        }

        /// <summary>
        /// Event to signal ThrottleManager when the operation is complete.
        /// </summary>
        internal override event EventHandler<OperationStateEventArgs> OperationComplete;

        /// <summary>
        /// Raise the OperationComplete event.
        /// </summary>
        private void RaiseOperationCompleteEvent()
        {
            this.job.StateChanged -= new EventHandler<JobStateEventArgs>(HandleJobStateChanged);

            OperationStateEventArgs operationStateArgs = new OperationStateEventArgs();
            operationStateArgs.OperationState = OperationState.StartComplete;
            operationStateArgs.BaseEvent = EventArgs.Empty;

            OperationComplete.SafeInvoke(this, operationStateArgs);
        }
    }
 
    /// <summary>
    /// Helper class to disconnect a runspace if the runspace supports disconnect
    /// semantics or otherwise close the runspace.
    /// </summary>
    internal sealed class CloseOrDisconnectRunspaceOperationHelper : IThrottleOperation
    {
        private RemoteRunspace remoteRunspace;

        /// <summary>
        /// Internal constructor
        /// </summary>
        /// <param name="remoteRunspace"></param>
        internal CloseOrDisconnectRunspaceOperationHelper(RemoteRunspace remoteRunspace)
        {
            this.remoteRunspace = remoteRunspace;
            this.remoteRunspace.StateChanged += new EventHandler<RunspaceStateEventArgs>(HandleRunspaceStateChanged);
        }

        /// <summary>
        /// Handle the runspace state changed event
        /// </summary>
        /// <param name="sender">sender of this information, unused</param>
        /// <param name="eventArgs">runspace event args</param>
        private void HandleRunspaceStateChanged(object sender, RunspaceStateEventArgs eventArgs)
        {
            switch (eventArgs.RunspaceStateInfo.State)
            {
                case RunspaceState.BeforeOpen:
                case RunspaceState.Closing:
                case RunspaceState.Opened:
                case RunspaceState.Opening:
                case RunspaceState.Disconnecting:
                    return;
            }

            //remoteRunspace.Dispose();
            //remoteRunspace = null;
            RaiseOperationCompleteEvent();
        }

        /// <summary>
        /// Start the operation of closing the runspace
        /// </summary>
        internal override void StartOperation()
        {
            if (remoteRunspace.RunspaceStateInfo.State == RunspaceState.Closed ||
                remoteRunspace.RunspaceStateInfo.State == RunspaceState.Broken ||
                remoteRunspace.RunspaceStateInfo.State == RunspaceState.Disconnected)
            {
                // If the runspace is currently in a disconnected state then leave it
                // as is.

                // in this case, calling a close won't raise any events. Simply raise 
                // the OperationCompleted event. After the if check, but before we
                // get to this point if the state was changed, then the StateChanged 
                // event handler will anyway raise the event and so we are fine
                RaiseOperationCompleteEvent();
            }
            else
            {
                // If the runspace supports disconnect semantics and is running a command, 
                // then disconnect it rather than closing it.
                if (remoteRunspace.CanDisconnect &&
                    remoteRunspace.GetCurrentlyRunningPipeline() != null)
                {
                    remoteRunspace.DisconnectAsync();
                }
                else
                {
                    remoteRunspace.CloseAsync();
                }
            }
        }

        /// <summary>
        /// There is no scenario where we are going to cancel this close
        /// Hence this method is intentionally empty
        /// </summary>
        internal override void StopOperation()
        {

        }

        /// <summary>
        /// Event raised when the required operation is complete
        /// </summary>
        internal override event EventHandler<OperationStateEventArgs> OperationComplete;

        /// <summary>
        /// Raise the operation completed event
        /// </summary>
        private void RaiseOperationCompleteEvent()
        {
            this.remoteRunspace.StateChanged -= new EventHandler<RunspaceStateEventArgs>(HandleRunspaceStateChanged);

            OperationStateEventArgs operationStateEventArgs =
                    new OperationStateEventArgs();
            operationStateEventArgs.OperationState =
                    OperationState.StartComplete;
            operationStateEventArgs.BaseEvent = EventArgs.Empty;

            OperationComplete.SafeInvoke(this, operationStateEventArgs);
        }
    }

    /// <summary>
    /// Defines the exception thrown an error loading modules occurs while opening the runspace. It
    /// contains a list of all of the module errors that have occurred
    /// </summary>
    [Serializable]
    public class RunspaceOpenModuleLoadException : RuntimeException
    {
        #region ctor

        /// <summary>
        /// Initializes a new instance of ScriptBlockToPowerShellNotSupportedException
        /// with the message set to typeof(ScriptBlockToPowerShellNotSupportedException).FullName
        /// </summary>
        public RunspaceOpenModuleLoadException()
            : base(typeof(ScriptBlockToPowerShellNotSupportedException).FullName)
        {
        }

        /// <summary>
        /// Initializes a new instance of ScriptBlockToPowerShellNotSupportedException setting the message
        /// </summary>
        /// <param name="message">the exception's message</param>
        public RunspaceOpenModuleLoadException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of ScriptBlockToPowerShellNotSupportedException setting the message and innerException
        /// </summary>
        /// <param name="message">the exception's message</param>
        /// <param name="innerException">the exceptions's inner exception</param>
        public RunspaceOpenModuleLoadException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Recommended constructor for the class
        /// </summary>
        /// <param name="moduleName">The name of the module that cause the error</param>
        /// <param name="errors">The collection of errors that occurred during module processing</param>
        internal RunspaceOpenModuleLoadException(
            string moduleName,
            PSDataCollection<ErrorRecord> errors)
            : base(StringUtil.Format(RunspaceStrings.ErrorLoadingModulesOnRunspaceOpen, moduleName,
                (errors != null && errors.Count > 0 && errors[0] != null) ? errors[0].ToString() : String.Empty), null)
        {
            _errors = errors;
            this.SetErrorId("ErrorLoadingModulesOnRunspaceOpen");
            this.SetErrorCategory(ErrorCategory.OpenError);
        }

        #endregion ctor

        /// <summary>
        /// The collection of error records generated while loading the modules.
        /// </summary>
        public PSDataCollection<ErrorRecord> ErrorRecords
        {
            get { return _errors; }
        }
        PSDataCollection<ErrorRecord> _errors;

        #region Serialization
        /// <summary>
        /// Initializes a new instance of RunspaceOpenModuleLoadException with serialization parameters
        /// </summary>
        /// <param name="info"> serialization information </param>
        /// <param name="context"> streaming context </param>
        protected RunspaceOpenModuleLoadException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }


        /// <summary>
        /// Populates a <see cref="SerializationInfo"/> with the
        /// data needed to serialize the RunspaceOpenModuleLoadException object.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> to populate with data.</param>
        /// <param name="context">The destination for this serialization.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new PSArgumentNullException("info");
            }

            base.GetObjectData(info, context);
        }

        #endregion Serialization



    } // RunspaceOpenException

    #endregion Helper Class
}




