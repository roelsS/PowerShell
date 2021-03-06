//
//    Copyright (C) Microsoft.  All rights reserved.
//
using System;
using System.Management.Automation;

namespace Microsoft.PowerShell.Commands
{
    /// <summary>
    /// Registers for an event on an object.
    /// </summary>
    [Cmdlet(VerbsLifecycle.Register, "ObjectEvent", HelpUri = "http://go.microsoft.com/fwlink/?LinkID=135244")]
    [OutputType(typeof(PSEventJob))]
    public class RegisterObjectEventCommand : ObjectEventRegistrationBase
    {
        #region parameters

        /// <summary>
        /// The object on which to subscribe
        /// </summary>
        [Parameter(Mandatory = true, Position = 0)]
        public PSObject InputObject
        {
            get
            {
                return inputObject;
            }
            set
            {
                inputObject = value;
            }
        }
        private PSObject inputObject = null;

        /// <summary>
        /// The event name to subscribe
        /// </summary>
        [Parameter(Mandatory = true, Position = 1)]
        public string EventName
        {
            get
            {
                return eventName;
            }
            set
            {
                eventName = value;
            }
        }
        private string eventName = null;

        #endregion parameters

        /// <summary>
        /// Returns the object that generates events to be monitored
        /// </summary>
        protected override Object GetSourceObject()
        {
            return inputObject;
        }

        /// <summary>
        /// Returns the event name to be monitored on the input object
        /// </summary>
        protected override String GetSourceObjectEventName()
        {
            return eventName;
        }
    }
}